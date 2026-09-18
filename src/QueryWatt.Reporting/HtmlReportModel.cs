using QueryWatt.Baselines.InApp;

namespace QueryWatt.Reporting;

/// <summary>
/// Everything the HTML report shows, in one shape. The page renders this and nothing else, so the
/// file on disk and the numbers behind it can never drift apart.
/// </summary>
public sealed record HtmlReportModel(
    DateTimeOffset GeneratedUtc,
    string ToolVersion,
    string Headline,
    int ScopeCount,
    int CommandCount,
    long? TotalLogicalReads,
    int QueryCount,
    int FailureCount,
    IReadOnlyList<HtmlReportRow> Rows,
    IReadOnlyList<HtmlReportRepetition> Repetitions,
    IReadOnlyList<string> Notes)
{
    /// <summary>True when a baseline was available, so the page can show verdicts.</summary>
    public bool HasBaseline => Rows.Any(row => row.Verdict is not null);
}

/// <summary>One query, with its measurement and — when there is a baseline — its verdict.</summary>
public sealed record HtmlReportRow(
    string QueryId,
    string ScenarioKey,
    string Source,
    int Executions,
    long? LogicalReads,
    long? RowsReturned,
    double? ReadsPerRow,
    double DurationMilliseconds,
    string? Verdict,
    string? Reason,
    long? BaselineLogicalReads,
    long? BaselineRowsReturned,
    double? BaselineReadsPerRow,
    IReadOnlyList<string> Diagnostics);

/// <summary>One command that ran many times inside a single scope.</summary>
public sealed record HtmlReportRepetition(
    string QueryId,
    string CommandText,
    int Repetitions,
    long? TotalLogicalReads,
    double TotalDurationMilliseconds);

/// <summary>Builds the page's model from what a run produced.</summary>
public static class HtmlReportModelFactory
{
    /// <summary>Combines an observation with an optional comparison.</summary>
    /// <param name="observation">What the run did.</param>
    /// <param name="verification">The verdicts, when a baseline existed.</param>
    /// <param name="toolVersion">Version stamped on the page.</param>
    /// <returns>The model the page renders.</returns>
    public static HtmlReportModel Create(
        ObservationReport observation,
        InAppVerificationResult? verification,
        string toolVersion)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var verdicts = verification?.Results ?? [];

        var rows = observation.Queries
            .Select(query =>
            {
                var verdict = verdicts.FirstOrDefault(result =>
                    string.Equals(result.QueryId, query.QueryId, StringComparison.Ordinal)
                    && string.Equals(result.ScenarioKey, query.ScenarioKey, StringComparison.Ordinal)
                    && string.Equals(result.Source, query.Source, StringComparison.Ordinal));

                return new HtmlReportRow(
                    query.QueryId,
                    query.ScenarioKey,
                    query.Source,
                    query.Executions,
                    query.MedianLogicalReads,
                    query.MedianRowsReturned,
                    query.ReadsPerRow,
                    query.MedianDurationMilliseconds,
                    verdict?.Verdict.ToString(),
                    verdict?.Reason,
                    verdict?.Baseline?.LogicalReads,
                    verdict?.Baseline?.RowsReturned,
                    verdict?.Baseline?.ReadsPerRow,
                    query.Diagnostics);
            })
            .ToArray();

        var repetitions = observation.Repetitions
            .Select(repetition => new HtmlReportRepetition(
                repetition.QueryId,
                repetition.CommandText,
                repetition.Repetitions,
                repetition.TotalLogicalReads,
                repetition.TotalDurationMilliseconds))
            .ToArray();

        var failures = verdicts.Count(result => result.Fails);

        return new HtmlReportModel(
            observation.GeneratedUtc,
            toolVersion,
            Headline(verification, failures),
            observation.ScopeCount,
            observation.CommandCount,
            observation.TotalLogicalReads,
            rows.Length,
            failures,
            rows,
            repetitions,
            observation.Notes);
    }

    private static string Headline(InAppVerificationResult? verification, int failures)
    {
        if (verification is null)
        {
            return "Observed";
        }

        return failures > 0
            ? $"{failures} {(failures == 1 ? "query" : "queries")} regressed"
            : verification.Verdict switch
            {
                InAppVerdict.NewScenario => "New scenarios, nothing to compare yet",
                InAppVerdict.Incomparable => "Not comparable with the baseline",
                InAppVerdict.Suspect => "Worth a look",
                InAppVerdict.DataChange => "Data grew, queries held",
                InAppVerdict.Improved => "Improved",
                _ => "No regressions"
            };
    }
}
