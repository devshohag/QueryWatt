namespace QueryWatt.Energy;

public static class CpuCoefficientEnergyModel
{
    public const string ModelId = "cpu-coefficient-v0.1";

    public static EnergyAssessment Assess(
        double cpuTimeMillisecondsPerExecution,
        double? executionsPerDay,
        bool energyEnabled,
        double? wattsPerBusyCore)
    {
        if (!double.IsFinite(cpuTimeMillisecondsPerExecution)
            || cpuTimeMillisecondsPerExecution < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cpuTimeMillisecondsPerExecution));
        }

        if (executionsPerDay is not null
            && (!double.IsFinite(executionsPerDay.Value) || executionsPerDay <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(executionsPerDay));
        }

        if (energyEnabled
            && (wattsPerBusyCore is null
                || !double.IsFinite(wattsPerBusyCore.Value)
                || wattsPerBusyCore <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(wattsPerBusyCore),
                "A finite positive watts-per-busy-core coefficient is required.");
        }

        var cpuCoreSecondsPerExecution = cpuTimeMillisecondsPerExecution / 1000d;
        var cpuCoreSecondsPerDay = executionsPerDay is null
            ? (double?)null : cpuCoreSecondsPerExecution * executionsPerDay.Value;

        EnergyEstimate? estimate = null;
        if (energyEnabled && wattsPerBusyCore is not null)
        {
            estimate = new EnergyEstimate(
                ModelId,
                wattsPerBusyCore.Value,
                cpuCoreSecondsPerExecution * wattsPerBusyCore.Value / 3600d,
                cpuCoreSecondsPerDay is null
                    ? (double?)null : cpuCoreSecondsPerDay.Value * wattsPerBusyCore.Value / 3600d,
                "estimated-not-measured-not-carbon");
        }

        return new EnergyAssessment(
            new CpuResourceIndex(
                cpuCoreSecondsPerExecution,
                executionsPerDay,
                cpuCoreSecondsPerDay),
            estimate);
    }
}

public sealed record EnergyAssessment(
    CpuResourceIndex MeasuredResourceIndex,
    EnergyEstimate? EstimatedEnergy);

public sealed record CpuResourceIndex(
    double CpuCoreSecondsPerExecution,
    double? ExecutionsPerDay,
    double? CpuCoreSecondsPerDay);

public sealed record EnergyEstimate(
    string ModelId,
    double WattsPerBusyCore,
    double WattHoursPerExecution,
    double? WattHoursPerDay,
    string Disclaimer);
