using QueryWatt.Core.Instrumentation;
using Xunit;

namespace QueryWatt.UnitTests;

[Collection(InstrumentationCollection.Name)]
public sealed class MeasurementScopeLifecycleTests : IDisposable
{
    private readonly WattFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Complete_MarksTheScopeCompleted()
    {
        MeasurementRecord? record;

        using (var scope = Watt.Measure("orders.pending-by-customer"))
        {
            scope.State!.RecordCommand(TestCommands.Measured());
            scope.Complete();
            record = scope.Record;
        }

        Assert.NotNull(record);
        Assert.Equal(MeasurementStatus.Completed, record!.Status);
        Assert.True(record.IsBaselineEligible);
        Assert.Single(_fixture.Sink.Records);
    }

    [Fact]
    public void Fail_MarksTheScopeFailed_AndKeepsItOutOfTheBaseline()
    {
        MeasurementRecord? record;

        using (var scope = Watt.Measure("orders.pending-by-customer"))
        {
            scope.State!.RecordCommand(TestCommands.Measured());
            scope.Fail(new TimeoutException("boom"));
            record = scope.Record;
        }

        Assert.NotNull(record);
        Assert.Equal(MeasurementStatus.Failed, record!.Status);
        Assert.False(record.IsBaselineEligible);
        Assert.Equal(typeof(TimeoutException).FullName, record.ExceptionType);
        Assert.Equal("boom", record.ExceptionMessage);
    }

    [Fact]
    public void Fail_WithCancellation_MarksTheScopeCancelled()
    {
        MeasurementRecord? record;

        using (var scope = Watt.Measure("orders.pending-by-customer"))
        {
            scope.Fail(new OperationCanceledException());
            record = scope.Record;
        }

        Assert.Equal(MeasurementStatus.Cancelled, record!.Status);
    }

    [Fact]
    public void DisposeWithoutATerminalCall_MarksTheScopeAbandoned()
    {
        using (var scope = Watt.Measure("orders.pending-by-customer"))
        {
            scope.State!.RecordCommand(TestCommands.Measured());
        }

        var record = Assert.Single(_fixture.Sink.Records);

        Assert.Equal(MeasurementStatus.Abandoned, record.Status);
        Assert.False(record.IsBaselineEligible);
        Assert.Contains(record.Diagnostics, d => d.Code == DiagnosticCode.AbandonedScope);
    }

    [Fact]
    public void Complete_IsIdempotent()
    {
        using (var scope = Watt.Measure("orders.pending-by-customer"))
        {
            scope.State!.RecordCommand(TestCommands.Measured());
            scope.Complete();
            scope.Complete();
            scope.Fail(new InvalidOperationException("ignored"));
        }

        var record = Assert.Single(_fixture.Sink.Records);

        Assert.Equal(MeasurementStatus.Completed, record.Status);
        Assert.Null(record.ExceptionType);
    }

    [Fact]
    public void AStartedCommandThatNeverFinished_MakesTheScopeAbandoned()
    {
        using (var scope = Watt.Measure("orders.pending-by-customer"))
        {
            scope.State!.BeginCommand();
            scope.State.BeginCommand();
            scope.State.RecordCommand(TestCommands.Measured());
            scope.Complete();
        }

        var record = Assert.Single(_fixture.Sink.Records);

        Assert.Equal(MeasurementStatus.Abandoned, record.Status);
    }

    [Fact]
    public void AScopeWithNoCommands_RaisesNoCommandsInScope()
    {
        using (var scope = Watt.Measure("service.does-no-database-work"))
        {
            scope.Complete();
        }

        var record = Assert.Single(_fixture.Sink.Records);

        Assert.Contains(record.Diagnostics, d => d.Code == DiagnosticCode.NoCommandsInScope);
    }

    [Fact]
    public void AnOpenReaderAtComplete_IsADiagnosticNotAFailure()
    {
        using (var scope = Watt.Measure("orders.stream"))
        {
            scope.State!.RecordCommand(TestCommands.Measured());
            scope.State.ReaderOpened();
            scope.Complete();
        }

        var record = Assert.Single(_fixture.Sink.Records);

        Assert.Equal(MeasurementStatus.Completed, record.Status);
        Assert.Contains(record.Diagnostics, d => d.Code == DiagnosticCode.OpenReaderAtComplete);
    }

    [Fact]
    public void CompleteWithRows_RecordsTheCallerSuppliedCount()
    {
        using var scope = Watt.Measure("orders.count");
        scope.State!.RecordCommand(TestCommands.Measured(rows: 0));
        scope.Complete(rowsReturned: 412);

        Assert.Equal(412L, scope.Record!.RowsReturned);
    }

    [Fact]
    public void AFaultingSink_NeverReachesTheApplication()
    {
        Watt.Sink = new ThrowingSink();

        using var scope = Watt.Measure("orders.pending-by-customer");
        scope.State!.RecordCommand(TestCommands.Measured());
        scope.Complete();

        Assert.Equal(MeasurementStatus.Completed, scope.Record!.Status);
    }

    [Fact]
    public void Measure_RejectsAnEmptyQueryId_WhenEnabled()
    {
        Assert.Throws<ArgumentException>(() => { _ = Watt.Measure("   "); });
    }
}
