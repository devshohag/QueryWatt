using System.Globalization;
using System.Text;
using System.Text.Json;

namespace QueryWatt.Reporting;

/// <summary>Renders an <see cref="ObservationReport"/> for a terminal, a pull request, or a tool.</summary>
public static class ObservationReportRenderer
{
    private const int CommandTextWidth = 72;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Renders the report in the requested format.</summary>
    /// <param name="report">The report to render.</param>
    /// <param name="format">Console, JSON, or Markdown.</param>
    /// <returns>The rendered report.</returns>
    public static string Render(ObservationReport report, ReportFormat format)
    {
        ArgumentNullException.ThrowIfNull(report);

        return format switch
        {
            ReportFormat.Console => RenderConsole(report),
            ReportFormat.Json => JsonSerializer.Serialize(report, JsonOptions),
            ReportFormat.Markdown => RenderMarkdown(report),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static string RenderConsole(ObservationReport report)
    {
        var builder = new StringBuilder();

        builder.AppendLine(CultureInfo.InvariantCulture, $"QueryWatt observation — {report.ScopeCount} scopes, {report.CommandCount} commands");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Total logical reads: {Format(report.TotalLogicalReads)}");

        if (report.Queries.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("Nothing was measured. Wrap the work you care about in Watt.Measure(\"...\").");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine();
        builder.AppendLine("Queries, by median reads:");
        builder.AppendLine();

        foreach (var query in report.Queries)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"  {query.QueryId}  {query.ScenarioKey}");
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"    runs {query.Executions}"
                + $"   reads {Format(query.MedianLogicalReads)}"
                + $"   rows {Format(query.MedianRowsReturned)}"
                + $"   reads/row {FormatRatio(query.ReadsPerRow)}"
                + $"   {query.MedianDurationMilliseconds.ToString("0.#", CultureInfo.InvariantCulture)} ms"
                + $"   via {query.Source}");

            if (query.Diagnostics.Count > 0)
            {
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"    note: {string.Join(", ", query.Diagnostics)}");
            }
        }

        if (report.Repetitions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Repeated commands (one statement, many executions in a single scope):");
            builder.AppendLine();

            foreach (var repetition in report.Repetitions)
            {
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"  {repetition.QueryId}  ×{repetition.Repetitions}"
                    + $"   reads {Format(repetition.TotalLogicalReads)}"
                    + $"   {repetition.TotalDurationMilliseconds.ToString("0.#", CultureInfo.InvariantCulture)} ms");
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"    {Truncate(repetition.CommandText, CommandTextWidth)}");
            }
        }

        foreach (var note in report.Notes)
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"Note: {note}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string RenderMarkdown(ObservationReport report)
    {
        var builder = new StringBuilder();

        builder.AppendLine("## QueryWatt observation");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"{report.ScopeCount} scopes, {report.CommandCount} commands, "
            + $"{Format(report.TotalLogicalReads)} logical reads.");

        if (report.Queries.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("Nothing was measured.");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine();
        builder.AppendLine("| Query | Scenario | Runs | Reads | Rows | Reads/row | ms | Source |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |");

        foreach (var query in report.Queries)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| `{EscapeMarkdown(query.QueryId)}` | `{EscapeMarkdown(query.ScenarioKey)}` "
                + $"| {query.Executions} | {Format(query.MedianLogicalReads)} "
                + $"| {Format(query.MedianRowsReturned)} | {FormatRatio(query.ReadsPerRow)} "
                + $"| {query.MedianDurationMilliseconds.ToString("0.#", CultureInfo.InvariantCulture)} "
                + $"| {query.Source} |");
        }

        if (report.Repetitions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Repeated commands");
            builder.AppendLine();
            builder.AppendLine("| Scope | Executions | Reads | ms | Command |");
            builder.AppendLine("| --- | ---: | ---: | ---: | --- |");

            foreach (var repetition in report.Repetitions)
            {
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"| `{EscapeMarkdown(repetition.QueryId)}` | {repetition.Repetitions} "
                    + $"| {Format(repetition.TotalLogicalReads)} "
                    + $"| {repetition.TotalDurationMilliseconds.ToString("0.#", CultureInfo.InvariantCulture)} "
                    + $"| `{EscapeMarkdown(Truncate(repetition.CommandText, CommandTextWidth))}` |");
            }
        }

        foreach (var note in report.Notes)
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"> {note}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string Format(long? value) =>
        value?.ToString("N0", CultureInfo.InvariantCulture) ?? "—";

    private static string FormatRatio(double? value) =>
        value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";

    private static string Truncate(string value, int width) =>
        value.Length <= width ? value : value[..width] + "…";

    private static string EscapeMarkdown(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);
}
