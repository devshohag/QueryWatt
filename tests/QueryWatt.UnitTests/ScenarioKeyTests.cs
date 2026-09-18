using System.Data;
using QueryWatt.Core.Instrumentation;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class ScenarioKeyTests
{
    [Fact]
    public void FromParameters_SortsByNameAndStripsTheSigil()
    {
        var key = ScenarioKey.FromParameters(
        [
            TestCommands.Parameter("@PlacedAfter", DbType.DateTime2),
            TestCommands.Parameter("@CustomerId", DbType.Guid)
        ]);

        Assert.Equal("p[CustomerId:Guid, PlacedAfter:DateTime2]", key);
    }

    [Fact]
    public void FromParameters_ExcludesParametersThatWereNull()
    {
        var key = ScenarioKey.FromParameters(
        [
            TestCommands.Parameter("@Keyword", DbType.String),
            TestCommands.Parameter("@CategoryId", DbType.Int32, isNull: true),
            TestCommands.Parameter("@InStockOnly", DbType.Boolean, isNull: true)
        ]);

        Assert.Equal("p[Keyword:String]", key);
    }

    [Fact]
    public void TwoFiltersAndFourFilters_AreDifferentScenarios()
    {
        // The case that makes a naive tool cry regression: same query, more filters supplied.
        var twoFilters = ScenarioKey.FromParameters(
        [
            TestCommands.Parameter("@Keyword", DbType.String),
            TestCommands.Parameter("@CategoryId", DbType.Int32, isNull: true)
        ]);

        var fourFilters = ScenarioKey.FromParameters(
        [
            TestCommands.Parameter("@Keyword", DbType.String),
            TestCommands.Parameter("@CategoryId", DbType.Int32),
            TestCommands.Parameter("@From", DbType.DateTime2),
            TestCommands.Parameter("@To", DbType.DateTime2)
        ]);

        Assert.NotEqual(twoFilters, fourFilters);
    }

    [Fact]
    public void FromParameters_DeduplicatesRepeatedShapesAcrossCommands()
    {
        var key = ScenarioKey.FromParameters(
        [
            TestCommands.Parameter("@OrderId", DbType.Int64),
            TestCommands.Parameter("@OrderId", DbType.Int64)
        ]);

        Assert.Equal("p[OrderId:Int64]", key);
    }

    [Fact]
    public void FromParameters_IsEmpty_WhenEveryParameterWasNull()
    {
        var key = ScenarioKey.FromParameters(
            [TestCommands.Parameter("@Keyword", DbType.String, isNull: true)]);

        Assert.Equal(ScenarioKey.Empty, key);
    }

    [Fact]
    public void FromParameters_IsNone_WhenNothingWasObserved()
    {
        Assert.Equal(ScenarioKey.None, ScenarioKey.FromParameters(null));
    }

    [Fact]
    public void FromName_WrapsAnExplicitScenario()
    {
        Assert.Equal("s[wide-date-range]", ScenarioKey.FromName("  wide-date-range  "));
    }

    [Fact]
    public void FromName_RejectsBlankNames()
    {
        Assert.Throws<ArgumentException>(() => { _ = ScenarioKey.FromName(" "); });
    }
}
