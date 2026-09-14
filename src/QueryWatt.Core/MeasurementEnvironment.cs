namespace QueryWatt.Core;

public sealed record MeasurementEnvironmentDetails(
    string SqlServerProductVersion,
    string SqlServerEdition,
    IReadOnlyDictionary<string, long> TableRowCounts);

public interface IMeasurementEnvironmentInspector
{
    Task<MeasurementEnvironmentDetails> InspectAsync(
        IReadOnlyList<string> tableNames,
        CancellationToken cancellationToken = default);
}
