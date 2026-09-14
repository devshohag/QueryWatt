using QueryWatt.Baselines;
using QueryWatt.Configuration;
using QueryWatt.Core;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class PlanChangeSignalTests
{
    [Fact]
    public async Task VerifyAsync_SeparatesAQueryTextChangeFromAPlanShapeChange()
    {
        using var fixture = new PlanFixture();
        var configuration = fixture.CreateConfiguration();
        var baseline = await fixture
            .CreateWorkflow(queryHash: "0x1111", planHash: "0xAAAA")
            .CreateAsync(configuration);

        // Rewritten query, same plan shape: this is the common pull request, and it
        // must not light up the plan signal.
        var result = await fixture
            .CreateWorkflow(queryHash: "0x2222", planHash: "0xAAAA")
            .VerifyAsync(configuration, baseline);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Queries[0].QueryTextChanged);
        Assert.False(result.Queries[0].PlanShapeChanged);
    }

    [Fact]
    public async Task VerifyAsync_ReportsAPlanShapeChangeWhileTheQueryTextIsUnchanged()
    {
        using var fixture = new PlanFixture();
        var configuration = fixture.CreateConfiguration();
        var baseline = await fixture
            .CreateWorkflow(queryHash: "0x1111", planHash: "0xAAAA")
            .CreateAsync(configuration);

        var result = await fixture
            .CreateWorkflow(queryHash: "0x1111", planHash: "0xBBBB")
            .VerifyAsync(configuration, baseline);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Queries[0].QueryTextChanged);
        Assert.True(result.Queries[0].PlanShapeChanged);
    }

    private sealed class PlanFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"querywatt-plan-tests-{Guid.NewGuid():N}");
        private readonly string _seedPath;

        public PlanFixture()
        {
            Directory.CreateDirectory(_root);
            _seedPath = Path.Combine(_root, "seed.sql");
            File.WriteAllText(_seedPath, "SELECT 1;");
        }

        public ResolvedQueryWattConfiguration CreateConfiguration() =>
            new(
                Path.Combine(_root, "querywatt.yml"),
                "TEST_CONNECTION",
                Path.Combine(_root, "querywatt-baseline.json"),
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

        public BaselineWorkflow CreateWorkflow(string queryHash, string planHash) =>
            new(
                new FingerprintedMeasurementRunner(queryHash, planHash),
                new StableEnvironmentInspector("16.0.1000.6"),
                "PINNED OPTIONS");

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class StableEnvironmentInspector(string productVersion)
        : IMeasurementEnvironmentInspector
    {
        public Task<MeasurementEnvironmentDetails> InspectAsync(
            IReadOnlyList<string> tableNames,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MeasurementEnvironmentDetails(
                productVersion,
                "Developer Edition",
                tableNames.ToDictionary(name => name, _ => 10L, StringComparer.OrdinalIgnoreCase)));
    }

    private sealed class FingerprintedMeasurementRunner(string queryHash, string planHash)
        : IMeasurementRunner
    {
        public Task<QueryMeasurementSample> MeasureAsync(
            MeasurementRequest request,
            CancellationToken cancellationToken = default)
        {
            var runs = Enumerable.Range(1, 20)
                .Select(number => new RunMetrics(
                    number, 100, 0, 0, 0, 0, 0, 20, 10, 1, [], []))
                .ToArray();

            return Task.FromResult(new QueryMeasurementSample(
                request.Name,
                request.WarmupRuns,
                runs,
                [new StatementPlanFingerprint(1, queryHash, planHash)]));
        }
    }
}
