namespace QueryWatt.Core;

public sealed record TableIoMetrics(
    string TableName,
    int Occurrence,
    long ScanCount,
    long LogicalReads,
    long PhysicalReads,
    long ReadAheadReads,
    long LobLogicalReads,
    long LobPhysicalReads,
    long LobReadAheadReads);

public sealed record StatementMetrics(
    int Ordinal,
    long CpuTimeMilliseconds,
    long ServerElapsedTimeMilliseconds,
    IReadOnlyList<TableIoMetrics> TableIo);

public sealed record RunMetrics(
    int RunNumber,
    long LogicalReads,
    long LobLogicalReads,
    long PhysicalReads,
    long ReadAheadReads,
    long LobPhysicalReads,
    long LobReadAheadReads,
    long CpuTimeMilliseconds,
    double ClientDurationMilliseconds,
    long RowsReturned,
    IReadOnlyList<StatementMetrics> Statements,
    IReadOnlyList<string> RawMessages);

public sealed record QueryMeasurementSample(
    string QueryName,
    int WarmupRuns,
    IReadOnlyList<RunMetrics> Runs);
