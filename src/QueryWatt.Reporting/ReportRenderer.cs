using System.Globalization;
using System.Text;
using System.Text.Json;

namespace QueryWatt.Reporting;

public enum ReportFormat
{
    Console,
    Json,
    Markdown
}

public static class ReportRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static ReportFormat ParseFormat(string value) => value.ToLowerInvariant() switch
    {
        "console" => ReportFormat.Console,
        "json" => ReportFormat.Json,
        "markdown" or "md" => ReportFormat.Markdown,
        _ => throw new ArgumentException(
            $"Unsupported report format '{value}'. Use console, json, or markdown.",
            nameof(value))
    };

    public static string Render(VerificationReport report, ReportFormat format) => format switch
    {
        ReportFormat.Console => RenderConsole(report),
        ReportFormat.Json => JsonSerializer.Serialize(report, JsonOptions),
        ReportFormat.Markdown => RenderMarkdown(report),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static string RenderBaselineCreated(
        string baselinePath,
        int queryCount,
        int schemaVersion,
        ReportFormat format) => format switch
    {
        ReportFormat.Console =>
            $"QueryWatt baseline created{Environment.NewLine}Path: {baselinePath}{Environment.NewLine}Queries: {queryCount}{Environment.NewLine}Schema: {schemaVersion}",
        ReportFormat.Json => JsonSerializer.Serialize(new
        {
            status = "baseline-created",
            path = baselinePath,
            queryCount,
            schemaVersion
        }, JsonOptions),
        ReportFormat.Markdown =>
            $"## QueryWatt baseline created{Environment.NewLine}{Environment.NewLine}"
            + $"- Path: `{EscapeMarkdown(baselinePath)}`{Environment.NewLine}"
            + $"- Queries: {queryCount}{Environment.NewLine}"
            + $"- Schema: {schemaVersion}",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static string RenderConsole(VerificationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"QueryWatt verification: {report.Verdict.ToUpperInvariant()}");

        foreach (var query in report.Queries)
        {
            builder.AppendLine();
            builder.AppendLine($"Query: {query.QueryName} [{query.Verdict}]");
            builder.AppendLine($"Estimated plan hash changed: {(query.PlanChanged ? "yes" : "no")}");
            builder.AppendLine("Measured (IQR-filtered median):");
            foreach (var metric in query.Metrics)
            {
                builder.AppendLine(
                    $"  {metric.MetricName}: {Number(metric.Baseline)} -> {Number(metric.Current)} "
                    + $"(delta {Number(metric.AbsoluteChange)}, {Percent(metric.PercentChange)}) "
                    + $"threshold {Threshold(metric)} "
                    + $"[{(metric.Regressed ? "regressed" : metric.Note ?? "ok")}] "
                    + $"outliers {metric.BaselineOutlierCount}->{metric.CurrentOutlierCount} "
                    + $"p95 {P95(metric)}");
            }

            var resource = query.Energy.MeasuredResourceIndex;
            builder.AppendLine("Measured resource index:");
            builder.AppendLine(
                $"  CPU-core-seconds/execution: {Number(resource.CpuCoreSecondsPerExecution)}");
            if (resource.CpuCoreSecondsPerDay is not null)
            {
                builder.AppendLine(
                    $"  CPU-core-seconds/day: {Number(resource.CpuCoreSecondsPerDay.Value)}");
            }

            if (query.Energy.EstimatedEnergy is { } estimate)
            {
                builder.AppendLine("Estimated energy (not measured, not carbon):");
                builder.AppendLine(
                    $"  {estimate.ModelId}; coefficient {Number(estimate.WattsPerBusyCore)} W/busy-core");
                builder.AppendLine(
                    $"  Wh/execution: {Number(estimate.WattHoursPerExecution)}; "
                    + $"Wh/day: {(estimate.WattHoursPerDay is null ? "unavailable" : Number(estimate.WattHoursPerDay.Value))}");
            }
            else
            {
                builder.AppendLine("Estimated energy: unavailable (no energy profile configured)");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string RenderMarkdown(VerificationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"## QueryWatt verification: {report.Verdict.ToUpperInvariant()}");

        foreach (var query in report.Queries)
        {
            builder.AppendLine();
            builder.AppendLine($"### {EscapeMarkdown(query.QueryName)} — {query.Verdict}");
            builder.AppendLine();
            builder.AppendLine($"Estimated plan hash changed: **{(query.PlanChanged ? "yes" : "no")}**");
            builder.AppendLine();
            builder.AppendLine("| Measured metric | Baseline | Current | Change | Threshold | Verdict | Outliers | p95 | ");
            builder.AppendLine("|---|---:|---:|---:|---|---|---:|---:|");
            foreach (var metric in query.Metrics)
            {
                builder.AppendLine(
                    $"| {EscapeMarkdown(metric.MetricName)} | {Number(metric.Baseline)} | "
                    + $"{Number(metric.Current)} | {Number(metric.AbsoluteChange)} "
                    + $"({Percent(metric.PercentChange)}) | {Threshold(metric)} | "
                    + $"{(metric.Regressed ? "regressed" : metric.Note ?? "ok")} | "
                    + $"{metric.BaselineOutlierCount} → {metric.CurrentOutlierCount} | "
                    + $"{P95(metric)} |");
            }

            var resource = query.Energy.MeasuredResourceIndex;
            builder.AppendLine();
            builder.AppendLine("**Measured resource index**");
            builder.AppendLine();
            builder.AppendLine(
                $"- CPU-core-seconds/execution: {Number(resource.CpuCoreSecondsPerExecution)}");
            if (resource.CpuCoreSecondsPerDay is not null)
            {
                builder.AppendLine(
                    $"- CPU-core-seconds/day: {Number(resource.CpuCoreSecondsPerDay.Value)}");
            }

            builder.AppendLine();
            if (query.Energy.EstimatedEnergy is { } estimate)
            {
                builder.AppendLine("**Estimated energy — not measured, not carbon**");
                builder.AppendLine();
                builder.AppendLine($"- Model: `{estimate.ModelId}`");
                builder.AppendLine($"- Coefficient: {Number(estimate.WattsPerBusyCore)} W/busy-core");
                builder.AppendLine($"- Wh/execution: {Number(estimate.WattHoursPerExecution)}");
                builder.AppendLine(
                    $"- Wh/day: {(estimate.WattHoursPerDay is null ? "unavailable" : Number(estimate.WattHoursPerDay.Value))}");
            }
            else
            {
                builder.AppendLine("**Estimated energy:** unavailable — no energy profile configured.");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Percent(double? value) =>
        value is null ? "n/a (zero baseline)" : $"{Number(value.Value)}%";

    private static string Threshold(QueryWatt.Baselines.MetricVerificationResult metric) =>
        metric.Threshold is null
            ? "none"
            : $">{Number(metric.Threshold.Percent)}% and >{Number(metric.Threshold.Absolute)}";

    private static string P95(QueryWatt.Baselines.MetricVerificationResult metric) =>
        metric.BaselineP95 is null || metric.CurrentP95 is null
            ? "n/a"
            : $"{Number(metric.BaselineP95.Value)}->{Number(metric.CurrentP95.Value)}";

    private static string EscapeMarkdown(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);
}
