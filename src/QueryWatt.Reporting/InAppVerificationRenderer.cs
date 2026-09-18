using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QueryWatt.Baselines.InApp;

namespace QueryWatt.Reporting;

/// <summary>Renders the verdicts a run earned against its baseline.</summary>
public static class InAppVerificationRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Renders the result in the requested format.</summary>
    /// <param name="result">The comparison result.</param>
    /// <param name="format">Console, JSON, or Markdown.</param>
    /// <returns>The rendered report.</returns>
    public static string Render(InAppVerificationResult result, ReportFormat format)
    {
        ArgumentNullException.ThrowIfNull(result);

        return format switch
        {
            ReportFormat.Console => RenderConsole(result),
            ReportFormat.Json => JsonSerializer.Serialize(result, JsonOptions),
            ReportFormat.Markdown => RenderMarkdown(result),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static string RenderConsole(InAppVerificationResult result)
    {
        var builder = new StringBuilder();

        builder.AppendLine(CultureInfo.InvariantCulture,
            $"QueryWatt check: {Headline(result.Verdict)}");

        if (result.Results.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("Nothing measurable was found in this run.");
            return builder.ToString().TrimEnd();
        }

        foreach (var scenario in result.Results)
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"  {Marker(scenario.Verdict)} {scenario.QueryId}  {scenario.ScenarioKey}  [{scenario.Verdict}]");
            builder.AppendLine(CultureInfo.InvariantCulture, $"    {scenario.Reason}");

            if (scenario.Baseline is { } baseline && scenario.Current is { } current)
            {
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"    reads {baseline.LogicalReads:N0} → {current.LogicalReads:N0}"
                    + $"   rows {baseline.RowsReturned:N0} → {current.RowsReturned:N0}"
                    + $"   reads/row {baseline.ReadsPerRow} → {current.ReadsPerRow}");
            }

            if (scenario.Verdict == InAppVerdict.NewScenario)
            {
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"    accept with: querywatt check <run> --accept");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string RenderMarkdown(InAppVerificationResult result)
    {
        var builder = new StringBuilder();

        builder.AppendLine(CultureInfo.InvariantCulture, $"## QueryWatt check: {Headline(result.Verdict)}");

        if (result.Results.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("Nothing measurable was found in this run.");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine();
        builder.AppendLine("| | Query | Scenario | Verdict | Reads | Rows | Reads/row |");
        builder.AppendLine("| --- | --- | --- | --- | ---: | ---: | ---: |");

        foreach (var scenario in result.Results)
        {
            var reads = Change(scenario.Baseline?.LogicalReads, scenario.Current?.LogicalReads);
            var rows = Change(scenario.Baseline?.RowsReturned, scenario.Current?.RowsReturned);
            var perRow = Change(scenario.Baseline?.ReadsPerRow, scenario.Current?.ReadsPerRow);

            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| {Marker(scenario.Verdict)} | `{scenario.QueryId}` | `{scenario.ScenarioKey}` "
                + $"| {scenario.Verdict} | {reads} | {rows} | {perRow} |");
        }

        foreach (var scenario in result.Results.Where(item => item.Verdict != InAppVerdict.Unchanged))
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"**{scenario.QueryId}** — {scenario.Reason}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string Headline(InAppVerdict verdict) => verdict switch
    {
        InAppVerdict.Regression => "REGRESSION",
        InAppVerdict.PlanRegression => "PLAN REGRESSION",
        InAppVerdict.Incomparable => "INCOMPARABLE",
        InAppVerdict.Suspect => "SUSPECT",
        InAppVerdict.DataChange => "DATA CHANGE",
        InAppVerdict.NewScenario => "NEW SCENARIO",
        InAppVerdict.Improved => "IMPROVED",
        _ => "PASS"
    };

    // A symbol beside the word, never colour alone: a verdict has to survive a plain terminal,
    // a pasted log and a colour-blind reader.
    private static string Marker(InAppVerdict verdict) => verdict switch
    {
        InAppVerdict.Regression or InAppVerdict.PlanRegression => "x",
        InAppVerdict.Incomparable => "?",
        InAppVerdict.Suspect => "!",
        InAppVerdict.DataChange => "~",
        InAppVerdict.NewScenario => "+",
        InAppVerdict.Improved => "v",
        _ => "."
    };

    private static string Change(long? baseline, long? current) =>
        baseline is null || current is null
            ? "—"
            : $"{baseline.Value:N0} → {current.Value:N0}";

    private static string Change(double? baseline, double? current) =>
        baseline is null || current is null
            ? "—"
            : $"{baseline.Value.ToString("0.##", CultureInfo.InvariantCulture)} → "
              + current.Value.ToString("0.##", CultureInfo.InvariantCulture);
}
