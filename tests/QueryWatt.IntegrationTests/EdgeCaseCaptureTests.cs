using System.Data;
using Microsoft.Data.SqlClient;
using QueryWatt.Core.Instrumentation;
using Xunit;

namespace QueryWatt.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class EdgeCaseCaptureTests(SqlServerFixture fixture) : IDisposable
{
    public void Dispose() => Watt.Instrumentation = InstrumentationLevel.Off;

    [Fact]
    public void AFailingCommandIsRecordedWithItsErrorAndKeptOutOfTheBaseline()
    {
        Begin();

        MeasurementRecord? record;

        using (var scope = Watt.Measure("orders.divide-by-zero"))
        {
            try
            {
                using var connection = fixture.OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1 / 0;";
                command.ExecuteScalar();
            }
            catch (SqlException exception)
            {
                scope.Fail(exception);
            }

            record = scope.Record;
        }

        Assert.NotNull(record);
        Assert.Equal(MeasurementStatus.Failed, record!.Status);
        Assert.False(record.IsBaselineEligible);
        Assert.Contains("SqlException", record.ExceptionType ?? string.Empty);

        var failedCommand = Assert.Single(record.Commands);

        Assert.Equal(CommandOutcome.Failed, failedCommand.Outcome);
        Assert.Equal(8134, failedCommand.SqlErrorNumber);
    }

    [Fact]
    public void ACommandOutsideEveryScopeIsNotMeasured()
    {
        Begin();

        using (var connection = fixture.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TOP (1) ProductId FROM dbo.Product;";
            command.ExecuteScalar();
        }

        Assert.Empty(fixture.Sink.Records);
        Assert.Null(Watt.Current);
    }

    [Fact]
    public void AReaderStillOpenAtCompleteRaisesADiagnostic()
    {
        Begin();

        using (var scope = Watt.Measure("orders.reader-left-open"))
        {
            using var connection = fixture.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT TOP (5) ProductId FROM dbo.Product;";

            using var reader = command.ExecuteReader();
            reader.Read();

            scope.State!.ReaderOpened();
            scope.Complete();
        }

        var record = Assert.Single(fixture.Sink.Records);

        Assert.Equal(MeasurementStatus.Completed, record.Status);
        Assert.Contains(record.Diagnostics, d => d.Code == DiagnosticCode.OpenReaderAtComplete);
    }

    [Fact]
    public void NestedScopesEachGetTheirOwnMeasurement()
    {
        Begin();

        using (var parent = Watt.Measure("checkout.prepare"))
        {
            using (var child = Watt.Measure("catalog.product-lookup"))
            {
                Scalar("SELECT COUNT(*) FROM dbo.Product;");
                child.Complete();
            }

            Scalar("SELECT COUNT(*) FROM dbo.[Order];");
            parent.Complete();
        }

        var childRecord = Assert.Single(fixture.Sink.Records, record => record.Depth == 1);
        var parentRecord = Assert.Single(fixture.Sink.RootRecords);

        MeasurementAssertions.AssertGatingMetrics(Assert.Single(childRecord.Commands));
        MeasurementAssertions.AssertGatingMetrics(Assert.Single(parentRecord.Commands));

        Assert.Equal("checkout.prepare", childRecord.ParentQueryId);
        Assert.Equal(1, parentRecord.OwnCommandCount);
        Assert.Equal(2, parentRecord.TotalCommandCount);
    }

    [Fact]
    public void AScopeWithNoDatabaseWorkSaysSo()
    {
        Begin();

        using (var scope = Watt.Measure("service.no-database-work"))
        {
            scope.Complete();
        }

        var record = Assert.Single(fixture.Sink.Records);

        Assert.Contains(record.Diagnostics, d => d.Code == DiagnosticCode.NoCommandsInScope);
        Assert.False(record.IsBaselineEligible);
    }

    [Fact]
    public void AMeasuredRecordConvertsIntoOneRunMetrics()
    {
        var measured = fixture.Measure("orders.count.for-run-metrics", () =>
        {
            using var connection = fixture.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM dbo.[Order];";
            return (int)command.ExecuteScalar()!;
        });

        Assert.Equal(2 * TestSchema.OrdersPerCustomer, measured.Result);
        Assert.True(measured.Record.TryCreateRunMetrics(1, out var runMetrics));
        Assert.NotNull(runMetrics);
        Assert.Equal(1, runMetrics!.RunNumber);
        Assert.True(runMetrics.LogicalReads > 0);
        Assert.True(runMetrics.ClientDurationMilliseconds > 0);
        Assert.NotEmpty(runMetrics.Statements);
    }

    [Fact]
    public void InlineLiteralsAreReportedEvenWhenTheQueryWorks()
    {
        var measured = fixture.Measure("orders.inline-literal", () =>
        {
            using var connection = fixture.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM dbo.[Order] WHERE StatusId = 2;";
            return (int)command.ExecuteScalar()!;
        });

        Assert.True(measured.Result > 0);
        Assert.Contains(
            measured.Record.Diagnostics,
            d => d.Code == DiagnosticCode.InlineLiteralsDetected);
    }

    private void Begin()
    {
        fixture.Sink.Clear();
        Watt.Instrumentation = InstrumentationLevel.Full;
    }

    private void Scalar(string commandText)
    {
        using var connection = fixture.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandType = CommandType.Text;
        command.ExecuteScalar();
    }
}
