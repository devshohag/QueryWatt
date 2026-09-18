using System.Data;

namespace QueryWatt.Core.Instrumentation;

/// <summary>
/// Parameter metadata. Parameter <em>values</em> are never carried here — only whether the value
/// was null, which is all <see cref="ScenarioKey"/> needs. Contract v2 §13.
/// </summary>
public sealed record CommandParameterInfo(
    string Name,
    DbType Type,
    bool IsNull,
    int? Size = null,
    byte? Precision = null,
    byte? Scale = null);

/// <summary>A diagnostic raised while measuring. Never surfaced to the application as a throw.</summary>
public sealed record MeasurementDiagnostic(DiagnosticCode Code, string Message);

/// <summary>
/// One database command observed inside a scope. Every metric is nullable: a metric that was not
/// acquired stays null and is never estimated, defaulted or inferred. Contract v2 §5.
/// </summary>
public sealed record MeasuredCommand(
    int Ordinal,
    string CommandText,
    CommandType CommandType,
    MeasurementSource Source,
    IReadOnlyList<CommandParameterInfo> Parameters,
    CommandOutcome Outcome)
{
    public double? ClientDurationMilliseconds { get; init; }
    public long? LogicalReads { get; init; }
    public long? LobLogicalReads { get; init; }
    public long? PhysicalReads { get; init; }
    public long? ReadAheadReads { get; init; }
    public long? LobPhysicalReads { get; init; }
    public long? LobReadAheadReads { get; init; }
    public long? CpuTimeMilliseconds { get; init; }
    public long? ServerElapsedTimeMilliseconds { get; init; }
    public long? RowsReturned { get; init; }
    public long? ServerRoundtrips { get; init; }
    public string? PlanFingerprint { get; init; }
    public string? ExceptionType { get; init; }
    public string? ExceptionMessage { get; init; }
    public int? SqlErrorNumber { get; init; }
    public IReadOnlyList<string> RawMessages { get; init; } = Array.Empty<string>();

    /// <summary>True when every metric that can fail a build was actually acquired.</summary>
    public bool HasGatingMetrics =>
        LogicalReads is not null
        && CpuTimeMilliseconds is not null
        && ClientDurationMilliseconds is not null;
}
