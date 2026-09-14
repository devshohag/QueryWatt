using QueryWatt.Baselines;
using QueryWatt.Configuration;
using QueryWatt.Energy;

namespace QueryWatt.Reporting;

public sealed record VerificationReport(
    string Verdict,
    int ExitCode,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<QueryReport> Queries);

public sealed record QueryReport(
    string QueryName,
    string Verdict,
    bool QueryTextChanged,
    bool PlanShapeChanged,
    IReadOnlyList<MetricVerificationResult> Metrics,
    EnergyAssessment Energy);

public static class VerificationReportFactory
{
    public static VerificationReport Create(
        VerificationResult verification,
        ResolvedQueryWattConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(configuration);

        var queries = verification.Queries.Select(query =>
        {
            var configured = configuration.Queries.Single(item =>
                string.Equals(item.Request.Name, query.QueryName, StringComparison.Ordinal));
            var cpu = query.Metrics.Single(metric =>
                metric.MetricName == "cpuTimeMilliseconds");
            var energy = CpuCoefficientEnergyModel.Assess(
                cpu.Current,
                configured.ExecutionsPerDay,
                configuration.Energy.Enabled,
                configuration.Energy.WattsPerBusyCore);

            return new QueryReport(
                query.QueryName,
                query.Regressed ? VerificationVerdict.Regressed : VerificationVerdict.Passed,
                query.QueryTextChanged,
                query.PlanShapeChanged,
                query.Metrics,
                energy);
        }).ToArray();

        return new VerificationReport(
            verification.Verdict,
            verification.ExitCode,
            verification.Warnings,
            queries);
    }
}
