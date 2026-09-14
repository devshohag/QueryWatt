using QueryWatt.Baselines;
using QueryWatt.Configuration;
using QueryWatt.Core;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class BaselineWorkflowTests
{
    [Fact]
    public async Task VerifyAsync_FailsOnlyWhenPercentAndAbsoluteThresholdsAreExceeded()
    {
        using var fixture = new WorkflowFixture();
        var configuration = fixture.CreateConfiguration(
            new QueryThresholds(new RegressionThreshold(25, 1000)));
        var baseline = await fixture.CreateWorkflow(logicalReads: 100)
            .CreateAsync(configuration);

        var belowAbsolute = await fixture.CreateWorkflow(logicalReads: 200)
            .VerifyAsync(configuration, baseline);
        var atAbsoluteBoundary = await fixture.CreateWorkflow(logicalReads: 1100)
            .VerifyAsync(configuration, baseline);
        var aboveBoth = await fixture.CreateWorkflow(logicalReads: 1200)
            .VerifyAsync(configuration, baseline);

        Assert.Equal(0, belowAbsolute.ExitCode);
        Assert.False(belowAbsolute.Queries[0].Regressed);
        Assert.Equal(0, atAbsoluteBoundary.ExitCode);
        Assert.False(atAbsoluteBoundary.Queries[0].Regressed);
        Assert.Equal(1, aboveBoth.ExitCode);
        Assert.True(aboveBoth.Queries[0].Regressed);
    }

    [Fact]
    public async Task VerifyAsync_DoesNotGateCpuBelowResolutionFloor()
    {
        using var fixture = new WorkflowFixture();
        var configuration = fixture.CreateConfiguration(new QueryThresholds(
            new RegressionThreshold(25, 1000),
            new RegressionThreshold(10, 1)));
        var baseline = await fixture.CreateWorkflow(logicalReads: 100, cpu: 5)
            .CreateAsync(configuration);

        var result = await fixture.CreateWorkflow(logicalReads: 100, cpu: 100)
            .VerifyAsync(configuration, baseline);

        Assert.Equal(0, result.ExitCode);
        var cpu = Assert.Single(result.Queries[0].Metrics, metric =>
            metric.MetricName == "cpuTimeMilliseconds");
        Assert.Equal("below-resolution", cpu.Note);
        Assert.False(cpu.Regressed);
    }

    [Fact]
    public async Task VerifyAsync_ReportsPlanChangeWithoutFailing()
    {
        using var fixture = new WorkflowFixture();
        var configuration = fixture.CreateConfiguration(
            new QueryThresholds(new RegressionThreshold(25, 1000)));
        var baseline = await fixture.CreateWorkflow(logicalReads: 100, planHash: "0xAAAA")
            .CreateAsync(configuration);

        var result = await fixture.CreateWorkflow(logicalReads: 100, planHash: "0xBBBB")
            .VerifyAsync(configuration, baseline);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Queries[0].PlanChanged);
    }

    private sealed class WorkflowFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"querywatt-baseline-tests-{Guid.NewGuid():N}");
        private readonly string _seedPath;

        public WorkflowFixture()
        {
            Directory.CreateDirectory(_root);
            _seedPath = Path.Combine(_root, "seed.sql");
            File.WriteAllText(_seedPath, "SELECT 1;");
        }

        public ResolvedQueryWattConfiguration CreateConfiguration(QueryThresholds thresholds)
        {
            var request = new MeasurementRequest("query", "SELECT 1;", 3, 20);
            return new ResolvedQueryWattConfiguration(
                Path.Combine(_root, "querywatt.yml"),
                "TEST_CONNECTION",
                Path.Combine(_root, "querywatt-baseline.json"),
                "test-image",
                [_seedPath],
                ["dbo.TestTable"],
                [new ResolvedQueryConfiguration(request, thresholds)]);
        }

        public BaselineWorkflow CreateWorkflow(
            long logicalReads,
            long cpu = 20,
            string planHash = "0xAAAA") =>
            new(
                new FakeMeasurementRunner(logicalReads, cpu, planHash),
                new FakeEnvironmentInspector(),
                "PINNED OPTIONS");

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeEnvironmentInspector : IMeasurementEnvironmentInspector
    {
        public Task<MeasurementEnvironmentDetails> InspectAsync(
            IReadOnlyList<string> tableNames,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MeasurementEnvironmentDetails(
                "16.0.1000.6",
                "Developer Edition",
                tableNames.ToDictionary(name => name, _ => 10L, StringComparer.OrdinalIgnoreCase)));
    }

    private sealed class FakeMeasurementRunner(
        long logicalReads,
        long cpu,
        string planHash) : IMeasurementRunner
    {
        public Task<QueryMeasurementSample> MeasureAsync(
            MeasurementRequest request,
            CancellationToken cancellationToken = default)
        {
            var runs = Enumerable.Range(1, 20)
                .Select(number => new RunMetrics(
                    number,
                    logicalReads,
                    0,
                    0,
                    0,
                    0,
                    0,
                    cpu,
                    10,
                    1,
                    [],
                    []))
                .ToArray();

            return Task.FromResult(new QueryMeasurementSample(
                request.Name,
                request.WarmupRuns,
                runs,
                [new StatementPlanFingerprint(1, "0x1111", planHash)]));
        }
    }
}
