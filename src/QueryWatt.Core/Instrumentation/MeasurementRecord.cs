namespace QueryWatt.Core.Instrumentation;

/// <summary>
/// The finished result of one measurement scope. One scope execution is one <em>run</em>, so a
/// baseline-eligible record converts into a single <see cref="RunMetrics"/> and flows through the
/// existing statistics, baseline and verdict pipeline unchanged. Contract v2 §1.
/// </summary>
public sealed record MeasurementRecord(
    string QueryId,
    string ScenarioKey,
    MeasurementSource Source,
    MeasurementStatus Status,
    int Depth,
    string? ParentQueryId,
    DateTimeOffset StartedUtc,
    double DurationMilliseconds,
    IReadOnlyList<MeasuredCommand> Commands,
    IReadOnlyList<MeasurementRecord> Children,
    IReadOnlyList<MeasurementDiagnostic> Diagnostics)
{
    public string? ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }

    /// <summary>Rows supplied by the caller through <c>Complete(rowsReturned)</c>, when given.</summary>
    public long? CallerReportedRows { get; init; }

    /// <summary>True when the scope's own commands all carry the metrics a verdict needs.</summary>
    public bool MetricsComplete =>
        Commands.Count > 0 && Commands.All(command => command.HasGatingMetrics);

    /// <summary>A record enters a baseline only when it completed cleanly and measured everything.</summary>
    public bool IsBaselineEligible => Status == MeasurementStatus.Completed && MetricsComplete;

    /// <summary>Commands executed directly by this scope, excluding its children.</summary>
    public int OwnCommandCount => Commands.Count;

    /// <summary>Commands executed by this scope and every descendant.</summary>
    public int TotalCommandCount =>
        Commands.Count + Children.Sum(child => child.TotalCommandCount);

    public long? TotalLogicalReads => SumOrNull(command => command.LogicalReads);

    public long? TotalCpuTimeMilliseconds => SumOrNull(command => command.CpuTimeMilliseconds);

    public long? RowsReturned =>
        CallerReportedRows ?? SumOrNull(command => command.RowsReturned);

    /// <summary>
    /// Bridges one scope execution into the v1 pipeline. Returns false — and produces nothing —
    /// when the record is not baseline-eligible, rather than filling missing metrics with zeros.
    /// </summary>
    public bool TryCreateRunMetrics(int runNumber, out RunMetrics? runMetrics)
    {
        if (runNumber < 1 || !IsBaselineEligible)
        {
            runMetrics = null;
            return false;
        }

        var statements = Commands
            .Select(command => new StatementMetrics(
                command.Ordinal,
                command.CpuTimeMilliseconds ?? 0L,
                command.ServerElapsedTimeMilliseconds ?? 0L,
                Array.Empty<TableIoMetrics>()))
            .ToArray();

        var rawMessages = Commands
            .SelectMany(command => command.RawMessages)
            .ToArray();

        runMetrics = new RunMetrics(
            runNumber,
            Sum(command => command.LogicalReads),
            Sum(command => command.LobLogicalReads),
            Sum(command => command.PhysicalReads),
            Sum(command => command.ReadAheadReads),
            Sum(command => command.LobPhysicalReads),
            Sum(command => command.LobReadAheadReads),
            Sum(command => command.CpuTimeMilliseconds),
            DurationMilliseconds,
            RowsReturned ?? 0L,
            statements,
            rawMessages);

        return true;
    }

    private long Sum(Func<MeasuredCommand, long?> selector) =>
        Commands.Sum(command => selector(command) ?? 0L);

    private long? SumOrNull(Func<MeasuredCommand, long?> selector)
    {
        var total = 0L;
        var sawValue = false;

        foreach (var command in Commands)
        {
            var value = selector(command);
            if (value is null)
            {
                continue;
            }

            total += value.Value;
            sawValue = true;
        }

        return sawValue ? total : null;
    }
}
