using QueryWatt.Core.Instrumentation;
using Xunit;

namespace QueryWatt.IntegrationTests;

/// <summary>
/// The release gate in one place: a stack is supported only when the measured command carries the
/// metrics a verdict needs and the application's own result came back unchanged.
/// Contract v2 §5, and the release gate in the roadmap.
/// </summary>
internal static class MeasurementAssertions
{
    public static MeasuredCommand SingleFullyMeasuredCommand(
        MeasurementRecord record,
        MeasurementSource expectedSource = MeasurementSource.AdoNet)
    {
        Assert.Equal(MeasurementStatus.Completed, record.Status);

        var command = Assert.Single(record.Commands);

        Assert.True(
            record.IsBaselineEligible,
            DescribeDiagnostics(record)
                + " | LogicalReads=" + command.LogicalReads
                + " | ServerElapsed=" + command.ServerElapsedTimeMilliseconds
                + " | RawMessages(" + command.RawMessages.Count + ")=["
                + string.Join(" || ", command.RawMessages)
                + "]");
        AssertGatingMetrics(command);
        Assert.Equal(expectedSource, command.Source);

        return command;
    }

    public static void AssertGatingMetrics(MeasuredCommand command)
    {
        Assert.NotNull(command.ClientDurationMilliseconds);
        Assert.True(command.ClientDurationMilliseconds > 0);

        Assert.True(
            command.LogicalReads.HasValue,
            "Expected logical reads. RawMessages: ["
                + string.Join(" || ", command.RawMessages)
                + $"] (count={command.RawMessages.Count})");
        Assert.True(
            command.LogicalReads > 0,
            $"Expected logical reads to be captured, got {command.LogicalReads}.");

        // CPU may legitimately be zero for a very cheap query; what matters is that it was
        // observed rather than defaulted.
        Assert.NotNull(command.CpuTimeMilliseconds);
        Assert.True(command.CpuTimeMilliseconds >= 0);

        Assert.True(command.HasGatingMetrics);
    }

    public static string DescribeDiagnostics(MeasurementRecord record) =>
        record.Diagnostics.Count == 0
            ? $"'{record.QueryId}' produced no diagnostics."
            : $"'{record.QueryId}' diagnostics: "
              + string.Join(
                  "; ",
                  record.Diagnostics.Select(diagnostic => diagnostic.Code + " - " + diagnostic.Message));
}
