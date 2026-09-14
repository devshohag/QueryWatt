namespace QueryWatt.Reporting;

/// <summary>
/// One <c>--output FORMAT=PATH</c> request. Several of these let a single
/// measurement produce several report files; rendering the same result twice is
/// free, re-measuring for a second format is not.
/// </summary>
public sealed record ReportOutputSpec(ReportFormat Format, string Path)
{
    public static IReadOnlyList<ReportOutputSpec> ParseAll(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        return values.Select(Parse).ToArray();
    }

    public static ReportOutputSpec Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var separator = value.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new ArgumentException(
                "An --output value must be FORMAT=PATH, for example "
                + $"markdown=artifacts/querywatt-report.md. Received '{value}'.",
                nameof(value));
        }

        var format = ReportRenderer.ParseFormat(value[..separator].Trim());
        var path = value[(separator + 1)..].Trim();

        if (path.Length == 0)
        {
            throw new ArgumentException(
                $"An --output value must name a file path. Received '{value}'.",
                nameof(value));
        }

        return new ReportOutputSpec(format, path);
    }
}
