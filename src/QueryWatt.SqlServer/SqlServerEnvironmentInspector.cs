using Microsoft.Data.SqlClient;
using QueryWatt.Core;

namespace QueryWatt.SqlServer;

public sealed class SqlServerEnvironmentInspector : IMeasurementEnvironmentInspector
{
    private readonly string _connectionString;

    public SqlServerEnvironmentInspector(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            Pooling = false
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task<MeasurementEnvironmentDetails> InspectAsync(
        IReadOnlyList<string> tableNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tableNames);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var (version, edition) = await ReadServerIdentityAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        var rowCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var tableName in tableNames)
        {
            var quotedName = QuoteTableName(tableName);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT_BIG(*) FROM {quotedName};";
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            rowCounts.Add(tableName, Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        return new MeasurementEnvironmentDetails(version, edition, rowCounts);
    }

    private static async Task<(string Version, string Edition)> ReadServerIdentityAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                CONVERT(NVARCHAR(128), SERVERPROPERTY('ProductVersion')),
                CONVERT(NVARCHAR(128), SERVERPROPERTY('Edition'));
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("SQL Server identity query returned no row.");
        }

        return (reader.GetString(0), reader.GetString(1));
    }

    private static string QuoteTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name cannot be empty.", nameof(tableName));
        }

        var parts = tableName.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2
            || parts.Any(part => part.Length == 0 || part.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '_' or '$' or '#'))))
        {
            throw new ArgumentException(
                $"Table name '{tableName}' must be an unquoted one- or two-part SQL identifier.",
                nameof(tableName));
        }

        return string.Join('.', parts.Select(part => $"[{part.Replace("]", "]]", StringComparison.Ordinal)}]"));
    }
}
