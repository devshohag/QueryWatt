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
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(baseline);

        var storedQueries = ValidateBaselineAgainstConfiguration(configuration, baseline);

        var environment = await _environmentInspector
            .InspectAsync(configuration.TableNames, cancellationToken)
            .ConfigureAwait(false);
        var currentFingerprint = CreateEnvironmentFingerprint(configuration, environment);
        var comparison = CompareEnvironments(baseline.Environment, currentFingerprint);

        if (comparison.Blocking.Count > 0)
        {
            // Changing the schema or the seed data is a normal thing to do in a pull
            // request. It makes the stored numbers meaningless, but it is not a broken
            // tool, so it gets its own verdict and a report instead of a bare refusal.
            if (comparison.Blocking.All(difference =>
                    string.Equals(difference.Field, "seedSha256", StringComparison.Ordinal)))
            {
                return new VerificationResult(
                    VerificationVerdict.BaselineStale,
                    2,
                    [],
                    [
                        "The database seed or schema changed since this baseline was approved, "
                        + "so the stored numbers describe a different database. "
                        + "Run 'querywatt baseline' again and commit the new baseline file.",
                        .. comparison.Warnings
                    ]);
            }

            throw new EnvironmentMismatchException(
                "Baseline environment mismatch: "
                + string.Join("; ", comparison.Blocking.Select(difference => difference.Description)));
        }

        var session = await new MeasurementSessionRunner(_measurementRunner)
            .MeasureAsync(configuration.Requests, cancellationToken)
            .ConfigureAwait(false);

        var results = new List<QueryVerificationResult>(configuration.Queries.Count);
        for (var index = 0; index < configuration.Queries.Count; index++)
        {
            var queryName = configuration.Queries[index].Request.Name;
            var baselineQuery = storedQueries[queryName];
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

            // The query text changes in almost every pull request this tool is meant
            // to review, so a combined flag would always read "changed" and carry no
            // signal. The plan shape is the part worth a reviewer's attention.
            var current = currentSample.EffectivePlanFingerprints;
            var queryTextChanged = FingerprintsDiffer(
                baselineQuery.PlanFingerprints,
                current,
                fingerprint => fingerprint.QueryHash);
            var planShapeChanged = FingerprintsDiffer(
                baselineQuery.PlanFingerprints,
                current,
                fingerprint => fingerprint.QueryPlanHash);

            results.Add(new QueryVerificationResult(
                queryName,
                metrics.Any(metric => metric.Regressed),
                queryTextChanged,
                planShapeChanged,
                metrics));
        }

        var regressed = results.Any(query => query.Regressed);
        return new VerificationResult(
            regressed ? VerificationVerdict.Regressed : VerificationVerdict.Passed,
            regressed ? 1 : 0,
            results,
            comparison.Warnings);
    }

    private static bool FingerprintsDiffer(
        IReadOnlyList<StatementPlanFingerprint> baseline,
        IReadOnlyList<StatementPlanFingerprint> current,
        Func<StatementPlanFingerprint, string> selector) =>
        !baseline.Select(selector).SequenceEqual(current.Select(selector), StringComparer.Ordinal);

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

    // Queries are matched by name, never by position. Reordering entries in
    // querywatt.yml is a harmless edit and must not force a re-baseline.
    private static IReadOnlyDictionary<string, BaselineQuery> ValidateBaselineAgainstConfiguration(
        ResolvedQueryWattConfiguration configuration,
        BaselineDocument baseline)
    {
        var stored = new Dictionary<string, BaselineQuery>(StringComparer.OrdinalIgnoreCase);
        foreach (var query in baseline.Queries)
        {
            if (!stored.TryAdd(query.Name, query))
            {
                throw new BaselineConfigurationException(
                    $"The baseline contains more than one query named '{query.Name}'; run baseline again.");
            }
        }

        var configuredNames = configuration.Queries
            .Select(query => query.Request.Name)
            .ToArray();

        var missingFromBaseline = configuredNames
            .Where(name => !stored.ContainsKey(name))
            .ToArray();
        var noLongerConfigured = stored.Keys
            .Where(name => !configuredNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (missingFromBaseline.Length > 0 || noLongerConfigured.Length > 0)
        {
            var differences = new List<string>();
            if (missingFromBaseline.Length > 0)
            {
                differences.Add(
                    "configured but not in the baseline: " + string.Join(", ", missingFromBaseline));
            }

            if (noLongerConfigured.Length > 0)
            {
                differences.Add(
                    "in the baseline but no longer configured: " + string.Join(", ", noLongerConfigured));
            }

            throw new BaselineConfigurationException(
                "Configured queries and baseline queries differ ("
                + string.Join("; ", differences)
                + "). Run baseline again.");
        }

        foreach (var configured in configuration.Queries)
        {
            var storedQuery = stored[configured.Request.Name];
            if (configured.Thresholds != storedQuery.Thresholds)
            {
                throw new BaselineConfigurationException(
                    $"Thresholds for query '{configured.Request.Name}' differ from the baseline; "
                    + "run baseline again.");
            }

            var parameterHash = FingerprintCalculator.CalculateParameterSet(configured.Request);
            if (!string.Equals(parameterHash, storedQuery.ParameterSetSha256, StringComparison.Ordinal))
            {
                throw new EnvironmentMismatchException(
                    $"Parameter set differs for query '{configured.Request.Name}'.");
            }
        }

        return stored;
    }

    private static EnvironmentComparison CompareEnvironments(
        BaselineEnvironmentFingerprint baseline,
        BaselineEnvironmentFingerprint current)
    {
        var blocking = new List<EnvironmentDifference>();
        AddDifference(
            blocking,
            "sqlServerMajorVersion",
            MajorMinorVersion(baseline.SqlServerProductVersion),
            MajorMinorVersion(current.SqlServerProductVersion));
        AddDifference(blocking, "sqlServerEdition", baseline.SqlServerEdition, current.SqlServerEdition);
        AddDifference(blocking, "containerImageTag", baseline.ContainerImageTag, current.ContainerImageTag);
        AddDifference(blocking, "pinnedSetOptionsSha256", baseline.PinnedSetOptionsSha256, current.PinnedSetOptionsSha256);
        AddDifference(blocking, "seedSha256", baseline.SeedSha256, current.SeedSha256);
        AddDifference(blocking, "warmupRuns", baseline.WarmupRuns, current.WarmupRuns);
        AddDifference(blocking, "measuredRuns", baseline.MeasuredRuns, current.MeasuredRuns);

        var warnings = new List<string>();
        if (!string.Equals(
                baseline.SqlServerProductVersion,
                current.SqlServerProductVersion,
                StringComparison.Ordinal))
        {
            warnings.Add(
                $"SQL Server build differs: baseline '{baseline.SqlServerProductVersion}', "
                + $"current '{current.SqlServerProductVersion}'. The major version matches, "
                + "so the comparison still ran.");
        }

        return new EnvironmentComparison(blocking, warnings);
    }

    // Only the major.minor pair gates a comparison. A cumulative update moves the
    // build number without changing the optimizer contract, and a gate that fails
    // because the container was patched is a gate people switch off. The full
    // product version stays in the baseline document for provenance.
    private static string MajorMinorVersion(string productVersion)
    {
        if (string.IsNullOrWhiteSpace(productVersion))
        {
            return string.Empty;
        }

        var parts = productVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : parts[0];
    }

    private static void AddDifference<T>(
        ICollection<EnvironmentDifference> differences,
        string name,
        T baseline,
        T current)
    {
        if (!EqualityComparer<T>.Default.Equals(baseline, current))
        {
            differences.Add(new EnvironmentDifference(
                name,
                baseline?.ToString() ?? "null",
                current?.ToString() ?? "null"));
        }
    }

    private sealed record EnvironmentDifference(string Field, string Baseline, string Current)
    {
        public string Description => $"{Field} (baseline '{Baseline}', current '{Current}')";
    }

    private sealed record EnvironmentComparison(
        IReadOnlyList<EnvironmentDifference> Blocking,
        IReadOnlyList<string> Warnings);

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

public static class VerificationVerdict
{
    public const string Passed = "passed";
    public const string Regressed = "regressed";
    public const string BaselineStale = "baseline-stale";
}

public sealed record VerificationResult(
    string Verdict,
    int ExitCode,
    IReadOnlyList<QueryVerificationResult> Queries,
    IReadOnlyList<string> Warnings);

public sealed record QueryVerificationResult(
    string QueryName,
    bool Regressed,
    bool QueryTextChanged,
    bool PlanShapeChanged,
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
