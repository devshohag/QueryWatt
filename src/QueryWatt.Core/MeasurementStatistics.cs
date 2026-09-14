namespace QueryWatt.Core;

public sealed record MetricSummary(
    int N,
    double Minimum,
    double RawMedian,
    double FilteredMedian,
    double Maximum,
    double Mean,
    double StandardDeviation,
    double FirstQuartile,
    double ThirdQuartile,
    double LowerOutlierFence,
    double UpperOutlierFence,
    IReadOnlyList<double> RawValues,
    IReadOnlyList<int> OutlierRunNumbers,
    double? P95);

public sealed record QueryStatisticsSummary(
    string QueryName,
    MetricSummary LogicalReads,
    MetricSummary LobLogicalReads,
    MetricSummary PhysicalReads,
    MetricSummary ReadAheadReads,
    MetricSummary LobPhysicalReads,
    MetricSummary LobReadAheadReads,
    MetricSummary CpuTimeMilliseconds,
    MetricSummary ClientDurationMilliseconds,
    MetricSummary RowsReturned);

public static class MeasurementStatistics
{
    public const int MinimumSampleSize = 20;
    public const int MinimumP95SampleSize = 50;

    public static QueryStatisticsSummary Summarize(QueryMeasurementSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (sample.Runs.Count < MinimumSampleSize)
        {
            throw new ArgumentException(
                $"At least {MinimumSampleSize} measured runs are required to produce a summary.",
                nameof(sample));
        }

        return new QueryStatisticsSummary(
            sample.QueryName,
            SummarizeMetric(sample.Runs.Select(run => (double)run.LogicalReads)),
            SummarizeMetric(sample.Runs.Select(run => (double)run.LobLogicalReads)),
            SummarizeMetric(sample.Runs.Select(run => (double)run.PhysicalReads)),
            SummarizeMetric(sample.Runs.Select(run => (double)run.ReadAheadReads)),
            SummarizeMetric(sample.Runs.Select(run => (double)run.LobPhysicalReads)),
            SummarizeMetric(sample.Runs.Select(run => (double)run.LobReadAheadReads)),
            SummarizeMetric(sample.Runs.Select(run => (double)run.CpuTimeMilliseconds)),
            SummarizeMetric(sample.Runs.Select(run => run.ClientDurationMilliseconds)),
            SummarizeMetric(sample.Runs.Select(run => (double)run.RowsReturned)));
    }

    public static MetricSummary SummarizeMetric(IEnumerable<double> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var raw = observations.ToArray();
        if (raw.Length < MinimumSampleSize)
        {
            throw new ArgumentException(
                $"At least {MinimumSampleSize} observations are required.",
                nameof(observations));
        }

        if (raw.Any(value => double.IsNaN(value) || double.IsInfinity(value)))
        {
            throw new ArgumentException("Observations must be finite numbers.", nameof(observations));
        }

        var sorted = raw.Order().ToArray();
        var q1 = PercentileR7(sorted, 0.25);
        var q3 = PercentileR7(sorted, 0.75);
        var iqr = q3 - q1;
        var lowerFence = q1 - (1.5 * iqr);
        var upperFence = q3 + (1.5 * iqr);

        var filtered = raw
            .Where(value => value >= lowerFence && value <= upperFence)
            .Order()
            .ToArray();

        var outlierRunNumbers = raw
            .Select((value, index) => new { Value = value, RunNumber = index + 1 })
            .Where(item => item.Value < lowerFence || item.Value > upperFence)
            .Select(item => item.RunNumber)
            .ToArray();

        var mean = raw.Average();

        // Sample variance: these observations are a sample of the runs the query
        // could have had, not the whole population, so the divisor is n - 1.
        // MinimumSampleSize keeps the divisor safely above zero.
        var variance = raw.Sum(value => Math.Pow(value - mean, 2)) / (raw.Length - 1);

        return new MetricSummary(
            raw.Length,
            sorted[0],
            PercentileR7(sorted, 0.5),
            PercentileR7(filtered, 0.5),
            sorted[^1],
            mean,
            Math.Sqrt(variance),
            q1,
            q3,
            lowerFence,
            upperFence,
            raw,
            outlierRunNumbers,
            raw.Length >= MinimumP95SampleSize ? PercentileR7(sorted, 0.95) : null);
    }

    private static double PercentileR7(IReadOnlyList<double> sortedValues, double probability)
    {
        if (sortedValues.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(sortedValues));
        }

        if (probability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        var position = (sortedValues.Count - 1) * probability;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        var fraction = position - lowerIndex;
        return sortedValues[lowerIndex]
            + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction);
    }
}
