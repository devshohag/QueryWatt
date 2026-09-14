using QueryWatt.Baselines;
using QueryWatt.Core;
using QueryWatt.Energy;
using QueryWatt.Reporting;
using Xunit;

namespace QueryWatt.UnitTests;

public sealed class ReportRendererTests
{
    [Theory]
    [InlineData(ReportFormat.Console)]
    [InlineData(ReportFormat.Json)]
    [InlineData(ReportFormat.Markdown)]
    public void Render_LabelsMeasuredAndEstimatedData(ReportFormat format)
    {
        var metric = new MetricVerificationResult(
            "logicalReads",
            100,
            200,
            100,
            100,
            new RegressionThreshold(25, 50),
            true,
            null,
            0,
            1,
            null,
            null);
        var energy = new EnergyAssessment(
            new CpuResourceIndex(1, 100, 100),
            new EnergyEstimate(
                CpuCoefficientEnergyModel.ModelId,
                0.9,
                0.00025,
                0.025,
                "estimated-not-measured-not-carbon"));
        var report = new VerificationReport(
            "regressed",
            1,
            [new QueryReport("query", "regressed", true, [metric], energy)]);

        var text = ReportRenderer.Render(report, format);

        Assert.Contains("measured", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("estimated", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("carbon", text, StringComparison.OrdinalIgnoreCase);
    }
}
