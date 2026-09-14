namespace QueryWatt.Core;

public sealed record MeasurementSessionResult(
    IReadOnlyList<QueryMeasurementSample> Samples,
    IReadOnlyList<QueryStatisticsSummary> Summaries);

public sealed class MeasurementSessionRunner(IMeasurementRunner measurementRunner)
{
    private readonly IMeasurementRunner _measurementRunner =
        measurementRunner ?? throw new ArgumentNullException(nameof(measurementRunner));

    public async Task<MeasurementSessionResult> MeasureAsync(
        IReadOnlyList<MeasurementRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
        {
            throw new ArgumentException("At least one query is required.", nameof(requests));
        }

        var duplicateName = requests
            .GroupBy(request => request.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;

        if (duplicateName is not null)
        {
            throw new ArgumentException(
                $"Query names must be unique. Duplicate: '{duplicateName}'.",
                nameof(requests));
        }

        var samples = new List<QueryMeasurementSample>(requests.Count);
        var summaries = new List<QueryStatisticsSummary>(requests.Count);

        // Measurement is deliberately sequential. Parallel query execution would
        // contaminate CPU and elapsed-duration observations.
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sample = await _measurementRunner
                .MeasureAsync(request, cancellationToken)
                .ConfigureAwait(false);

            samples.Add(sample);
            summaries.Add(MeasurementStatistics.Summarize(sample));
        }

        return new MeasurementSessionResult(samples, summaries);
    }
}
