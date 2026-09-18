using System.Text.RegularExpressions;

namespace QueryWatt.SqlServer.Capture;

/// <summary>
/// Cheap, conservative text checks used for diagnostics and for deciding whether plan capture is
/// safe. Contract v2 §7, §13. Never a parser: when in doubt it answers "no".
/// </summary>
public static partial class CommandTextInspector
{
    /// <summary>
    /// True when the text carries inline literals instead of parameters, which makes the
    /// normalized query hash unstable between runs.
    /// </summary>
    public static bool ContainsInlineLiterals(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        return InlineLiteralRegex().IsMatch(commandText);
    }

    /// <summary>
    /// True only when the text is confidently a single read statement. A write token, a batch
    /// separator, an <c>EXEC</c>, DDL, or anything unrecognised answers false, so plan capture is
    /// skipped rather than risked.
    /// </summary>
    public static bool IsSingleReadStatement(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        var stripped = StripCommentsAndStrings(commandText);

        if (WriteOrControlTokenRegex().IsMatch(stripped))
        {
            return false;
        }

        // More than one terminated statement in the batch: not a single statement.
        var terminators = stripped.Count(character => character == ';');
        if (terminators > 1)
        {
            return false;
        }

        if (terminators == 1 && !stripped.TrimEnd().EndsWith(';'))
        {
            return false;
        }

        return LeadingReadTokenRegex().IsMatch(stripped);
    }

    /// <summary>
    /// Replaces string literals and comments with placeholders so token matching cannot be fooled
    /// by text inside quotes.
    /// </summary>
    public static string StripCommentsAndStrings(string commandText)
    {
        ArgumentNullException.ThrowIfNull(commandText);

        var withoutBlockComments = BlockCommentRegex().Replace(commandText, " ");
        var withoutLineComments = LineCommentRegex().Replace(withoutBlockComments, " ");
        return StringLiteralRegex().Replace(withoutLineComments, "''");
    }

    /// <summary>
    /// Collapses whitespace and replaces literals with placeholders so the same logical command
    /// hashes identically across runs. Values never survive this.
    /// </summary>
    public static string Normalize(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return string.Empty;
        }

        var stripped = StripCommentsAndStrings(commandText);
        var withoutNumbers = NumericLiteralRegex().Replace(stripped, "0");
        return WhitespaceRegex().Replace(withoutNumbers, " ").Trim();
    }

    [GeneratedRegex(@"'(?:''|[^'])*'", RegexOptions.CultureInvariant)]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"--[^\r\n]*", RegexOptions.CultureInvariant)]
    private static partial Regex LineCommentRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?<![\w.])\d+(\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumericLiteralRegex();

    [GeneratedRegex(
        @"\b(insert|update|delete|merge|truncate|exec|execute|create|alter|drop|grant|revoke|deny|backup|restore|dbcc|waitfor|declare|set)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WriteOrControlTokenRegex();

    [GeneratedRegex(@"^\s*(with|select)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingReadTokenRegex();

    [GeneratedRegex(
        @"(=|<|>|<=|>=|<>|!=|\bin\b|\blike\b|\bvalues\b)\s*\(?\s*(N?'|\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineLiteralRegex();
}
