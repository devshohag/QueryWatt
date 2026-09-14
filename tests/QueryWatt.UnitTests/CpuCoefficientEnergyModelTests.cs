using QueryWatt.Energy;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class CpuCoefficientEnergyModelTests
{
    [Fact]
    public void Assess_ComputesDocumentedWattHourFormula()
    {
        var result = CpuCoefficientEnergyModel.Assess(
            cpuTimeMillisecondsPerExecution: 1000,
            executionsPerDay: 1584,
            energyEnabled: true,
            wattsPerBusyCore: 0.9);

        Assert.Equal(1d, result.MeasuredResourceIndex.CpuCoreSecondsPerExecution);
        Assert.Equal(1584d, result.MeasuredResourceIndex.CpuCoreSecondsPerDay);
        Assert.NotNull(result.EstimatedEnergy);
        Assert.Equal(0.396d, result.EstimatedEnergy!.WattHoursPerDay!.Value, 10);
        Assert.Equal("estimated-not-measured-not-carbon", result.EstimatedEnergy.Disclaimer);
    }

    [Fact]
    public void Assess_DoesNotEstimateWhenEnergyIsDisabled()
    {
        var result = CpuCoefficientEnergyModel.Assess(500, 100, false, null);

        Assert.NotNull(result.MeasuredResourceIndex.CpuCoreSecondsPerDay);
        Assert.Null(result.EstimatedEnergy);
    }
}
