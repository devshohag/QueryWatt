namespace QueryWatt.Baselines.InApp;

/// <summary>Schema constants for the in-app baseline file. Contract v2 §14.</summary>
public static class InAppBaselineContract
{
    /// <summary>Schema version of the in-app baseline. Runner-mode baselines stay at 1.</summary>
    public const int SchemaVersion = 2;

    /// <summary>The default file name, kept short because it lives in the repository root.</summary>
    public const string DefaultFileName = "querywatt-baseline.json";
}

/// <summary>
/// An accepted baseline: one entry per query id, scenario and source, with the numbers a later run
/// is judged against.
/// </summary>
/// <remarks>
/// This file is committed and reviewed like source. That is why it holds one small summary per
/// scenario rather than every raw run — a diff should show that a query got heavier, not scroll past
/// a thousand samples.
/// </remarks>
public sealed record InAppBaselineDocument(
    int SchemaVersion,
    string ToolVersion,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<InAppBaselineEntry> Entries)
{
    /// <summary>Finds the entry a measurement should be judged against, if there is one.</summary>
    /// <param name="queryId">The query id.</param>
    /// <param name="scenarioKey">The scenario key.</param>
    /// <param name="source">The stack the measurement came from.</param>
    /// <returns>The matching entry, or null when this scenario has never been accepted.</returns>
    public InAppBaselineEntry? Find(string queryId, string scenarioKey, string source) =>
        Entries.FirstOrDefault(entry =>
            string.Equals(entry.QueryId, queryId, StringComparison.Ordinal)
            && string.Equals(entry.ScenarioKey, scenarioKey, StringComparison.Ordinal)
            && string.Equals(entry.Source, source, StringComparison.Ordinal));
}

/// <summary>One accepted scenario.</summary>
/// <remarks>
/// <paramref name="Source"/> is part of the identity, not decoration. The same logical query
/// measured through Dapper and through EF Core produces different SQL and different parameter
/// shapes, so comparing one against the other would report a regression that nobody caused.
/// Contract v2 §1.1.
/// </remarks>
public sealed record InAppBaselineEntry(
    string QueryId,
    string ScenarioKey,
    string Source,
    DateTimeOffset AcceptedUtc,
    int Runs,
    InAppMetrics Metrics)
{
    /// <summary>The plan the accepted runs used, when plan capture was available.</summary>
    public string? PlanFingerprint { get; init; }

    /// <summary>The server the accepted runs were measured on, when it was probed.</summary>
    public string? EnvironmentFingerprint { get; init; }

    /// <summary>The data the accepted runs were measured against, when it was probed.</summary>
    public string? DatasetFingerprint { get; init; }

    /// <summary>Per-query thresholds, when they differ from the defaults.</summary>
    public InAppThresholds? Thresholds { get; init; }

    /// <summary>Why this scenario was accepted, when a developer said.</summary>
    public string? Note { get; init; }
}

/// <summary>The measured numbers for one scenario, as medians across the accepted runs.</summary>
/// <remarks>
/// Medians, not averages: a single cold run should not move a committed baseline.
/// <see cref="ReadsPerRow"/> is the metric verdicts are keyed on, because raw reads climb with a
/// table that is simply growing, while reads per row does not.
/// </remarks>
public sealed record InAppMetrics(
    long LogicalReads,
    long CpuTimeMilliseconds,
    long RowsReturned,
    double DurationMilliseconds,
    double ReadsPerRow);

/// <summary>How much worse a measurement may get before it is called a regression.</summary>
/// <param name="ReadsPerRowIncreaseRatio">Allowed growth in reads per row, as a fraction.</param>
/// <param name="CpuIncreaseRatio">Allowed growth in CPU time, as a fraction.</param>
/// <param name="MinimumLogicalReads">
/// Below this many reads, ratios are noise — a query going from two reads to three is not a
/// regression, and saying so would teach developers to ignore QueryWatt.
/// </param>
public sealed record InAppThresholds(
    double ReadsPerRowIncreaseRatio = 0.10d,
    double CpuIncreaseRatio = 0.25d,
    long MinimumLogicalReads = 16L)
{
    /// <summary>The thresholds used when a baseline entry names none.</summary>
    public static InAppThresholds Default { get; } = new();
}
