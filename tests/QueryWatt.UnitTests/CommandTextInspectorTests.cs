using QueryWatt.SqlServer.Capture;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class CommandTextInspectorTests
{
    [Theory]
    [InlineData("SELECT * FROM dbo.[Order] WHERE CustomerId = '3F2504E0'")]
    [InlineData("SELECT * FROM dbo.[Order] WHERE StatusId = 4")]
    [InlineData("SELECT * FROM dbo.Product WHERE Name LIKE N'widget%'")]
    [InlineData("SELECT * FROM dbo.[Order] WHERE StatusId IN (1, 2)")]
    public void InlineLiterals_AreDetected(string commandText)
    {
        Assert.True(CommandTextInspector.ContainsInlineLiterals(commandText));
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.[Order] WHERE CustomerId = @CustomerId")]
    [InlineData("SELECT o.OrderId FROM dbo.[Order] o WHERE o.StatusId = @StatusId")]
    [InlineData("")]
    [InlineData(null)]
    public void ParameterisedText_IsNotFlagged(string? commandText)
    {
        Assert.False(CommandTextInspector.ContainsInlineLiterals(commandText));
    }

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT o.OrderId FROM dbo.[Order] o WHERE o.CustomerId = @CustomerId;")]
    [InlineData("WITH recent AS (SELECT 1 AS n) SELECT n FROM recent")]
    public void ASingleReadStatement_IsRecognised(string commandText)
    {
        Assert.True(CommandTextInspector.IsSingleReadStatement(commandText));
    }

    [Theory]
    [InlineData("UPDATE dbo.[Order] SET StatusId = 9 WHERE OrderId = @OrderId")]
    [InlineData("INSERT INTO dbo.[Order] (OrderId) VALUES (@OrderId)")]
    [InlineData("DELETE FROM dbo.[Order] WHERE OrderId = @OrderId")]
    [InlineData("MERGE dbo.[Order] AS target USING dbo.Staging AS source ON 1 = 1")]
    [InlineData("EXEC dbo.usp_CloseFulfilledOrders @WarehouseId")]
    [InlineData("CREATE INDEX IX_Order_PlacedOn ON dbo.[Order] (PlacedOn)")]
    [InlineData("DECLARE @x int = 1; SELECT @x")]
    [InlineData("SELECT 1; SELECT 2;")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_IsNotASingleReadStatement(string? commandText)
    {
        Assert.False(CommandTextInspector.IsSingleReadStatement(commandText));
    }

    [Fact]
    public void AWriteTokenInsideAStringLiteral_DoesNotDisqualifyARead()
    {
        Assert.True(CommandTextInspector.IsSingleReadStatement(
            "SELECT Label FROM dbo.Lookup WHERE Label = 'please update me'"));
    }

    [Fact]
    public void AWriteTokenInsideACommentDoesNotDisqualifyARead()
    {
        Assert.True(CommandTextInspector.IsSingleReadStatement(
            "-- todo: update this later\nSELECT 1"));
    }

    [Fact]
    public void Normalize_CollapsesWhitespaceAndReplacesLiterals()
    {
        var normalized = CommandTextInspector.Normalize(
            "SELECT   *\r\n  FROM dbo.[Order]\n WHERE StatusId = 4 AND Note = 'abc'");

        Assert.Equal("SELECT * FROM dbo.[Order] WHERE StatusId = 0 AND Note = ''", normalized);
    }

    [Fact]
    public void Normalize_IsStableAcrossDifferentLiteralValues()
    {
        var first = CommandTextInspector.Normalize("SELECT 1 WHERE Id = 41 AND Name = 'a'");
        var second = CommandTextInspector.Normalize("SELECT 1 WHERE Id = 9182 AND Name = 'zzzz'");

        Assert.Equal(first, second);
    }
}
