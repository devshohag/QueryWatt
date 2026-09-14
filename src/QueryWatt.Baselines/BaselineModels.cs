using QueryWatt.Core;

namespace QueryWatt.Baselines;

public static class BaselineContract
{
    public const int SchemaVersion = 1;
    public const string ToolVersion = "0.4.0-preview.1";
}

public sealed record BaselineDocument(
    int SchemaVersion,
    string ToolVersion,
    BaselineEnvironmentFingerprint Environment,
    IReadOnlyList<BaselineQuery> Queries);

public sealed record BaselineEnvironmentFingerprint(
    string SqlServerProductVersion,
    string SqlServerEdition,
    string? ContainerImageTag,
    string PinnedSetOptionsSha256,
    string SeedSha256,
    int WarmupRuns,
    int MeasuredRuns);

public sealed record BaselineQuery(
    string Name,
    string CommandTextSha256,
    string ParameterSetSha256,
    QueryThresholds Thresholds,
    IReadOnlyList<StatementPlanFingerprint> PlanFingerprints,
    QueryStatisticsSummary Summary,
    IReadOnlyList<BaselineRun> Runs);

public sealed record BaselineRun(
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
    IReadOnlyList<StatementMetrics> Statements);
