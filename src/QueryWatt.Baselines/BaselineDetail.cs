using QueryWatt.Core;

namespace QueryWatt.Baselines;

/// <summary>
/// A committed baseline is read in a pull-request diff. Storing every raw
/// observation makes the file large and makes every re-baseline rewrite all of
/// it, which is exactly what stops a reviewer from reading it. Verification uses
/// only the summary statistics, so the raw detail is kept out unless it is asked
/// for with <c>--include-runs</c>.
/// </summary>
public static class BaselineDetail
{
    public static BaselineDocument Strip(BaselineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document with
        {
            Queries = document.Queries
                .Select(query => query with
                {
                    Summary = StripSummary(query.Summary),
                    Runs = null
                })
                .ToArray()
        };
    }

    private static QueryStatisticsSummary StripSummary(QueryStatisticsSummary summary) =>
        new(
            summary.QueryName,
            StripMetric(summary.LogicalReads),
            StripMetric(summary.LobLogicalReads),
            StripMetric(summary.PhysicalReads),
            StripMetric(summary.ReadAheadReads),
            StripMetric(summary.LobPhysicalReads),
            StripMetric(summary.LobReadAheadReads),
            StripMetric(summary.CpuTimeMilliseconds),
            StripMetric(summary.ClientDurationMilliseconds),
            StripMetric(summary.RowsReturned));

    // Every field a comparison reads survives: the filtered median, the quartiles
    // and fences, the outlier run numbers and p95. Only the observation list goes.
    private static MetricSummary StripMetric(MetricSummary metric) =>
        metric with { RawValues = [] };
}
