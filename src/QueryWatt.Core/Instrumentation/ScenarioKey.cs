using System.Data;
using System.Text;

namespace QueryWatt.Core.Instrumentation;

/// <summary>
/// Builds the scenario key from the <em>shape</em> of the parameters, never from their values.
/// Contract v2 §8.1.
/// </summary>
/// <remarks>
/// Types are named by <see cref="DbType"/> rather than by any provider's own type names, because
/// this assembly stays provider-neutral.
/// </remarks>
public static class ScenarioKey
{
    /// <summary>Used when commands were observed but none carried a non-null parameter.</summary>
    public const string Empty = "p[]";

    /// <summary>Used when no command was observed at all.</summary>
    public const string None = "p[?]";

    /// <summary>
    /// Parameters whose value was null are excluded: the same query called with two filters and
    /// with four filters are different scenarios and are never compared with each other.
    /// </summary>
    public static string FromParameters(IEnumerable<CommandParameterInfo>? parameters)
    {
        if (parameters is null)
        {
            return None;
        }

        var supplied = parameters
            .Where(parameter => !parameter.IsNull)
            .Select(parameter => Normalize(parameter.Name) + ":" + parameter.Type)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (supplied.Length == 0)
        {
            return Empty;
        }

        var builder = new StringBuilder("p[");
        for (var index = 0; index < supplied.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(supplied[index]);
        }

        return builder.Append(']').ToString();
    }

    /// <summary>An explicitly named scenario, as supplied through <c>Scenario("name")</c>.</summary>
    public static string FromName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return "s[" + name.Trim() + "]";
    }

    private static string Normalize(string name) =>
        string.IsNullOrEmpty(name) ? name : name.TrimStart('@', ':', '?');
}
