using QueryWatt.SqlServer;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class ShowPlanParserTests
{
    [Fact]
    public void Parse_ExtractsOrderedStatementHashes()
    {
        var xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence><Batch><Statements>
                <StmtSimple QueryHash="0x1111" QueryPlanHash="0xAAAA" />
                <StmtSimple QueryHash="0x2222" QueryPlanHash="0xBBBB" />
              </Statements></Batch></BatchSequence>
            </ShowPlanXML>
            """;

        var result = ShowPlanParser.Parse([xml]);

        Assert.Equal(2, result.Count);
        Assert.Equal("0x1111", result[0].QueryHash);
        Assert.Equal("0xBBBB", result[1].QueryPlanHash);
        Assert.Equal(2, result[1].Ordinal);
    }

    [Fact]
    public void Parse_UsesStableFallbackWhenSqlServerOmitsHashes()
    {
        var first = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence><Batch><Statements>
                <StmtSimple StatementId="1" StatementText="SELECT * FROM dbo.Ticket" QueryHash="">
                  <QueryPlan CompileTime="4" CachedPlanSize="24">
                    <RelOp NodeId="0" PhysicalOp="Index Seek" LogicalOp="Index Seek" />
                  </QueryPlan>
                </StmtSimple>
              </Statements></Batch></BatchSequence>
            </ShowPlanXML>
            """;
        var second = first
            .Replace("CompileTime=\"4\"", "CompileTime=\"91\"", StringComparison.Ordinal)
            .Replace("CachedPlanSize=\"24\"", "CachedPlanSize=\"48\"", StringComparison.Ordinal);

        var firstResult = Assert.Single(ShowPlanParser.Parse([first]));
        var secondResult = Assert.Single(ShowPlanParser.Parse([second]));

        Assert.StartsWith("SHA256:", firstResult.QueryHash);
        Assert.StartsWith("SHA256:", firstResult.QueryPlanHash);
        Assert.Equal(firstResult, secondResult);
    }

    [Fact]
    public void Parse_FallbackChangesWhenPlanShapeChanges()
    {
        const string template = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence><Batch><Statements>
                <StmtSimple StatementText="SELECT * FROM dbo.Ticket">
                  <QueryPlan><RelOp PhysicalOp="{0}" LogicalOp="{0}" /></QueryPlan>
                </StmtSimple>
              </Statements></Batch></BatchSequence>
            </ShowPlanXML>
            """;

        var seek = Assert.Single(ShowPlanParser.Parse(
            [string.Format(template, "Index Seek")]));
        var scan = Assert.Single(ShowPlanParser.Parse(
            [string.Format(template, "Index Scan")]));

        Assert.NotEqual(seek.QueryPlanHash, scan.QueryPlanHash);
    }

    [Fact]
    public void Parse_ReturnsEmptyWhenPlanFingerprintIsUnavailable()
    {
        var result = ShowPlanParser.Parse(["<ShowPlanXML />"]);

        Assert.Empty(result);
    }
}

