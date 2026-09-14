using QueryWatt.Baselines;
using QueryWatt.Configuration;
using QueryWatt.Core;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class StaleBaselineTests
{
    [Fact]
    public async Task VerifyAsync_ReportsAStaleBaselineWhenOnlyTheSeedChanged()
    {
        using var fixture = new StaleFixture();
        var configuration = fixture.CreateConfiguration();
        var baseline = await fixture.CreateWorkflow().CreateAsync(configuration);

        fixture.RewriteSeed("SELECT 2; -- an extra column arrived with this pull request");
        var result = await fixture.CreateWorkflow().VerifyAsync(configuration, baseline);

        Assert.Equal(VerificationVerdict.BaselineStale, result.Verdict);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Queries);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("querywatt baseline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyAsync_WarnsButStillComparesWhenOnlyTheSqlServerBuildMoved()
    {
        using var fixture = new StaleFixture();
        var configuration = fixture.CreateConfiguration();
        var baseline = await fixture.CreateWorkflow("16.0.1000.6").CreateAsync(configuration);

        var result = await fixture.CreateWorkflow("16.0.4215.2")
            .VerifyAsync(configuration, baseline);

        Assert.Equal(VerificationVerdict.Passed, result.Verdict);
        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Queries);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("16.0.1000.6", warning, StringComparison.Ordinal);
        Assert.Contains("16.0.4215.2", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_StillRefusesWhenSomethingBeyondTheSeedChanged()
    {
        using var fixture = new StaleFixture();
        var configuration = fixture.CreateConfiguration();
        var baseline = await fixture.CreateWorkflow().CreateAsync(configuration);

        fixture.RewriteSeed("SELECT 2;");
        var exception = await Assert.ThrowsAsync<EnvironmentMismatchException>(
            async () => await fixture.CreateWorkflow("17.0.100.1")
                .VerifyAsync(configuration, baseline));

        Assert.Contains("sqlServerMajorVersion", exception.Message, StringComparison.Ordinal);
    }

    private sealed class StaleFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"querywatt-stale-tests-{Guid.NewGuid():N}");
        private readonly string _seedPath;

        public StaleFixture()
        {
            Directory.CreateDirectory(_root);
            _seedPath = Path.Combine(_root, "seed.sql");
            File.WriteAllText(_seedPath, "SELECT 1;");
        }

        public void RewriteSeed(string contents) => File.WriteAllText(_seedPath, contents);

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

        public BaselineWorkflow CreateWorkflow(string productVersion = "16.0.1000.6") =>
            new(
                new StableMeasurementRunner(),
                new VersionedEnvironmentInspector(productVersion),
                "PINNED OPTIONS");

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class VersionedEnvironmentInspector(string productVersion)
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
