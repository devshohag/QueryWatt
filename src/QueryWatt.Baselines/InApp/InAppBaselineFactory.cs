using QueryWatt.Core.Instrumentation;

namespace QueryWatt.Baselines.InApp;

/// <summary>
/// Turns the records a run produced into the summaries a baseline stores, and into the samples a
/// later run is judged from.
/// </summary>
public static class InAppBaselineFactory
{
    /// <summary>
    /// Rolls records up into one sample per query id, scenario and source. Only baseline-eligible
    /// records are used: a scope that failed, was abandoned, or is missing a gating metric is left
    /// out rather than averaged in.
    /// </summary>
    /// <param name="records">Root records from a run.</param>
    /// <returns>One sample per scenario, ordered by query id.</returns>
    public static IReadOnlyList<InAppSample> Summarise(IEnumerable<MeasurementRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        return Flatten(records)
            .Where(record => record.IsBaselineEligible)
            .GroupBy(record => (record.QueryId, record.ScenarioKey, Source: record.Source.ToString()))
            .Select(group => new InAppSample(
                group.Key.QueryId,
                group.Key.ScenarioKey,
                group.Key.Source,
                group.Count(),
                Aggregate(group.ToArray()),
                PlanFingerprint(group.ToArray())))
            .OrderBy(sample => sample.QueryId, StringComparer.Ordinal)
            .ThenBy(sample => sample.ScenarioKey, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Writes a fresh baseline document from a run's samples.</summary>
    /// <param name="samples">Samples to accept.</param>
    /// <param name="toolVersion">The version stamped into the file.</param>
    /// <param name="environmentFingerprint">The server, when it was probed.</param>
    /// <param name="createdUtc">Timestamp to stamp; defaults to now.</param>
    /// <returns>The document to store.</returns>
    public static InAppBaselineDocument CreateDocument(
        IEnumerable<InAppSample> samples,
        string toolVersion,
        string? environmentFingerprint = null,
        DateTimeOffset? createdUtc = null)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var stamp = createdUtc ?? DateTimeOffset.UtcNow;

        var entries = samples
            .Select(sample => new InAppBaselineEntry(
                sample.QueryId,
                sample.ScenarioKey,
                sample.Source,
                stamp,
                sample.Runs,
                sample.Metrics)
            {
                PlanFingerprint = sample.PlanFingerprint,
                EnvironmentFingerprint = environmentFingerprint
            })
            .ToArray();

        return new InAppBaselineDocument(
            InAppBaselineContract.SchemaVersion,
            toolVersion,
            stamp,
            entries);
    }

    /// <summary>
    /// Merges newly accepted samples into an existing baseline: scenarios present in both are
    /// replaced, scenarios only in the baseline are kept.
    /// </summary>
    /// <param name="existing">The baseline on disk, or null when there is none yet.</param>
    /// <param name="samples">Samples being accepted now.</param>
    /// <param name="toolVersion">The version stamped into the file.</param>
    /// <param name="environmentFingerprint">The server, when it was probed.</param>
    /// <param name="acceptedUtc">Timestamp to stamp on the accepted entries; defaults to now.</param>
    /// <returns>The merged document.</returns>
    public static InAppBaselineDocument Merge(
        InAppBaselineDocument? existing,
        IEnumerable<InAppSample> samples,
        string toolVersion,
        string? environmentFingerprint = null,
        DateTimeOffset? acceptedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var accepted = CreateDocument(samples, toolVersion, environmentFingerprint, acceptedUtc);

        if (existing is null)
        {
            return accepted;
        }

        var replaced = accepted.Entries
            .Select(entry => (entry.QueryId, entry.ScenarioKey, entry.Source))
            .ToHashSet();

        var kept = existing.Entries
            .Where(entry => !replaced.Contains((entry.QueryId, entry.ScenarioKey, entry.Source)));

        var merged = kept
            .Concat(accepted.Entries)
            .OrderBy(entry => entry.QueryId, StringComparer.Ordinal)
            .ThenBy(entry => entry.ScenarioKey, StringComparer.Ordinal)
            .ToArray();

        return accepted with { Entries = merged, CreatedUtc = existing.CreatedUtc };
    }

    private static IEnumerable<MeasurementRecord> Flatten(IEnumerable<MeasurementRecord> records)
    {
        foreach (var record in records)
        {
            yield return record;

            foreach (var child in Flatten(record.Children))
            {
                yield return child;
            }
        }
    }

    private static InAppMetrics Aggregate(IReadOnlyList<MeasurementRecord> runs)
    {
        var reads = Median(runs.Select(run => run.TotalLogicalReads ?? 0L));
        var rows = Median(runs.Select(run => run.RowsReturned ?? 0L));

        return new InAppMetrics(
            reads,
            Median(runs.Select(run => run.TotalCpuTimeMilliseconds ?? 0L)),
            rows,
            MedianDouble(runs.Select(run => run.DurationMilliseconds)),
            ReadsPerRow(reads, rows));
    }

    private static string? PlanFingerprint(IReadOnlyList<MeasurementRecord> runs)
    {
        var fingerprints = runs
            .SelectMany(run => run.Commands)
            .Select(command => command.PlanFingerprint)
            .Where(fingerprint => !string.IsNullOrWhiteSpace(fingerprint))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Several distinct plans across the accepted runs means the plan is not a stable property
        // of this scenario, so there is nothing honest to record.
        return fingerprints.Length == 1 ? fingerprints[0] : null;
    }

    /// <summary>Reads per row, with a row count of zero treated as one row.</summary>
    /// <param name="reads">Logical reads.</param>
    /// <param name="rows">Rows returned.</param>
    /// <returns>Reads per row, rounded to two decimals.</returns>
    public static double ReadsPerRow(long reads, long rows) =>
        Math.Round(reads / (double)Math.Max(rows, 1L), 2);

    private static long Median(IEnumerable<long> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            return 0L;
        }

        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2L;
    }

    private static double MedianDouble(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            return 0d;
        }

        var middle = ordered.Length / 2;
        var median = ordered.Length % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2d;

        return Math.Round(median, 2);
    }
}

/// <summary>One scenario's numbers from a single run, before they are accepted or judged.</summary>
public sealed record InAppSample(
    string QueryId,
    string ScenarioKey,
    string Source,
    int Runs,
    InAppMetrics Metrics,
    string? PlanFingerprint = null)
{
    /// <summary>The server this sample was measured on, when it was probed.</summary>
    public string? EnvironmentFingerprint { get; init; }

    /// <summary>The data this sample was measured against, when it was probed.</summary>
    public string? DatasetFingerprint { get; init; }
}
