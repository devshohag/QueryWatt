using QueryWatt.Core;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class MeasurementSessionRunnerTests
{
    [Fact]
    public async Task MeasureAsync_RunsQueriesSequentiallyInConfigurationOrder()
    {
        var fakeRunner = new RecordingMeasurementRunner();
        var sessionRunner = new MeasurementSessionRunner(fakeRunner);
        var requests = new[]
        {
            new MeasurementRequest("first", "SELECT 1", 0, 20),
            new MeasurementRequest("second", "SELECT 2", 0, 20)
        };

        var result = await sessionRunner.MeasureAsync(requests);

        Assert.Equal(new[] { "first", "second" }, fakeRunner.Names);
        Assert.Equal(
            new[] { "first", "second" },
            result.Summaries.Select(summary => summary.QueryName).ToArray());
        Assert.Equal(1, fakeRunner.MaximumConcurrentCalls);
    }

    [Fact]
    public async Task MeasureAsync_RejectsDuplicateNamesIgnoringCase()
    {
        var sessionRunner = new MeasurementSessionRunner(new RecordingMeasurementRunner());
        var requests = new[]
        {
            new MeasurementRequest("Seek", "SELECT 1", 0, 20),
            new MeasurementRequest("seek", "SELECT 2", 0, 20)
        };

        await Assert.ThrowsAsync<ArgumentException>(() => sessionRunner.MeasureAsync(requests));
    }

    private sealed class RecordingMeasurementRunner : IMeasurementRunner
    {
        private int _activeCalls;

        public List<string> Names { get; } = [];
        public int MaximumConcurrentCalls { get; private set; }

        public Task<QueryMeasurementSample> MeasureAsync(
            MeasurementRequest request,
            CancellationToken cancellationToken = default)
        {
            _activeCalls++;
            MaximumConcurrentCalls = Math.Max(MaximumConcurrentCalls, _activeCalls);
            Names.Add(request.Name);

            var runs = Enumerable.Range(1, 20)
                .Select(run => new RunMetrics(
                    run,
                    10,
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                    2,
                    1,
                    [],
                    []))
                .ToArray();

            _activeCalls--;
            return Task.FromResult(new QueryMeasurementSample(request.Name, request.WarmupRuns, runs));
        }
    }
}
