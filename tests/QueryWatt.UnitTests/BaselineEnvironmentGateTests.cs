using QueryWatt.Baselines;
using QueryWatt.Configuration;
using QueryWatt.Core;
using QueryWatt.SqlServer;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class BaselineEnvironmentGateTests
{
    [Fact]
    public async Task VerifyAsync_AcceptsBaselineWrittenByAnOlderToolVersion()
    {
        using var fixture = new EnvironmentFixture();
        var configuration = fixture.CreateConfiguration();
        var baseline = await fixture.CreateWorkflow().CreateAsync(configuration);
        var writtenByAnOlderBuild = baseline with { ToolVersion = "0.0.1-alpha" };

        var result = await fixture.CreateWorkflow()
            .VerifyAsync(configuration, writtenByAnOlderBuild);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task VerifyAsync_AcceptsCumulativeUpdateWithinTheSameMajorVersion()
    {
        using var fixture = new EnvironmentFixture();
        var configuration = fixture.CreateConfiguration();
        var baseline = await fixture.CreateWorkflow("16.0.1000.6").CreateAsync(configuration);

        var result = await fixture.CreateWorkflow("16.0.4215.2")
            .VerifyAsync(configuration, baseline);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task VerifyAsync_RefusesADifferentMajorVersion()
    {
        using var fixture = new EnvironmentFixture();
        var configuration = fixture.CreateConfiguration();
        var baseline = await fixture.CreateWorkflow("16.0.1000.6").CreateAsync(configuration);

        var exception = await Assert.ThrowsAsync<EnvironmentMismatchException>(
            async () => await fixture.CreateWorkflow("17.0.100.1")
                .VerifyAsync(configuration, baseline));

        Assert.Contains("sqlServerMajorVersion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("16.0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("17.0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolVersion_ComesFromTheAssemblyAndCarriesNoBuildMetadata()
    {
        Assert.False(string.IsNullOrWhiteSpace(BaselineContract.ToolVersion));
        Assert.DoesNotContain("+", BaselineContract.ToolVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedSetOptions_PinTheServerLanguageSoStatisticsMessagesStayParsable()
    {
        Assert.Contains(
            "SET LANGUAGE us_english;",
            SqlServerMeasurementRunner.PinnedSetOptions,
            StringComparison.Ordinal);
    }

    private sealed class EnvironmentFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"querywatt-environment-tests-{Guid.NewGuid():N}");
        private readonly string _seedPath;

        public EnvironmentFixture()
        {
            Directory.CreateDirectory(_root);
            _seedPath = Path.Combine(_root, "seed.sql");
            File.WriteAllText(_seedPath, "SELECT 1;");
        }

        public ResolvedQueryWattConfiguration CreateConfiguration()
        {
            var request = new MeasurementRequest("query", "SELECT 1;", 3, 20);
            return new ResolvedQueryWattConfiguration(
                Path.Combine(_root, "querywatt.yml"),
                "TEST_CONNECTION",
                Path.Combine(_root, "querywatt-baseline.json"),
                "test-image",
                [_seedPath],
                ["dbo.TestTable"],
                new ResolvedEnergyConfiguration(false, null),
                [
                    new ResolvedQueryConfiguration(
                        request,
                        new QueryThresholds(new RegressionThreshold(25, 1000)),
                        null)
                ]);
        }

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