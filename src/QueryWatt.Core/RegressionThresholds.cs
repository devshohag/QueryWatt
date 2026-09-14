namespace QueryWatt.Core;

public sealed record RegressionThreshold(double Percent, double Absolute)
{
    public void Validate(string metricName)
    {
        if (!double.IsFinite(Percent) || Percent < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Percent),
                $"{metricName} percent threshold must be a finite non-negative number.");
        }

        if (!double.IsFinite(Absolute) || Absolute < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Absolute),
                $"{metricName} absolute threshold must be a finite non-negative number.");
        }
    }
}

public sealed record QueryThresholds(
    RegressionThreshold LogicalReads,
    RegressionThreshold? CpuTimeMilliseconds = null,
    RegressionThreshold? ClientDurationMilliseconds = null)
{
    public void Validate()
    {
        LogicalReads.Validate("logicalReads");
        CpuTimeMilliseconds?.Validate("cpuTimeMilliseconds");
        ClientDurationMilliseconds?.Validate("clientDurationMilliseconds");
    }
}
