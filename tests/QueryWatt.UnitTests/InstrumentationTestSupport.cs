using System.Data;
using QueryWatt.Core.Instrumentation;
using Xunit;

namespace QueryWatt.UnitTests;

/// <summary>
/// <see cref="Watt"/> is static, so these tests must not run beside each other.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InstrumentationCollection
{
    public const string Name = "querywatt-instrumentation";
}

/// <summary>Sets a clean static state for one test and restores it afterwards.</summary>
public sealed class WattFixture : IDisposable
{
    public WattFixture()
    {
        Watt.Reset();
        Sink = new InMemoryMeasurementSink();
        Watt.Sink = Sink;
        Watt.Instrumentation = InstrumentationLevel.Full;
    }

    public InMemoryMeasurementSink Sink { get; }

    public void Dispose() => Watt.Reset();
}

internal static class TestCommands
{
    public static MeasuredCommand Measured(
        int ordinal = 1,
        string text = "SELECT 1",
        MeasurementSource source = MeasurementSource.AdoNet,
        long logicalReads = 84,
        long cpuMilliseconds = 4,
        double durationMilliseconds = 11,
        long rows = 37,
        params CommandParameterInfo[] parameters) =>
        new(
            ordinal,
            text,
            CommandType.Text,
            source,
            parameters,
            CommandOutcome.Succeeded)
        {
            LogicalReads = logicalReads,
            CpuTimeMilliseconds = cpuMilliseconds,
            ClientDurationMilliseconds = durationMilliseconds,
            RowsReturned = rows
        };

    public static MeasuredCommand WithoutGatingMetrics(int ordinal = 1) =>
        new(
            ordinal,
            "SELECT 1",
            CommandType.Text,
            MeasurementSource.AdoNet,
            [],
            CommandOutcome.Succeeded)
        {
            ClientDurationMilliseconds = 8
        };

    public static CommandParameterInfo Parameter(
        string name,
        DbType type = DbType.Int32,
        bool isNull = false) =>
        new(name, type, isNull);
}

internal sealed class ThrowingSink : IMeasurementSink
{
    public void Publish(MeasurementRecord record) =>
        throw new InvalidOperationException("Sinks can fault.");
}
