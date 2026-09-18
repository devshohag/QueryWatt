using System.Data;
using QueryWatt.Core.Instrumentation;
using QueryWatt.Reporting;
using Xunit;

namespace QueryWatt.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public sealed class ObserveModeTests(SqlServerFixture fixture) : IDisposable
{
    public void Dispose() => Watt.Instrumentation = InstrumentationLevel.Off;

    [Fact]
    public void ARequestThatQueriesOncePerRowIsReportedAsOneRepeatedCommand()
    {
        const int orders = 10;

        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");
        var previousSink = Watt.Sink;

        try
        {
            using (var sink = new JsonLinesMeasurementSink(path))
            {
                Watt.Sink = sink;
                Watt.Instrumentation = InstrumentationLevel.Full;

                using var scope = Watt.Measure("GET /orders");
                using var connection = fixture.OpenWrappedConnection();

                var orderIds = new List<long>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        $"SELECT TOP ({orders}) OrderId FROM dbo.[Order] ORDER BY OrderId;";

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        orderIds.Add(reader.GetInt64(0));
                    }
                }

                // The N+1 itself: one query per row of the first result, each one individually
                // cheap and individually innocent.
                foreach (var orderId in orderIds)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT Sku FROM dbo.OrderLine WHERE OrderId = @OrderId;";

                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@OrderId";
                    parameter.DbType = DbType.Int64;
                    parameter.Value = orderId;
                    command.Parameters.Add(parameter);

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                    }
                }

                scope.Complete();
            }

            var records = MeasurementRecordFile.Read(path);
            var record = Assert.Single(records);

            Assert.Equal("GET /orders", record.QueryId);
            Assert.Equal(orders + 1, record.OwnCommandCount);

            var report = ObservationReportFactory.Create(records);
            var finding = Assert.Single(report.Repetitions);

            Assert.Equal("GET /orders", finding.QueryId);
            Assert.Equal(orders, finding.Repetitions);
            Assert.NotNull(finding.TotalLogicalReads);
            Assert.True(finding.TotalLogicalReads > 0);
            Assert.Contains("OrderLine", finding.CommandText, StringComparison.Ordinal);

            // The reads the loop cost are visible in the rendered report, which is the whole point:
            // Query Store would see eleven healthy little queries and nothing to say about them.
            var rendered = ObservationReportRenderer.Render(report, ReportFormat.Console);

            Assert.Contains("Repeated commands", rendered, StringComparison.Ordinal);
            Assert.Contains($"×{orders}", rendered, StringComparison.Ordinal);
        }
        finally
        {
            Watt.Sink = previousSink;
            Watt.Instrumentation = InstrumentationLevel.Off;

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void AMeasuredRunSurvivesTheFileAndKeepsItsServerMetrics()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");
        var previousSink = Watt.Sink;

        try
        {
            using (var sink = new JsonLinesMeasurementSink(path))
            {
                Watt.Sink = sink;
                Watt.Instrumentation = InstrumentationLevel.Full;

                using var scope = Watt.Measure("orders.count.observed");
                using var connection = fixture.OpenConnection();
                using var command = connection.CreateCommand();

                command.CommandText = "SELECT COUNT(*) FROM dbo.[Order];";
                command.ExecuteScalar();

                scope.Complete();
            }

            var record = Assert.Single(MeasurementRecordFile.Read(path));
            var measured = Assert.Single(record.Commands);

            Assert.True(record.IsBaselineEligible);
            Assert.NotNull(measured.LogicalReads);
            Assert.True(measured.LogicalReads > 0);
            Assert.NotNull(measured.CpuTimeMilliseconds);

            var report = ObservationReportFactory.Create([record]);
            var query = Assert.Single(report.Queries);

            Assert.True(query.FullyMeasured);
            Assert.Equal(measured.LogicalReads, query.MedianLogicalReads);
        }
        finally
        {
            Watt.Sink = previousSink;
            Watt.Instrumentation = InstrumentationLevel.Off;

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
