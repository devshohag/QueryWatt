using QueryWatt.Core;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class MeasurementRequestTests
{
    [Fact]
    public void Validate_RejectsFewerThanTwentyMeasuredRuns()
    {
        var request = new MeasurementRequest("seek", "SELECT 1", 2, 19);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(request.Validate);

        Assert.Equal("MeasuredRuns", exception.ParamName);
    }

    [Fact]
    public void Validate_AcceptsContractMinimum()
    {
        var request = new MeasurementRequest("seek", "SELECT 1", 2, 20);

        request.Validate();
    }
}
