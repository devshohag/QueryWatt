using System.Data;
using QueryWatt.Core.Instrumentation;
using QueryWatt.Reporting;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class ObservationReportTests
{
    [Fact]
    public void RepeatedExecutionsOfOneQueryAreRolledUpWithAMedian()
    {
        var records = new[]
        {
            Scope("orders.by-customer", Command(1, "SELECT 1", reads: 100, rows: 10)),
            Scope("orders.by-customer", Command(1, "SELECT 1", reads: 120, rows: 10)),
            Scope("orders.by-customer", Command(1, "SELECT 1", reads: 110, rows: 10))
        };

        var report = ObservationReportFactory.Create(records);

        var query = Assert.Single(report.Queries);

        Assert.Equal(3, query.Executions);
        Assert.Equal(110, query.MedianLogicalReads);
        Assert.Equal(10, query.MedianRowsReturned);
        Assert.Equal(11d, query.ReadsPerRow);
        Assert.True(query.FullyMeasured);
    }

    [Fact]
    public void OneStatementRunManyTimesInsideAScopeIsReported()
    {
        var lineItems = Enumerable
            .Range(1, 37)
            .Select(ordinal => Command(
                ordinal,
                "SELECT * FROM dbo.OrderLine WHERE OrderId = @OrderId",
                reads: 4,
                rows: 3))
            .ToArray();

        var commands = new[] { Command(0, "SELECT * FROM dbo.[Order] WHERE CustomerId = @Id", reads: 8, rows: 37) }
            .Concat(lineItems)
            .ToArray();

        var report = ObservationReportFactory.Create([Scope("GET /orders", commands)]);

        var finding = Assert.Single(report.Repetitions);

        Assert.Equal("GET /orders", finding.QueryId);
        Assert.Equal(37, finding.Repetitions);
        Assert.Equal(37 * 4, finding.TotalLogicalReads);
        Assert.Contains("OrderLine", finding.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameStatementLaidOutDifferentlyStillGroupsTogether()
    {
        var commands = new[]
        {
            Command(1, "SELECT  *\n FROM dbo.Product", reads: 2, rows: 1),
            Command(2, "SELECT * FROM dbo.Product", reads: 2, rows: 1),
            Command(3, "SELECT *   FROM   dbo.Product", reads: 2, rows: 1)
        };

        var report = ObservationReportFactory.Create([Scope("catalog.page", commands)], repetitionThreshold: 3);

        Assert.Single(report.Repetitions);
    }

    [Fact]
    public void ChildScopesAreCountedOnceAndRolledUpSeparately()
    {
        var child = Scope("catalog.lookup", Command(1, "SELECT 1", reads: 5, rows: 1)) with { Depth = 1 };
        var parent = Scope("checkout.prepare", Command(1, "SELECT 2", reads: 7, rows: 1)) with
        {
            Children = [child]
        };

        var report = ObservationReportFactory.Create([parent]);

        Assert.Equal(2, report.ScopeCount);
        Assert.Equal(2, report.CommandCount);
        Assert.Equal(12, report.TotalLogicalReads);
        Assert.Equal(2, report.Queries.Count);
    }

    [Fact]
    public void AQueryWithNoServerMetricsIsCalledOutRatherThanShownAsZero()
    {
        var record = Scope(
            "orders.light",
            new MeasuredCommand(1, "SELECT 1", CommandType.Text, MeasurementSource.AdoNet, [], CommandOutcome.Succeeded)
            {
                ClientDurationMilliseconds = 2d,
                RowsReturned = 4
            });

        var report = ObservationReportFactory.Create([record]);

        var query = Assert.Single(report.Queries);

        Assert.False(query.FullyMeasured);
        Assert.Null(query.MedianLogicalReads);
        Assert.Null(query.ReadsPerRow);
        Assert.Contains(report.Notes, note => note.Contains("no server-side reads", StringComparison.Ordinal));
    }

    [Fact]
    public void ARecordSurvivesTheRoundTripThroughTheMeasurementFile()
    {
        var record = Scope("orders.round-trip", Command(1, "SELECT 1", reads: 42, rows: 7));

        var line = MeasurementRecordFile.ToLine(record);
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            File.WriteAllLines(path, [line, "{ this line is torn", string.Empty]);

            var read = Assert.Single(MeasurementRecordFile.Read(path));

            Assert.Equal(record.QueryId, read.QueryId);
            Assert.Equal(record.ScenarioKey, read.ScenarioKey);
            Assert.Equal(MeasurementStatus.Completed, read.Status);
            Assert.Equal(42, Assert.Single(read.Commands).LogicalReads);
            Assert.True(read.IsBaselineEligible);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheConsoleReportNamesTheQueriesAndTheRepetitions()
    {
        var commands = Enumerable
            .Range(1, 6)
            .Select(ordinal => Command(ordinal, "SELECT * FROM dbo.OrderLine WHERE OrderId = @Id", reads: 3, rows: 2))
            .ToArray();

        var report = ObservationReportFactory.Create([Scope("GET /orders", commands)]);
        var rendered = ObservationReportRenderer.Render(report, ReportFormat.Console);

        Assert.Contains("GET /orders", rendered, StringComparison.Ordinal);
        Assert.Contains("×6", rendered, StringComparison.Ordinal);
        Assert.Contains("Repeated commands", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkdownReportIsATable()
    {
        var report = ObservationReportFactory.Create([Scope("orders.by-customer", Command(1, "SELECT 1", 10, 2))]);
        var rendered = ObservationReportRenderer.Render(report, ReportFormat.Markdown);

        Assert.Contains("| Query | Scenario |", rendered, StringComparison.Ordinal);
        Assert.Contains("orders.by-customer", rendered, StringComparison.Ordinal);
    }

    private static MeasurementRecord Scope(string queryId, params MeasuredCommand[] commands) =>
        new(
            queryId,
            "p[]",
            MeasurementSource.AdoNet,
            MeasurementStatus.Completed,
            0,
            null,
            DateTimeOffset.UnixEpoch,
            12.5d,
            commands,
            [],
            []);

    private static MeasuredCommand Command(int ordinal, string text, long reads, long rows) =>
        new(ordinal, text, CommandType.Text, MeasurementSource.AdoNet, [], CommandOutcome.Succeeded)
        {
            ClientDurationMilliseconds = 1.5d,
            LogicalReads = reads,
            CpuTimeMilliseconds = 0L,
            RowsReturned = rows
        };
}
