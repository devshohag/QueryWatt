using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using QueryWatt.Core.Instrumentation;
using QueryWatt.SqlServer.Capture;
using Xunit;

namespace QueryWatt.UnitTests;

/// <summary>
/// Drives the capture path with synthetic SqlClient diagnostic events, so the attribution rules
/// are proven without a database. Real metrics are covered by the integration tests in PR #9.
/// </summary>
[Collection(InstrumentationCollection.Name)]
public sealed class SqlClientCaptureTests : IDisposable
{
    private const string MicrosoftPrefix = "Microsoft.Data.SqlClient";
    private const string LegacyPrefix = "System.Data.SqlClient";

    private readonly WattFixture _fixture = new();
    private readonly DiagnosticListener _listener = new(SqlClientCapture.ListenerName);
    private readonly SqlClientCapture _capture = SqlClientCapture.Enable();

    public void Dispose()
    {
        _capture.Dispose();
        _listener.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void ACommandInsideAScope_IsRecordedWithItsIdentityAndDuration()
    {
        using var command = NewCommand();

        MeasurementRecord? record;
        using (var scope = Watt.Measure("orders.pending-by-customer"))
        {
            Execute(MicrosoftPrefix, command);
            scope.Complete();
            record = scope.Record;
        }

        Assert.NotNull(record);

        var measured = Assert.Single(record!.Commands);

        Assert.Equal(1, measured.Ordinal);
        Assert.Equal(command.CommandText, measured.CommandText);
        Assert.Equal(CommandType.Text, measured.CommandType);
        Assert.Equal(MeasurementSource.AdoNet, measured.Source);
        Assert.Equal(CommandOutcome.Succeeded, measured.Outcome);
        Assert.NotNull(measured.ClientDurationMilliseconds);

        // No connection, so no server statistics — and they stay null rather than becoming zero.
        Assert.Null(measured.LogicalReads);
        Assert.Null(measured.CpuTimeMilliseconds);
        Assert.False(record.IsBaselineEligible);
        Assert.Equal(MeasurementStatus.Completed, record.Status);
    }

    [Fact]
    public void TheScenarioKey_ComesFromTheObservedParameters()
    {
        using var command = NewCommand();

        using (var scope = Watt.Measure("orders.pending-by-customer"))
        {
            Execute(MicrosoftPrefix, command);
            scope.Complete();
        }

        var record = Assert.Single(_fixture.Sink.Records);

        Assert.Equal("p[CustomerId:Guid, PlacedAfter:DateTime2]", record.ScenarioKey);
    }

    [Fact]
    public void ACommandOutsideEveryScope_IsIgnored()
    {
        using var command = NewCommand();

        Execute(MicrosoftPrefix, command);

        Assert.Empty(_fixture.Sink.Records);
        Assert.Null(Watt.Current);
    }

    [Fact]
    public void TheLegacyDriverEventsAreCapturedToo()
    {
        using var command = NewCommand();

        using (var scope = Watt.Measure("catalog.product-search"))
        {
            Execute(LegacyPrefix, command);
            scope.Complete();
        }

        var record = Assert.Single(_fixture.Sink.Records);

        Assert.Single(record.Commands);
    }

    [Fact]
    public void AFailedCommand_IsRecordedAsFailedWithoutFailingTheScope()
    {
        using var command = NewCommand();

        using (var scope = Watt.Measure("orders.pending-by-customer"))
        {
            var operationId = Guid.NewGuid();
            Write(MicrosoftPrefix, "WriteCommandBefore", operationId, command, exception: null);
            Write(
                MicrosoftPrefix,
                "WriteCommandError",
                operationId,
                command,
                new TimeoutException("Execution Timeout Expired."));
            scope.Complete();
        }

        var record = Assert.Single(_fixture.Sink.Records);
        var measured = Assert.Single(record.Commands);

        Assert.Equal(CommandOutcome.Failed, measured.Outcome);
        Assert.Equal(typeof(TimeoutException).FullName, measured.ExceptionType);
        Assert.Equal(MeasurementStatus.Completed, record.Status);
    }

    [Fact]
    public void ACommandThatNeverFinishes_LeavesTheScopeAbandoned()
    {
        using var command = NewCommand();

        using (var scope = Watt.Measure("orders.pending-by-customer"))
        {
            Write(MicrosoftPrefix, "WriteCommandBefore", Guid.NewGuid(), command, exception: null);
            scope.Complete();
        }

        var record = Assert.Single(_fixture.Sink.Records);

        Assert.Equal(MeasurementStatus.Abandoned, record.Status);
        Assert.False(record.IsBaselineEligible);
    }

    [Fact]
    public void SeveralCommands_KeepTheirOrderAndCount()
    {
        using var first = NewCommand();
        using var second = new SqlCommand("SELECT COUNT(*) FROM dbo.OrderLine WHERE OrderId = @OrderId");
        second.Parameters.Add(new SqlParameter("@OrderId", SqlDbType.BigInt) { Value = 7L });

        using (var scope = Watt.Measure("orders.detail"))
        {
            Execute(MicrosoftPrefix, first);
            Execute(MicrosoftPrefix, second);
            scope.Complete();
        }

        var record = Assert.Single(_fixture.Sink.Records);

        Assert.Equal(2, record.Commands.Count);
        Assert.Equal([1, 2], record.Commands.Select(measured => measured.Ordinal).Order().ToArray());
        Assert.Equal(2, record.TotalCommandCount);
    }

    [Fact]
    public void ACommandIsAttributedToTheInnermostScope()
    {
        using var outer = NewCommand();
        using var inner = new SqlCommand("SELECT 1");

        using (var parent = Watt.Measure("checkout.prepare"))
        {
            using (var child = Watt.Measure("carts.get-with-lines"))
            {
                Execute(MicrosoftPrefix, inner);
                child.Complete();
            }

            Execute(MicrosoftPrefix, outer);
            parent.Complete();
        }

        var childRecord = Assert.Single(_fixture.Sink.Records, record => record.Depth == 1);
        var parentRecord = Assert.Single(_fixture.Sink.RootRecords);

        Assert.Equal("SELECT 1", Assert.Single(childRecord.Commands).CommandText);
        Assert.Equal(outer.CommandText, Assert.Single(parentRecord.Commands).CommandText);
    }

    [Fact]
    public void InlineLiterals_RaiseADiagnostic()
    {
        using var command = new SqlCommand("SELECT * FROM dbo.[Order] WHERE StatusId = 4");

        using (var scope = Watt.Measure("orders.by-status"))
        {
            Execute(MicrosoftPrefix, command);
            scope.Complete();
        }

        var record = Assert.Single(_fixture.Sink.Records);

        Assert.Contains(record.Diagnostics, d => d.Code == DiagnosticCode.InlineLiteralsDetected);
    }

    [Fact]
    public void CaptureIsASingleton_SoEnablingTwiceDoesNotDoubleCount()
    {
        using var again = SqlClientCapture.Enable();
        using var command = NewCommand();

        Assert.Same(_capture, again);

        using (var scope = Watt.Measure("orders.pending-by-customer"))
        {
            Execute(MicrosoftPrefix, command);
            scope.Complete();
        }

        var record = Assert.Single(_fixture.Sink.Records);

        Assert.Single(record.Commands);
    }

    private static SqlCommand NewCommand()
    {
        var command = new SqlCommand(
            "SELECT o.OrderId, o.OrderNumber FROM dbo.[Order] o "
            + "WHERE o.CustomerId = @CustomerId AND o.PlacedOn >= @PlacedAfter");

        command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.UniqueIdentifier)
        {
            Value = Guid.NewGuid()
        });
        command.Parameters.Add(new SqlParameter("@PlacedAfter", SqlDbType.DateTime2)
        {
            Value = DateTime.UtcNow.AddDays(-90)
        });

        return command;
    }

    private void Execute(string prefix, SqlCommand command)
    {
        var operationId = Guid.NewGuid();
        Write(prefix, "WriteCommandBefore", operationId, command, exception: null);
        Write(prefix, "WriteCommandAfter", operationId, command, exception: null);
    }

    private void Write(
        string prefix,
        string operation,
        Guid operationId,
        SqlCommand command,
        Exception? exception) =>
        _listener.Write(
            prefix + "." + operation,
            new
            {
                OperationId = operationId,
                Command = command,
                Exception = exception
            });
}
