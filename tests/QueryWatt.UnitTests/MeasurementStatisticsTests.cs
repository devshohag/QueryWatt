using QueryWatt.Core;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class MeasurementStatisticsTests
{
    [Fact]
    public void SummarizeMetric_ReportsSampleStandardDeviation()
    {
        // Ten 10s and ten 20s: mean 15, squared deviations 20 * 25 = 500.
        // Sample standard deviation divides by n - 1, so sqrt(500 / 19).
        var observations = Enumerable.Repeat(10d, 10)
            .Concat(Enumerable.Repeat(20d, 10))
            .ToArray();

        var summary = MeasurementStatistics.SummarizeMetric(observations);

        Assert.Equal(15d, summary.Mean);
        Assert.Equal(Math.Sqrt(500d / 19d), summary.StandardDeviation, 10);
    }

    [Fact]
    public void SummarizeMetric_MarksOutlierAndKeepsRawValues()
    {
        var values = Enumerable.Repeat(100d, 19).Append(10_000d).ToArray();

        var summary = MeasurementStatistics.SummarizeMetric(values);

        Assert.Equal(20, summary.N);
        Assert.Equal(values, summary.RawValues.ToArray());
        Assert.Equal([20], summary.OutlierRunNumbers);
        Assert.Equal(100d, summary.RawMedian);
        Assert.Equal(100d, summary.FilteredMedian);
        Assert.Null(summary.P95);
    }

    [Fact]
    public void SummarizeMetric_ComputesP95OnlyAtFiftyRuns()
    {
        var summary = MeasurementStatistics.SummarizeMetric(
            Enumerable.Range(1, 50).Select(value => (double)value));

        Assert.NotNull(summary.P95);
        Assert.Equal(47.55d, summary.P95!.Value, precision: 10);
    }

    [Fact]
    public void SummarizeMetric_RejectsFewerThanTwentyRuns()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MeasurementStatistics.SummarizeMetric(Enumerable.Repeat(1d, 19)));

        Assert.Equal("observations", exception.ParamName);
    }
}
