namespace QueryWatt.Core;

public interface IMeasurementRunner
{
    Task<QueryMeasurementSample> MeasureAsync(
        MeasurementRequest request,
        CancellationToken cancellationToken = default);
}
