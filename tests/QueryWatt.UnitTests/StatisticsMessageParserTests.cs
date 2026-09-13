using QueryWatt.SqlServer;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class StatisticsMessageParserTests
{
    private readonly StatisticsMessageParser _parser = new();

    [Fact]
    public void Parse_AggregatesTablesAndKeepsLobReadsSeparate()
    {
        string[] messages =
        [
            "Table 'Ticket'. Scan count 1, logical reads 14280, physical reads 2, page server reads 0, read-ahead reads 12, page server read-ahead reads 0, lob logical reads 7, lob physical reads 1, lob page server reads 0, lob read-ahead reads 3, lob page server read-ahead reads 0.",
            "Table 'Worktable'. Scan count 0, logical reads 24, physical reads 0, page server reads 0, read-ahead reads 0, page server read-ahead reads 0, lob logical reads 0, lob physical reads 0, lob page server reads 0, lob read-ahead reads 0, lob page server read-ahead reads 0.",
            "SQL Server Execution Times:\r\n   CPU time = 86 ms,  elapsed time = 112 ms."
        ];

        var result = _parser.Parse(messages);

        Assert.Equal(14304L, result.LogicalReads);
        Assert.Equal(7L, result.LobLogicalReads);
        Assert.Equal(2L, result.PhysicalReads);
        Assert.Equal(12L, result.ReadAheadReads);
        Assert.Equal(1L, result.LobPhysicalReads);
        Assert.Equal(3L, result.LobReadAheadReads);
        Assert.Equal(86L, result.CpuTimeMilliseconds);
        Assert.Equal(2, result.Statements[0].TableIo.Count);
    }

    [Fact]
    public void Parse_DoesNotOverwriteReadAheadWithPageServerReadAhead()
    {
        string[] messages =
        [
            "Table 'Worktable'. Scan count 0, logical reads 0, physical reads 0, page server reads 0, read-ahead reads 2649, page server read-ahead reads 0, lob logical reads 0, lob physical reads 0, lob page server reads 0, lob read-ahead reads 0, lob page server read-ahead reads 0.",
            "SQL Server Execution Times: CPU time = 1922 ms, elapsed time = 2006 ms."
        ];

        var result = _parser.Parse(messages);

        Assert.Equal(2649L, result.ReadAheadReads);
        Assert.Equal(2649L, result.Statements[0].TableIo[0].ReadAheadReads);
    }

    [Fact]
    public void Parse_ExcludesParseAndCompileTime()
    {
        string[] messages =
        [
            "SQL Server parse and compile time: CPU time = 400 ms, elapsed time = 500 ms.",
            "Table 'Ticket'. Scan count 1, logical reads 10, physical reads 0, read-ahead reads 0, lob logical reads 0, lob physical reads 0, lob read-ahead reads 0.",
            "SQL Server Execution Times: CPU time = 3 ms, elapsed time = 4 ms."
        ];

        var result = _parser.Parse(messages);

        Assert.Equal(3L, result.CpuTimeMilliseconds);
    }

    [Fact]
    public void Parse_RetainsRepeatedTableOccurrencesAcrossStatements()
    {
        string[] messages =
        [
            "Table 'Ticket'. Scan count 1, logical reads 10, physical reads 0, read-ahead reads 0, lob logical reads 0, lob physical reads 0, lob read-ahead reads 0.",
            "SQL Server Execution Times: CPU time = 1 ms, elapsed time = 2 ms.",
            "PRINT: application message that must be ignored",
            "Table 'Ticket'. Scan count 2, logical reads 30, physical reads 0, read-ahead reads 4, lob logical reads 0, lob physical reads 0, lob read-ahead reads 0.",
            "SQL Server Execution Times: CPU time = 5 ms, elapsed time = 7 ms."
        ];

        var result = _parser.Parse(messages);

        Assert.Equal(2, result.Statements.Count);
        Assert.Equal(40L, result.LogicalReads);
        Assert.Equal(6L, result.CpuTimeMilliseconds);
        Assert.Single(result.Statements[0].TableIo);
        Assert.Single(result.Statements[1].TableIo);
    }

    [Fact]
    public void Parse_FailsLoudlyWhenExecutionBlockIsMissing()
    {
        string[] messages =
        [
            "Table 'Ticket'. Scan count 1, logical reads 10, physical reads 0, read-ahead reads 0."
        ];

        var exception = Assert.Throws<MeasurementParseException>(() => _parser.Parse(messages));

        Assert.Contains("No SQL Server Execution Times block", exception.Message);
    }

    [Fact]
    public void Parse_HandlesEscapedQuoteInTableName()
    {
        string[] messages =
        [
            "Table 'Customer''s Audit'. Scan count 1, logical reads 9, physical reads 0, read-ahead reads 0.",
            "SQL Server Execution Times: CPU time = 0 ms, elapsed time = 1 ms."
        ];

        var result = _parser.Parse(messages);

        Assert.Equal("Customer's Audit", result.Statements[0].TableIo[0].TableName);
    }
}
