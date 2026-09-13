using System.Data;

namespace QueryWatt.Core;

public sealed record MeasurementRequest(
    string Name,
    string CommandText,
    int WarmupRuns,
    int MeasuredRuns,
    IReadOnlyList<QueryParameter>? Parameters = null,
    CommandType CommandType = CommandType.Text,
    int CommandTimeoutSeconds = 30)
{
    public IReadOnlyList<QueryParameter> EffectiveParameters =>
        Parameters ?? Array.Empty<QueryParameter>();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A query name is required.", nameof(Name));
        }

        if (string.IsNullOrWhiteSpace(CommandText))
        {
            throw new ArgumentException("Command text is required.", nameof(CommandText));
        }

        if (WarmupRuns < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(WarmupRuns), "Warm-up runs cannot be negative.");
        }

        if (MeasuredRuns < 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MeasuredRuns),
                "The measurement contract requires at least 20 measured runs.");
        }

        if (CommandTimeoutSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CommandTimeoutSeconds),
                "Command timeout must be at least one second.");
        }
    }
}

public sealed record QueryParameter(
    string Name,
    DbType Type,
    object? Value,
    int? Size = null,
    byte? Precision = null,
    byte? Scale = null);
