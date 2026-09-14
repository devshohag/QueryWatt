using QueryWatt.Baselines;
using QueryWatt.Configuration;
using QueryWatt.Core;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class BaselineDetailTests
{
    [Fact]
    public async Task Strip_KeepsEveryFieldAComparisonReads()
    {
        using var fixture = new DetailFixture();
        var configuration = fixture.CreateConfiguration();
        var full = await fixture.CreateWorkflow().CreateAsync(configuration);

        var slim = BaselineDetail.Strip(full);

        var fullReads = full.Queries[0].Summary.LogicalReads;
        var slimReads = slim.Queries[0].Summary.LogicalReads;

        Assert.Equal(fullReads.FilteredMedian, slimReads.FilteredMedian);
        Assert.Equal(fullReads.RawMedian, slimReads.RawMedian);
        Assert.Equal(fullReads.FirstQuartile, slimReads.FirstQuartile);
        Assert.Equal(fullReads.ThirdQuartile, slimReads.ThirdQuartile);
        Assert.Equal(fullReads.LowerOutlierFence, slimReads.LowerOutlierFence);
        Assert.Equal(fullReads.UpperOutlierFence, slimReads.UpperOutlierFence);
        Assert.Equal(fullReads.OutlierRunNumbers, slimReads.OutlierRunNumbers);
        Assert.Equal(fullReads.P95, slimReads.P95);
        Assert.Equal(fullReads.N, slimReads.N);
        Assert.Equal(full.Queries[0].PlanFingerprints, slim.Queries[0].PlanFingerprints);
        Assert.Equal(full.Queries[0].Thresholds, slim.Queries[0].Thresholds);
    }

    [Fact]
    public async Task Strip_DropsTheRawObservationsAndTheRunList()
    {
        using var fixture = new DetailFixture();
        var full = await fixture.CreateWorkflow().CreateAsync(fixture.CreateConfiguration());

        var slim = BaselineDetail.Strip(full);

        Assert.NotNull(full.Queries[0].Runs);
        Assert.Null(slim.Queries[0].Runs);
        Assert.NotEmpty(full.Queries[0].Summary.LogicalReads.RawValues);
        Assert.Empty(slim.Queries[0].Summary.LogicalReads.RawValues);
        Assert.Empty(slim.Queries[0].Summary.CpuTimeMilliseconds.RawValues);
        Assert.Empty(slim.Queries[0].Summary.ClientDurationMilliseconds.RawValues);
    }

    [Fact]
    public async Task StrippedBaseline_RoundTripsThroughTheStoreAndStillVerifies()
    {
        using var fixture = new DetailFixture();
        var configuration = fixture.CreateConfiguration();
        var full = await fixture.CreateWorkflow().CreateAsync(configuration);
        var path = Path.Combine(fixture.Root, "querywatt-baseline.json");
        var store = new BaselineJsonStore();

        store.Save(path, BaselineDetail.Strip(full));
        var json = File.ReadAllText(path);
        var loaded = store.Load(path);

        Assert.DoesNotContain("\"runs\"", json, StringComparison.Ordinal);
        Assert.Null(loaded.Queries[0].Runs);

        var result = await fixture.CreateWorkflow().VerifyAsync(configuration, loaded);

        Assert.Equal(VerificationVerdict.Passed, result.Verdict);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task StrippedBaseline_IsSubstantiallySmallerOnDisk()
    {
        using var fixture = new DetailFixture();
        var full = await fixture.CreateWorkflow().CreateAsync(fixture.CreateConfiguration());
        var store = new BaselineJsonStore();
        var fullPath = Path.Combine(fixture.Root, "full.json");
        var slimPath = Path.Combine(fixture.Root, "slim.json");

        store.Save(fullPath, full);
        store.Save(slimPath, BaselineDetail.Strip(full));

        var fullLines = File.ReadAllLines(fullPath).Length;
        var slimLines = File.ReadAllLines(slimPath).Length;

        Assert.True(
            slimLines * 3 < fullLines,
            $"Expected the slim baseline to be far smaller; full {fullLines} lines, slim {slimLines}.");
    }

    private sealed class DetailFixture : IDisposable
    {
        private readonly string _seedPath;

        public DetailFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"querywatt-detail-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            _seedPath = Path.Combine(Root, "seed.sql");
            File.WriteAllText(_seedPath, "SELECT 1;");
        }

        public string Root { get; }

        public ResolvedQueryWattConfiguration CreateConfiguration() =>
            new(
                Path.Combine(Root, "querywatt.yml"),
                "TEST_CONNECTION",
                Path.Combine(Root, "querywatt-baseline.json"),
                "test-image",
                [_seedPath],
                ["dbo.TestTable"],
                new ResolvedEnergyConfiguration(false, null),
                [
                    new ResolvedQueryConfiguration(
                        new MeasurementRequest("query", "SELECT 1;", 3, 20),
                        new QueryThresholds(new RegressionThreshold(25, 1000)),
                        null)
                ]);

        public BaselineWorkflow CreateWorkflow() =>
            new(
                new VaryingMeasurementRunner(),
                new StableEnvironmentInspector(),
                "PINNED OPTIONS");

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class StableEnvironmentInspector : IMeasurementEnvironmentInspector
    {
        public Task<MeasurementEnvironmentDetails> InspectAsync(
            IReadOnlyList<string> tableNames,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MeasurementEnvironmentDetails(
                "16.0.1000.6",
                "Developer Edition",
                tableNames.ToDictionary(name => name, _ => 10L, StringComparer.OrdinalIgnoreCase)));
    }

    // Deterministic but not constant, so the raw observation lists are real.
    private sealed class VaryingMeasurementRunner : IMeasurementRunner
    {
        public Task<QueryMeasurementSample> MeasureAsync(
            MeasurementRequest request,
            CancellationToken cancellationToken = default)
        {
            var runs = Enumerable.Range(1, 20)
                .Select(number => new RunMetrics(
                    number,
                    100,
                    0,
                    0,
                    0,
                    0,
                    0,
                    20,
                    10 + (number % 3),
                    1,
                    [new StatementMetrics(1, 20, 20, [
                        new TableIoMetrics("dbo.TestTable", 1, 1, 100, 0, 0, 0, 0, 0)
                    ])],
                    []))
                .ToArray();

            return Task.FromResult(new QueryMeasurementSample(
                request.Name,
                request.WarmupRuns,
                runs,
                [new StatementPlanFingerprint(1, "0x1111", "0xAAAA")]));
        }
    }
}
