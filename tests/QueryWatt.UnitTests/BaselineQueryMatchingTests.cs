using QueryWatt.Baselines;
using QueryWatt.Configuration;
using QueryWatt.Core;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class BaselineQueryMatchingTests
{
    [Fact]
    public async Task VerifyAsync_MatchesByNameWhenTheConfigurationOrderChanges()
    {
        using var fixture = new MatchingFixture();
        var baseline = await fixture.CreateWorkflow()
            .CreateAsync(fixture.CreateConfiguration("alpha", "beta"));

        var reordered = fixture.CreateConfiguration("beta", "alpha");
        var result = await fixture.CreateWorkflow().VerifyAsync(reordered, baseline);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            new[] { "beta", "alpha" },
            result.Queries.Select(query => query.QueryName).ToArray());
    }

    [Fact]
    public async Task VerifyAsync_NamesBothTheAddedAndTheRemovedQuery()
    {
        using var fixture = new MatchingFixture();
        var baseline = await fixture.CreateWorkflow()
            .CreateAsync(fixture.CreateConfiguration("alpha", "beta"));

        var renamed = fixture.CreateConfiguration("alpha", "gamma");
        var exception = await Assert.ThrowsAsync<BaselineConfigurationException>(
            async () => await fixture.CreateWorkflow().VerifyAsync(renamed, baseline));

        Assert.Contains("gamma", exception.Message, StringComparison.Ordinal);
        Assert.Contains("beta", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_ReportsRemovedQueriesWhenTheConfigurationShrinks()
    {
        using var fixture = new MatchingFixture();
        var baseline = await fixture.CreateWorkflow()
            .CreateAsync(fixture.CreateConfiguration("alpha", "beta"));

        var shrunk = fixture.CreateConfiguration("alpha");
        var exception = await Assert.ThrowsAsync<BaselineConfigurationException>(
            async () => await fixture.CreateWorkflow().VerifyAsync(shrunk, baseline));

        Assert.Contains("beta", exception.Message, StringComparison.Ordinal);
    }

    private sealed class MatchingFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"querywatt-matching-tests-{Guid.NewGuid():N}");
        private readonly string _seedPath;

        public MatchingFixture()
        {
            Directory.CreateDirectory(_root);
            _seedPath = Path.Combine(_root, "seed.sql");
            File.WriteAllText(_seedPath, "SELECT 1;");
        }

        public ResolvedQueryWattConfiguration CreateConfiguration(params string[] queryNames)
        {
            var queries = queryNames
                .Select(name => new ResolvedQueryConfiguration(
                    new MeasurementRequest(name, "SELECT 1;", 3, 20),
                    new QueryThresholds(new RegressionThreshold(25, 1000)),
                    null))
                .ToArray();

            return new ResolvedQueryWattConfiguration(
                Path.Combine(_root, "querywatt.yml"),
                "TEST_CONNECTION",
                Path.Combine(_root, "querywatt-baseline.json"),
                "test-image",
                [_seedPath],
                ["dbo.TestTable"],
                new ResolvedEnergyConfiguration(false, null),
                queries);
        }

        public BaselineWorkflow CreateWorkflow() =>
            new(
                new StableMeasurementRunner(),
                new StableEnvironmentInspector(),
                "PINNED OPTIONS");

        public void Dispose() => Directory.Delete(_root, recursive: true);
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

    private sealed class StableMeasurementRunner : IMeasurementRunner
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
                [new StatementPlanFingerprint(1, "0x1111", "0xAAAA")]));
        }
    }
}
