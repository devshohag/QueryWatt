using QueryWatt.Core;
using System.Reflection;
namespace QueryWatt.Baselines;

public static class BaselineContract
{
    public const int SchemaVersion = 1;

    public static string ToolVersion { get; } = ResolveToolVersion();

    private static string ResolveToolVersion()
    {
        var assembly = typeof(BaselineContract).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
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
