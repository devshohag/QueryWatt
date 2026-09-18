using System.Text.RegularExpressions;
using QueryWatt.Core.Instrumentation;

namespace QueryWatt.Reporting;

/// <summary>
/// What an application actually did to the database during a run: every measured scope rolled up by
/// query, plus the repetitions that look like an N+1.
/// </summary>
/// <remarks>
/// Observe mode judges nothing. It has no baseline and returns no verdict — it exists so a developer
/// can look at a run and see where the reads went, and so the first question ("is anything obviously
/// silly here?") can be answered before any baseline exists.
/// </remarks>
public sealed record ObservationReport(
    DateTimeOffset GeneratedUtc,
    int ScopeCount,
    int CommandCount,
    long? TotalLogicalReads,
    IReadOnlyList<ObservedQuery> Queries,
    IReadOnlyList<RepeatedCommandFinding> Repetitions,
    IReadOnlyList<string> Notes);

/// <summary>One query id and scenario, summarised across every execution in the run.</summary>
public sealed record ObservedQuery(
    string QueryId,
    string ScenarioKey,
    string Source,
    int Executions,
    int CommandsPerExecution,
    long? MedianLogicalReads,
    long? MedianRowsReturned,
    double MedianDurationMilliseconds,
    double? ReadsPerRow,
    bool FullyMeasured,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// One command text that ran many times inside a single scope — the shape of an N+1.
/// </summary>
/// <remarks>
/// This is the finding a per-query tool cannot make. Each of these executions is individually cheap
/// and individually innocent; only counting them per request shows the problem, and only the
/// application knows where a request begins and ends.
/// </remarks>
public sealed record RepeatedCommandFinding(
    string QueryId,
    string CommandText,
    int Repetitions,
    long? TotalLogicalReads,
    double TotalDurationMilliseconds);

/// <summary>Builds an <see cref="ObservationReport"/> from the records a run produced.</summary>
public static partial class ObservationReportFactory
{
    /// <summary>A command text repeated at least this many times in one scope is reported.</summary>
    public const int DefaultRepetitionThreshold = 5;

    /// <summary>Rolls a run's records up into a report.</summary>
    /// <param name="records">Root records, as written by the sink.</param>
    /// <param name="repetitionThreshold">How many repetitions in one scope count as a finding.</param>
    /// <param name="generatedUtc">Timestamp to stamp the report with; defaults to now.</param>
    /// <returns>The report.</returns>
    public static ObservationReport Create(
        IEnumerable<MeasurementRecord> records,
        int repetitionThreshold = DefaultRepetitionThreshold,
        DateTimeOffset? generatedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentOutOfRangeException.ThrowIfLessThan(repetitionThreshold, 2);

        var roots = records.ToArray();
        var flattened = roots.SelectMany(Flatten).ToArray();

        var queries = flattened
            .GroupBy(record => (record.QueryId, record.ScenarioKey, record.Source))
            .Select(group => Summarise(group.Key.QueryId, group.Key.ScenarioKey, group.Key.Source, group.ToArray()))
            .OrderByDescending(query => query.MedianLogicalReads ?? 0L)
            .ThenBy(query => query.QueryId, StringComparer.Ordinal)
            .ToArray();

        var repetitions = roots
            .SelectMany(root => FindRepetitions(root, repetitionThreshold))
            .OrderByDescending(finding => finding.Repetitions)
            .ToArray();

        var commandCount = flattened.Sum(record => record.OwnCommandCount);
        var totalReads = SumOrNull(flattened.SelectMany(record => record.Commands).Select(command => command.LogicalReads));

        return new ObservationReport(
            generatedUtc ?? DateTimeOffset.UtcNow,
            flattened.Length,
            commandCount,
            totalReads,
            queries,
            repetitions,
            BuildNotes(flattened, queries));
    }

    private static IEnumerable<MeasurementRecord> Flatten(MeasurementRecord record)
    {
        yield return record;

        foreach (var child in record.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static ObservedQuery Summarise(
        string queryId,
        string scenarioKey,
        MeasurementSource source,
        IReadOnlyList<MeasurementRecord> executions)
    {
        var reads = Median(executions.Select(record => record.TotalLogicalReads));
        var rows = Median(executions.Select(record => record.RowsReturned));
        var duration = Median(executions.Select(record => (long?)Math.Round(record.DurationMilliseconds)));

        var diagnostics = executions
            .SelectMany(record => record.Diagnostics)
            .Select(diagnostic => diagnostic.Code.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        return new ObservedQuery(
            queryId,
            scenarioKey,
            source.ToString(),
            executions.Count,
            (int)Math.Round(executions.Average(record => (double)record.TotalCommandCount)),
            reads,
            rows,
            duration ?? 0d,
            ReadsPerRow(reads, rows),
            executions.All(record => record.MetricsComplete),
            diagnostics);
    }

    private static IEnumerable<RepeatedCommandFinding> FindRepetitions(
        MeasurementRecord root,
        int threshold)
    {
        var commands = Flatten(root).SelectMany(record => record.Commands).ToArray();

        return commands
            .GroupBy(command => Collapse(command.CommandText), StringComparer.Ordinal)
            .Where(group => group.Count() >= threshold)
            .Select(group => new RepeatedCommandFinding(
                root.QueryId,
                group.Key,
                group.Count(),
                SumOrNull(group.Select(command => command.LogicalReads)),
                group.Sum(command => command.ClientDurationMilliseconds ?? 0d)));
    }

    private static IReadOnlyList<string> BuildNotes(
        IReadOnlyList<MeasurementRecord> records,
        IReadOnlyList<ObservedQuery> queries)
    {
        var notes = new List<string>();

        var unmeasured = queries.Count(query => !query.FullyMeasured);
        if (unmeasured > 0)
        {
            notes.Add(
                $"{unmeasured} of {queries.Count} queries have no server-side reads. Run with Full "
                + "instrumentation, and wrap the connection so its readers finish before closing.");
        }

        var abandoned = records.Count(record => record.Status == MeasurementStatus.Abandoned);
        if (abandoned > 0)
        {
            notes.Add($"{abandoned} scopes ended without Complete() or Fail() and were not measured.");
        }

        var failed = records.Count(record => record.Status == MeasurementStatus.Failed);
        if (failed > 0)
        {
            notes.Add($"{failed} scopes failed. Their commands are reported but never baselined.");
        }

        return notes;
    }

    private static double? ReadsPerRow(long? reads, long? rows) =>
        reads is null || rows is null || rows.Value <= 0L
            ? null
            : Math.Round(reads.Value / (double)rows.Value, 2);

    private static long? Median(IEnumerable<long?> values)
    {
        var present = values.Where(value => value.HasValue).Select(value => value!.Value).Order().ToArray();
        if (present.Length == 0)
        {
            return null;
        }

        var middle = present.Length / 2;
        return present.Length % 2 == 1
            ? present[middle]
            : (present[middle - 1] + present[middle]) / 2L;
    }

    private static long? SumOrNull(IEnumerable<long?> values)
    {
        var total = 0L;
        var sawValue = false;

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            total += value.Value;
            sawValue = true;
        }

        return sawValue ? total : null;
    }

    /// <summary>
    /// Collapses whitespace so the same statement groups together however it was laid out. Values
    /// are never touched: parameterised commands already share their text, and a command carrying
    /// inline literals is reported as such by its own diagnostic.
    /// </summary>
    private static string Collapse(string commandText) =>
        WhitespaceRegex().Replace(commandText ?? string.Empty, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
