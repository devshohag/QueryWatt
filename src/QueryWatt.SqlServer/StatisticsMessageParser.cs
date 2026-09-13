using System.Globalization;
using System.Text.RegularExpressions;
using QueryWatt.Core;

namespace QueryWatt.SqlServer;

public sealed partial class StatisticsMessageParser
{
    public ParsedStatistics Parse(IReadOnlyList<string> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var text = string.Join(Environment.NewLine, messages);
        var executionMatches = ExecutionTimesRegex().Matches(text);

        if (executionMatches.Count == 0)
        {
            throw new MeasurementParseException(
                "No SQL Server Execution Times block was found. " +
                "The server language may be unsupported or the result stream may not have been fully drained.");
        }

        var statements = new List<StatementMetrics>(executionMatches.Count);
        var previousBlockEnd = 0;

        for (var index = 0; index < executionMatches.Count; index++)
        {
            var executionMatch = executionMatches[index];
            var precedingText = text[previousBlockEnd..executionMatch.Index];
            var tableIo = ParseTableIo(precedingText);

            statements.Add(new StatementMetrics(
                index + 1,
                ParseInt64(executionMatch.Groups["cpu"].Value),
                ParseInt64(executionMatch.Groups["elapsed"].Value),
                tableIo));

            previousBlockEnd = executionMatch.Index + executionMatch.Length;
        }

        // Some providers deliver IO messages after the last TIME block. Retain
        // those entries on the final statement instead of silently dropping them.
        var trailingIo = ParseTableIo(text[previousBlockEnd..]);
        if (trailingIo.Count > 0)
        {
            var last = statements[^1];
            statements[^1] = last with
            {
                TableIo = last.TableIo.Concat(trailingIo).ToArray()
            };
        }

        return new ParsedStatistics(statements);
    }

    private static IReadOnlyList<TableIoMetrics> ParseTableIo(string text)
    {
        var entries = new List<TableIoMetrics>();
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (Match tableMatch in TableLineRegex().Matches(text))
        {
            var tableName = tableMatch.Groups["table"].Value.Replace("''", "'", StringComparison.Ordinal);
            occurrences.TryGetValue(tableName, out var previousOccurrence);
            var occurrence = previousOccurrence + 1;
            occurrences[tableName] = occurrence;

            var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (Match metricMatch in IoMetricRegex().Matches(tableMatch.Groups["metrics"].Value))
            {
                values[metricMatch.Groups["name"].Value] =
                    ParseInt64(metricMatch.Groups["value"].Value);
            }

            entries.Add(new TableIoMetrics(
                tableName,
                occurrence,
                GetValue(values, "scan count"),
                GetValue(values, "logical reads"),
                GetValue(values, "physical reads"),
                GetValue(values, "read-ahead reads"),
                GetValue(values, "lob logical reads"),
                GetValue(values, "lob physical reads"),
                GetValue(values, "lob read-ahead reads")));
        }

        return entries;
    }

    private static long GetValue(IReadOnlyDictionary<string, long> values, string name) =>
        values.TryGetValue(name, out var value) ? value : 0;

    private static long ParseInt64(string value) =>
        long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(
        @"SQL Server Execution Times:\s*CPU time\s*=\s*(?<cpu>\d+)\s*ms,\s*elapsed time\s*=\s*(?<elapsed>\d+)\s*ms\.?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecutionTimesRegex();

    [GeneratedRegex(
        @"^[ \t]*Table\s+'(?<table>(?:''|[^'])+)'\.\s*(?<metrics>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex TableLineRegex();

    [GeneratedRegex(
        @"(?<name>lob page server read-ahead reads|lob page server reads|page server read-ahead reads|page server reads|lob logical reads|lob physical reads|lob read-ahead reads|logical reads|physical reads|read-ahead reads|scan count)\s+(?<value>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IoMetricRegex();
}

public sealed record ParsedStatistics(IReadOnlyList<StatementMetrics> Statements)
{
    public long LogicalReads => Statements.Sum(statement =>
        statement.TableIo.Sum(table => table.LogicalReads));

    public long LobLogicalReads => Statements.Sum(statement =>
        statement.TableIo.Sum(table => table.LobLogicalReads));

    public long PhysicalReads => Statements.Sum(statement =>
        statement.TableIo.Sum(table => table.PhysicalReads));

    public long ReadAheadReads => Statements.Sum(statement =>
        statement.TableIo.Sum(table => table.ReadAheadReads));

    public long LobPhysicalReads => Statements.Sum(statement =>
        statement.TableIo.Sum(table => table.LobPhysicalReads));

    public long LobReadAheadReads => Statements.Sum(statement =>
        statement.TableIo.Sum(table => table.LobReadAheadReads));

    public long CpuTimeMilliseconds => Statements.Sum(statement => statement.CpuTimeMilliseconds);
}
