using QueryWatt.Configuration;
using QueryWatt.Core;

namespace QueryWatt.Baselines;

public sealed class BaselineWorkflow(
    IMeasurementRunner measurementRunner,
    IMeasurementEnvironmentInspector environmentInspector,
    string pinnedSetOptions)
{
    private readonly IMeasurementRunner _measurementRunner = measurementRunner;
    private readonly IMeasurementEnvironmentInspector _environmentInspector = environmentInspector;
    private readonly string _pinnedSetOptions = pinnedSetOptions;

    public async Task<BaselineDocument> CreateAsync(
        ResolvedQueryWattConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var environment = await _environmentInspector
            .InspectAsync(configuration.TableNames, cancellationToken)
            .ConfigureAwait(false);
        var fingerprint = CreateEnvironmentFingerprint(configuration, environment);
        var session = await new MeasurementSessionRunner(_measurementRunner)
            .MeasureAsync(configuration.Requests, cancellationToken)
            .ConfigureAwait(false);

        var queries = configuration.Queries
            .Zip(session.Samples, session.Summaries)
            .Select(tuple => CreateBaselineQuery(tuple.First, tuple.Second, tuple.Third))
            .ToArray();

        return new BaselineDocument(
            BaselineContract.SchemaVersion,
            BaselineContract.ToolVersion,
            fingerprint,
            queries);
    }

    public async Task<VerificationResult> VerifyAsync(
        ResolvedQueryWattConfiguration configuration,
        BaselineDocument baseline,
        CancellationToken cancellationToken = default)
    {
        ValidateBaselineAgainstConfiguration(configuration, baseline);

        var environment = await _environmentInspector
            .InspectAsync(configuration.TableNames, cancellationToken)
            .ConfigureAwait(false);
        var currentFingerprint = CreateEnvironmentFingerprint(configuration, environment);
        EnsureEnvironmentMatches(baseline.Environment, currentFingerprint);

        var session = await new MeasurementSessionRunner(_measurementRunner)
            .MeasureAsync(configuration.Requests, cancellationToken)
            .ConfigureAwait(false);

        var results = new List<QueryVerificationResult>(configuration.Queries.Count);
        for (var index = 0; index < configuration.Queries.Count; index++)
        {
            var baselineQuery = baseline.Queries[index];
            var currentSummary = session.Summaries[index];
            var currentSample = session.Samples[index];

            var metrics = new List<MetricVerificationResult>
            {
                CompareMetric(
                    "logicalReads",
                    baselineQuery.Summary.LogicalReads,
                    currentSummary.LogicalReads,
                    baselineQuery.Thresholds.LogicalReads,
                    canGate: true,
                    note: null),
                CompareMetric(
                    "lobLogicalReads",
                    baselineQuery.Summary.LobLogicalReads,
                    currentSummary.LobLogicalReads,
                    threshold: null,
                    canGate: false,
                    note: "informational"),
                CompareMetric(
                    "physicalReads",
                    baselineQuery.Summary.PhysicalReads,
                    currentSummary.PhysicalReads,
                    threshold: null,
                    canGate: false,
                    note: "informational"),
                CompareMetric(
                    "readAheadReads",
                    baselineQuery.Summary.ReadAheadReads,
                    currentSummary.ReadAheadReads,
                    threshold: null,
                    canGate: false,
                    note: "informational"),
                CompareMetric(
                    "lobPhysicalReads",
                    baselineQuery.Summary.LobPhysicalReads,
                    currentSummary.LobPhysicalReads,
                    threshold: null,
                    canGate: false,
                    note: "informational"),
                CompareMetric(
                    "lobReadAheadReads",
                    baselineQuery.Summary.LobReadAheadReads,
                    currentSummary.LobReadAheadReads,
                    threshold: null,
                    canGate: false,
                    note: "informational"),
                CompareMetric(
                    "cpuTimeMilliseconds",
                    baselineQuery.Summary.CpuTimeMilliseconds,
                    currentSummary.CpuTimeMilliseconds,
                    baselineQuery.Thresholds.CpuTimeMilliseconds,
                    canGate: baselineQuery.Summary.CpuTimeMilliseconds.FilteredMedian >= 10,
                    note: baselineQuery.Summary.CpuTimeMilliseconds.FilteredMedian < 10
                        ? "below-resolution"
                        : null),
                CompareMetric(
                    "clientDurationMilliseconds",
                    baselineQuery.Summary.ClientDurationMilliseconds,
                    currentSummary.ClientDurationMilliseconds,
                    baselineQuery.Thresholds.ClientDurationMilliseconds,
                    canGate: true,
                    note: baselineQuery.Thresholds.ClientDurationMilliseconds is null
                        ? "informational"
                        : null),
                CompareMetric(
                    "rowsReturned",
                    baselineQuery.Summary.RowsReturned,
                    currentSummary.RowsReturned,
                    threshold: null,
                    canGate: false,
                    note: "informational")
            };

            var planChanged = !baselineQuery.PlanFingerprints.SequenceEqual(
                currentSample.EffectivePlanFingerprints);
            results.Add(new QueryVerificationResult(
                baselineQuery.Name,
                metrics.Any(metric => metric.Regressed),
                planChanged,
                metrics));
        }

        return new VerificationResult(
            results.Any(query => query.Regressed) ? 1 : 0,
            results);
    }

    private BaselineEnvironmentFingerprint CreateEnvironmentFingerprint(
        ResolvedQueryWattConfiguration configuration,
        MeasurementEnvironmentDetails environment) =>
        new(
            environment.SqlServerProductVersion,
            environment.SqlServerEdition,
            configuration.ContainerImageTag,
            FingerprintCalculator.Sha256(_pinnedSetOptions),
            FingerprintCalculator.CalculateSeed(configuration, environment),
            configuration.Requests[0].WarmupRuns,
            configuration.Requests[0].MeasuredRuns);

    private static BaselineQuery CreateBaselineQuery(
        ResolvedQueryConfiguration configured,
        QueryMeasurementSample sample,
        QueryStatisticsSummary summary) =>
        new(
            configured.Request.Name,
            FingerprintCalculator.Sha256(configured.Request.CommandText),
            FingerprintCalculator.CalculateParameterSet(configured.Request),
            configured.Thresholds,
            sample.EffectivePlanFingerprints,
            summary,
            sample.Runs.Select(run => new BaselineRun(
                run.RunNumber,
                run.LogicalReads,
                run.LobLogicalReads,
                run.PhysicalReads,
                run.ReadAheadReads,
                run.LobPhysicalReads,
                run.LobReadAheadReads,
                run.CpuTimeMilliseconds,
                run.ClientDurationMilliseconds,
                run.RowsReturned,
                run.Statements)).ToArray());

    private static void ValidateBaselineAgainstConfiguration(
        ResolvedQueryWattConfiguration configuration,
        BaselineDocument baseline)
    {
        if (!string.Equals(
                baseline.ToolVersion,
                BaselineContract.ToolVersion,
                StringComparison.Ordinal))
        {
            throw new EnvironmentMismatchException(
                $"Tool version differs: baseline '{baseline.ToolVersion}', current '{BaselineContract.ToolVersion}'.");
        }

        if (baseline.Queries.Count != configuration.Queries.Count)
        {
            throw new BaselineConfigurationException(
                "Configured query count differs from the baseline; run baseline again.");
        }

        for (var index = 0; index < configuration.Queries.Count; index++)
        {
            var configured = configuration.Queries[index];
            var stored = baseline.Queries[index];
            if (!string.Equals(configured.Request.Name, stored.Name, StringComparison.Ordinal)
                || configured.Thresholds != stored.Thresholds)
            {
                throw new BaselineConfigurationException(
                    $"Query order, name, or thresholds differ at position {index + 1}; run baseline again.");
            }

            var parameterHash = FingerprintCalculator.CalculateParameterSet(configured.Request);
            if (!string.Equals(parameterHash, stored.ParameterSetSha256, StringComparison.Ordinal))
            {
                throw new EnvironmentMismatchException(
                    $"Parameter set differs for query '{stored.Name}'.");
            }
        }
    }

    private static void EnsureEnvironmentMatches(
        BaselineEnvironmentFingerprint baseline,
        BaselineEnvironmentFingerprint current)
    {
        var differences = new List<string>();
        AddDifference(differences, "sqlServerProductVersion", baseline.SqlServerProductVersion, current.SqlServerProductVersion);
        AddDifference(differences, "sqlServerEdition", baseline.SqlServerEdition, current.SqlServerEdition);
        AddDifference(differences, "containerImageTag", baseline.ContainerImageTag, current.ContainerImageTag);
        AddDifference(differences, "pinnedSetOptionsSha256", baseline.PinnedSetOptionsSha256, current.PinnedSetOptionsSha256);
        AddDifference(differences, "seedSha256", baseline.SeedSha256, current.SeedSha256);
        AddDifference(differences, "warmupRuns", baseline.WarmupRuns, current.WarmupRuns);
        AddDifference(differences, "measuredRuns", baseline.MeasuredRuns, current.MeasuredRuns);

        if (differences.Count > 0)
        {
            throw new EnvironmentMismatchException(
                "Baseline environment mismatch: " + string.Join(", ", differences));
        }
    }

    private static void AddDifference<T>(
        ICollection<string> differences,
        string name,
        T baseline,
        T current)
    {
        if (!EqualityComparer<T>.Default.Equals(baseline, current))
        {
            differences.Add(name);
        }
    }

    private static MetricVerificationResult CompareMetric(
        string name,
        MetricSummary baselineSummary,
        MetricSummary currentSummary,
        RegressionThreshold? threshold,
        bool canGate,
        string? note)
    {
        var baseline = baselineSummary.FilteredMedian;
        var current = currentSummary.FilteredMedian;
        var absoluteChange = current - baseline;
        double? percentChange = baseline == 0
            ? null
            : (absoluteChange / baseline) * 100;
        var relativeExceeded = threshold is not null
            && (baseline == 0
                ? current > 0
                : percentChange!.Value > threshold.Percent);
        var regressed = threshold is not null
            && canGate
            && absoluteChange > threshold.Absolute
            && relativeExceeded;

        return new MetricVerificationResult(
            name,
            baseline,
            current,
            absoluteChange,
            percentChange,
            threshold,
            regressed,
            note,
            baselineSummary.OutlierRunNumbers.Count,
            currentSummary.OutlierRunNumbers.Count,
            baselineSummary.P95,
            currentSummary.P95);
    }
}

public sealed record VerificationResult(
    int ExitCode,
    IReadOnlyList<QueryVerificationResult> Queries);

public sealed record QueryVerificationResult(
    string QueryName,
    bool Regressed,
    bool PlanChanged,
    IReadOnlyList<MetricVerificationResult> Metrics);

public sealed record MetricVerificationResult(
    string MetricName,
    double Baseline,
    double Current,
    double AbsoluteChange,
    double? PercentChange,
    RegressionThreshold? Threshold,
    bool Regressed,
    string? Note,
    int BaselineOutlierCount,
    int CurrentOutlierCount,
    double? BaselineP95,
    double? CurrentP95);
