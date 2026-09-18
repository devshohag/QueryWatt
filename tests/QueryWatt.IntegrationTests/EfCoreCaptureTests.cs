using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QueryWatt.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class EfCoreCaptureTests(SqlServerFixture fixture)
{
    [Fact(Skip = "Needs the QueryWatt connection wrapper (PR #9b): this stack closes the reader without advancing past the last result set, so SqlClient discards the STATISTICS IO/TIME messages before QueryWatt can read them.")]
    public async Task AnEfCoreLinqQueryComesBackUnchangedAndFullyMeasured()
    {
        var reference = await fixture
            .WithoutMeasurementAsync(QueryOrdersAsync)
            ;

        var measured = await fixture
            .MeasureAsync("orders.by-customer.efcore", QueryOrdersAsync)
            ;

        Assert.Equal(TestSchema.OrdersPerCustomer, reference.Count);
        Assert.Equal(reference.Count, measured.Result.Count);
        Assert.Equal(reference[0].OrderNumber, measured.Result[0].OrderNumber);

        var command = MeasurementAssertions.SingleFullyMeasuredCommand(measured.Record);

        Assert.Equal(TestSchema.OrdersPerCustomer, command.RowsReturned);

        // EF Core parameterises the LINQ predicate, so identity stays stable between runs.
        Assert.DoesNotContain(
            measured.Record.Diagnostics,
            diagnostic => diagnostic.Code
                == QueryWatt.Core.Instrumentation.DiagnosticCode.NoCommandsInScope);
    }

    [Fact(Skip = "Needs the QueryWatt connection wrapper (PR #9b): this stack closes the reader without advancing past the last result set, so SqlClient discards the STATISTICS IO/TIME messages before QueryWatt can read them.")]
    public async Task AGroupedReportQueryIsMeasuredWithItsGeneratedSql()
    {
        var measured = await fixture.MeasureAsync("reports.orders-per-status.efcore", async () =>
        {
            await using var context = NewContext();

            return await context.Orders
                .GroupBy(order => order.StatusId)
                .Select(group => new { StatusId = group.Key, Orders = group.Count() })
                .OrderBy(row => row.StatusId)
                .ToListAsync()
                ;
        });

        Assert.NotEmpty(measured.Result);

        var command = MeasurementAssertions.SingleFullyMeasuredCommand(measured.Record);

        Assert.Contains("GROUP BY", command.CommandText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Needs the QueryWatt connection wrapper (PR #9b): this stack closes the reader without advancing past the last result set, so SqlClient discards the STATISTICS IO/TIME messages before QueryWatt can read them.")]
    public async Task ARawSqlQueryThroughEfCoreIsMeasured()
    {
        var measured = await fixture.MeasureAsync("catalog.active-products.efcore-raw", async () =>
        {
            await using var context = NewContext();

            return await context.Orders
                .FromSqlRaw(
                    "SELECT * FROM dbo.[Order] WHERE CustomerId = {0}",
                    TestSchema.SecondaryCustomerId)
                .AsNoTracking()
                .ToListAsync()
                ;
        });

        Assert.Equal(TestSchema.OrdersPerCustomer, measured.Result.Count);

        MeasurementAssertions.SingleFullyMeasuredCommand(measured.Record);
    }

    private async Task<List<OrderEntity>> QueryOrdersAsync()
    {
        await using var context = NewContext();

        return await context.Orders
            .Where(order => order.CustomerId == TestSchema.PrimaryCustomerId
                            && order.PlacedOn >= TestSchema.SeedEpoch)
            .OrderByDescending(order => order.PlacedOn)
            .AsNoTracking()
            .ToListAsync()
            ;
    }

    private OrdersContext NewContext() => new(fixture.ConnectionString);

    internal sealed class OrdersContext(string connectionString) : DbContext
    {
        public DbSet<OrderEntity> Orders => Set<OrderEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder
                .UseSqlServer(connectionString)
                .EnableServiceProviderCaching(false);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var order = modelBuilder.Entity<OrderEntity>();

            order.ToTable("Order", "dbo");
            order.HasKey(entity => entity.OrderId);
            order.Property(entity => entity.OrderId).ValueGeneratedOnAdd();
            order.Property(entity => entity.OrderNumber).HasMaxLength(40);
            order.Property(entity => entity.TotalAmount).HasPrecision(18, 2);
        }
    }

    internal sealed class OrderEntity
    {
        public long OrderId { get; set; }

        public Guid CustomerId { get; set; }

        public string OrderNumber { get; set; } = string.Empty;

        public DateTime PlacedOn { get; set; }

        public decimal TotalAmount { get; set; }

        public int StatusId { get; set; }

        public DateTime? PromisedShipOn { get; set; }
    }
}
