using QueryWatt.Core.Instrumentation;
using Xunit;

namespace QueryWatt.UnitTests;

[Collection(InstrumentationCollection.Name)]
public sealed class MeasurementScopeDisabledTests : IDisposable
{
    public MeasurementScopeDisabledTests() => Watt.Reset();

    public void Dispose() => Watt.Reset();

    [Fact]
    public void WhenOff_TheScopeIsANoOp()
    {
        using var scope = Watt.Measure("orders.pending-by-customer");

        Assert.False(scope.IsEnabled);
        Assert.Null(scope.State);
        Assert.Null(scope.Record);
        Assert.Null(Watt.Current);

        scope.Complete();
        scope.Fail(new InvalidOperationException("ignored"));
        scope.Scenario("ignored");

        Assert.Null(scope.Record);
    }

    [Fact]
    public void WhenOff_AnEmptyQueryIdIsNotEvenValidated()
    {
        using var scope = Watt.Measure("");

        Assert.False(scope.IsEnabled);
    }

    [Fact]
    public void WhenOff_MeasuringDoesNotAllocate()
    {
        // Warm the JIT so compilation is not counted.
        for (var i = 0; i < 1_000; i++)
        {
            Spin();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 10_000; i++)
        {
            Spin();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Ten thousand allocating calls would cost hundreds of kilobytes.
        Assert.True(
            allocated < 1_024,
            $"Disabled measurement allocated {allocated} bytes across 10,000 scopes.");

        static void Spin()
        {
            using var scope = Watt.Measure("orders.pending-by-customer");
            scope.Complete();
        }
    }

    [Fact]
    public void EnabledFollowsTheInstrumentationLevel()
    {
        Assert.False(Watt.Enabled);
        Assert.Equal(InstrumentationLevel.Off, Watt.Instrumentation);

        Watt.Enabled = true;
        Assert.Equal(InstrumentationLevel.Full, Watt.Instrumentation);

        Watt.Instrumentation = InstrumentationLevel.Light;
        Assert.True(Watt.Enabled);

        Watt.Enabled = true;
        Assert.Equal(InstrumentationLevel.Light, Watt.Instrumentation);

        Watt.Enabled = false;
        Assert.Equal(InstrumentationLevel.Off, Watt.Instrumentation);
    }
}
