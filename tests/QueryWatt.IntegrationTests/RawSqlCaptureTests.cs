using System.Data;
using Microsoft.Data.SqlClient;
using QueryWatt.Core.Instrumentation;
using Xunit;

namespace QueryWatt.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class RawSqlCaptureTests(SqlServerFixture fixture)
{
    private const string OrdersByCustomer = """
        SELECT o.OrderId, o.OrderNumber, o.PlacedOn, o.TotalAmount, o.StatusId
        FROM   dbo.[Order] o
        WHERE  o.CustomerId = @CustomerId
          AND  o.PlacedOn  >= @PlacedAfter
        ORDER BY o.PlacedOn DESC;
        """;

    [Fact]
    public void ADataTableComesBackUnchangedAndFullyMeasured()
    {
        var reference = fixture.WithoutMeasurement(() => FillOrders());
        var measured = fixture.Measure("orders.by-customer.datatable", () => FillOrders());

        // The application's own result is untouched.
        Assert.Equal(reference.Rows.Count, measured.Result.Rows.Count);
        Assert.Equal(reference.Columns.Count, measured.Result.Columns.Count);
        Assert.Equal(TestSchema.OrdersPerCustomer, measured.Result.Rows.Count);
        Assert.Equal(
            reference.Rows[0]["OrderNumber"],
            measured.Result.Rows[0]["OrderNumber"]);

        var command = MeasurementAssertions.SingleFullyMeasuredCommand(measured.Record);

        Assert.Equal(CommandType.Text, command.CommandType);
        Assert.Equal(TestSchema.OrdersPerCustomer, command.RowsReturned);
        Assert.Equal("p[CustomerId:Guid, PlacedAfter:DateTime2]", measured.Record.ScenarioKey);
    }

    [Fact]
    public void AMultiResultDataSetKeepsItsTableCount()
    {
        const string sql = """
            SELECT TOP (5) p.ProductId, p.Sku FROM dbo.Product p ORDER BY p.ProductId;
            SELECT TOP (3) c.CustomerId, c.DisplayName FROM dbo.Customer c ORDER BY c.Email;
            """;

        var reference = fixture.WithoutMeasurement(() => FillDataSet(sql));
        var measured = fixture.Measure("catalog.two-result-sets", () => FillDataSet(sql));

        // STATISTICS IO and TIME publish on InfoMessage, outside the result stream, so no extra
        // table appears. This is the assertion that makes in-app measurement safe at all.
        Assert.Equal(2, reference.Tables.Count);
        Assert.Equal(2, measured.Result.Tables.Count);
        Assert.Equal(5, measured.Result.Tables[0].Rows.Count);
        Assert.Equal(2, measured.Result.Tables[1].Rows.Count);

        MeasurementAssertions.SingleFullyMeasuredCommand(measured.Record);
    }

    [Fact]
    public void ADataReaderIsMeasuredOnceItHasBeenDrained()
    {
        var measured = fixture.Measure("orders.by-customer.reader", () =>
        {
            using var connection = fixture.OpenConnection();
            using var command = NewOrdersCommand(connection);
            using var reader = command.ExecuteReader();

            var rows = 0;
            while (reader.Read())
            {
                rows++;
            }

            // Advancing past the last result set is what makes SqlClient raise the trailing
            // STATISTICS IO/TIME messages instead of discarding them on reader close. The
            // QueryWatt connection wrapper (PR #9b) does this for the application, after which
            // this drain can come out of the test.
            while (reader.NextResult())
            {
            }

            return rows;
        });

        Assert.Equal(TestSchema.OrdersPerCustomer, measured.Result);

        // A reader's statistics messages arrive after the driver's "after" event, which is why
        // commands are recorded when the scope closes. Contract v2 §6.
        MeasurementAssertions.SingleFullyMeasuredCommand(measured.Record);
    }

    [Fact]
    public void TwoCommandsInOneScopeAreMeasuredSeparately()
    {
        var measured = fixture.Measure("orders.two-commands", () =>
        {
            using var connection = fixture.OpenConnection();

            var orders = FillOrders(connection);

            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(*) FROM dbo.OrderLine;";
            var lines = (int)countCommand.ExecuteScalar()!;

            return (Orders: orders.Rows.Count, Lines: lines);
        });

        Assert.Equal(TestSchema.OrdersPerCustomer, measured.Result.Orders);
        Assert.Equal(2 * TestSchema.OrdersPerCustomer * TestSchema.LinesPerOrder, measured.Result.Lines);

        Assert.Equal(2, measured.Record.Commands.Count);
        Assert.Equal(
            [1, 2],
            measured.Record.Commands.Select(command => command.Ordinal).Order().ToArray());

        foreach (var command in measured.Record.Commands)
        {
            MeasurementAssertions.AssertGatingMetrics(command);
        }

        Assert.True(
            measured.Record.IsBaselineEligible,
            MeasurementAssertions.DescribeDiagnostics(measured.Record));
    }

    [Fact]
    public void ReadsAreStableAcrossRunsOnAFixedDataSet()
    {
        var first = fixture.Measure("orders.by-customer.stable", () => FillOrders());
        var second = fixture.Measure("orders.by-customer.stable", () => FillOrders());

        var firstReads = Assert.Single(first.Record.Commands).LogicalReads;
        var secondReads = Assert.Single(second.Record.Commands).LogicalReads;

        // The whole product rests on this: same query, same data, same reads.
        Assert.Equal(firstReads, secondReads);
    }

    [Fact]
    public void LightInstrumentationReportsRowsButNotReads()
    {
        var measured = fixture.Measure(
            "orders.by-customer.light",
            () => FillOrders(),
            InstrumentationLevel.Light);

        var command = Assert.Single(measured.Record.Commands);

        Assert.Equal(TestSchema.OrdersPerCustomer, command.RowsReturned);
        Assert.NotNull(command.ClientDurationMilliseconds);

        // Light mode touches no session option, so the server-side metrics stay null.
        Assert.Null(command.LogicalReads);
        Assert.Null(command.CpuTimeMilliseconds);
        Assert.False(measured.Record.IsBaselineEligible);
    }

    private DataTable FillOrders()
    {
        using var connection = fixture.OpenConnection();
        return FillOrders(connection);
    }

    private static DataTable FillOrders(SqlConnection connection)
    {
        using var command = NewOrdersCommand(connection);
        using var adapter = new SqlDataAdapter(command);

        var dataSet = new DataSet();
        adapter.Fill(dataSet);
        return dataSet.Tables[0];
    }

    private DataSet FillDataSet(string sql)
    {
        using var connection = fixture.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var adapter = new SqlDataAdapter(command);
        var dataSet = new DataSet();
        adapter.Fill(dataSet);
        return dataSet;
    }

    private static SqlCommand NewOrdersCommand(SqlConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = OrdersByCustomer;
        command.Parameters.Add("@CustomerId", SqlDbType.UniqueIdentifier).Value =
            TestSchema.PrimaryCustomerId;
        command.Parameters.Add("@PlacedAfter", SqlDbType.DateTime2).Value = TestSchema.SeedEpoch;
        return command;
    }
}
