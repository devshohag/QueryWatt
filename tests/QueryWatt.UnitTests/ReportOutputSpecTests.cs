using QueryWatt.Reporting;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class ReportOutputSpecTests
{
    [Fact]
    public void ParseAll_AcceptsSeveralFormatsForOneMeasurement()
    {
        var parsed = ReportOutputSpec.ParseAll(
            ["markdown=artifacts/querywatt-report.md", "json=artifacts/querywatt-report.json"]);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(ReportFormat.Markdown, parsed[0].Format);
        Assert.Equal("artifacts/querywatt-report.md", parsed[0].Path);
        Assert.Equal(ReportFormat.Json, parsed[1].Format);
        Assert.Equal("artifacts/querywatt-report.json", parsed[1].Path);
    }

    [Theory]
    [InlineData("markdown")]
    [InlineData("=report.md")]
    [InlineData("markdown=")]
    public void Parse_RejectsValuesThatAreNotFormatEqualsPath(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() => ReportOutputSpec.Parse(value));

        Assert.Contains("FORMAT=PATH", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsAnUnsupportedFormat()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ReportOutputSpec.Parse("xml=report.xml"));

        Assert.Contains("xml", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAll_TreatsNoOutputsAsAnEmptyList() =>
        Assert.Empty(ReportOutputSpec.ParseAll(null));
}
