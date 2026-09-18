using Dapper;
using Xunit;

namespace QueryWatt.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class DapperCaptureTests(SqlServerFixture fixture)
{
    private const string OrdersByCustomer = """
        SELECT o.OrderId, o.OrderNumber, o.PlacedOn, o.TotalAmount, o.StatusId
        FROM   dbo.[Order] o
        WHERE  o.CustomerId = @CustomerId
          AND  o.PlacedOn  >= @PlacedAfter
        ORDER BY o.PlacedOn DESC;
        """;

    [Fact]
    public async Task ADapperQueryComesBackUnchangedAndFullyMeasured()
    {
        var reference = await fixture.WithoutMeasurementAsync(QueryOrdersAsync);
        var measured = await fixture
            .MeasureAsync("orders.by-customer.dapper", QueryOrdersAsync)
            ;

        Assert.Equal(TestSchema.OrdersPerCustomer, reference.Count);
        Assert.Equal(reference.Count, measured.Result.Count);
        Assert.Equal(reference[0].OrderNumber, measured.Result[0].OrderNumber);
        Assert.Equal(reference[0].TotalAmount, measured.Result[0].TotalAmount);

        var command = MeasurementAssertions.SingleFullyMeasuredCommand(measured.Record);

        Assert.Equal(TestSchema.OrdersPerCustomer, command.RowsReturned);
        Assert.Equal("p[CustomerId:Guid, PlacedAfter:DateTime]", measured.Record.ScenarioKey);
    }

    [Fact]
    public async Task ADapperMultiMappedQueryIsMeasuredAsOneCommand()
    {
        const string sql = """
            SELECT o.OrderId, o.OrderNumber, o.PlacedOn, o.TotalAmount, o.StatusId,
                   l.OrderLineId, l.Sku, l.Quantity, l.UnitPrice
            FROM   dbo.[Order] o
            JOIN   dbo.OrderLine l ON l.OrderId = o.OrderId
            WHERE  o.CustomerId = @CustomerId;
            """;

        var measured = await fixture.MeasureAsync("orders.with-lines.dapper", async () =>
        {
            await using var connection = fixture.OpenConnection();
            var lookup = new Dictionary<long, OrderRow>();

            await connection.QueryAsync<OrderRow, OrderLineRow, OrderRow>(
                sql,
                (order, line) =>
                {
                    if (!lookup.TryGetValue(order.OrderId, out var existing))
                    {
                        existing = order;
                        lookup.Add(order.OrderId, existing);
                    }

                    existing.Lines.Add(line);
                    return existing;
                },
                new { CustomerId = TestSchema.PrimaryCustomerId },
                splitOn: "OrderLineId");

            return lookup;
        });

        Assert.Equal(TestSchema.OrdersPerCustomer, measured.Result.Count);
        Assert.All(
            measured.Result.Values,
            order => Assert.Equal(TestSchema.LinesPerOrder, order.Lines.Count));

        var command = MeasurementAssertions.SingleFullyMeasuredCommand(measured.Record);

        Assert.Equal(
            TestSchema.OrdersPerCustomer * TestSchema.LinesPerOrder,
            command.RowsReturned);
    }

    private async Task<List<OrderRow>> QueryOrdersAsync()
    {
        await using var connection = fixture.OpenConnection();

        var rows = await connection.QueryAsync<OrderRow>(
                OrdersByCustomer,
                new
                {
                    CustomerId = TestSchema.PrimaryCustomerId,
                    PlacedAfter = TestSchema.SeedEpoch
                })
            ;

        return rows.ToList();
    }

    private sealed class OrderRow
    {
        public long OrderId { get; init; }

        public string OrderNumber { get; init; } = string.Empty;

        public DateTime PlacedOn { get; init; }

        public decimal TotalAmount { get; init; }

        public int StatusId { get; init; }

        public List<OrderLineRow> Lines { get; } = [];
    }

    private sealed class OrderLineRow
    {
        public long OrderLineId { get; init; }

        public string Sku { get; init; } = string.Empty;

        public int Quantity { get; init; }

        public decimal UnitPrice { get; init; }
    }
}
