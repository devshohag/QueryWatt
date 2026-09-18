using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using QueryWatt.SqlServer.Capture;

namespace QueryWatt.SqlServer.Fingerprints;

/// <summary>
/// What a measurement was taken on: the facts about the server that change what a query costs
/// without anyone touching the query. Contract v2 §9.
/// </summary>
/// <remarks>
/// Comparing a measurement from one server with a baseline accepted on another is the quickest way
/// to report a regression nobody caused. Rather than guess, QueryWatt records these facts and
/// refuses the comparison when they differ, naming the field that moved.
/// </remarks>
public sealed record ServerFingerprint(
    string ProductVersion,
    string Edition,
    int CompatibilityLevel,
    int MaxDegreeOfParallelism,
    string Collation)
{
    /// <summary>A short stable hash of every field, for storing in a baseline.</summary>
    public string Value => Hash(
        $"{ProductVersion}|{Edition}|{CompatibilityLevel.ToString(CultureInfo.InvariantCulture)}"
        + $"|{MaxDegreeOfParallelism.ToString(CultureInfo.InvariantCulture)}|{Collation}");

    /// <summary>Names the fields that differ, so a refusal can say why.</summary>
    /// <param name="other">The fingerprint to compare with.</param>
    /// <returns>One line per differing field; empty when the two match.</returns>
    public IReadOnlyList<string> Differences(ServerFingerprint other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var differences = new List<string>();

        Compare("SQL Server version", ProductVersion, other.ProductVersion);
        Compare("edition", Edition, other.Edition);
        Compare(
            "compatibility level",
            CompatibilityLevel.ToString(CultureInfo.InvariantCulture),
            other.CompatibilityLevel.ToString(CultureInfo.InvariantCulture));
        Compare(
            "max degree of parallelism",
            MaxDegreeOfParallelism.ToString(CultureInfo.InvariantCulture),
            other.MaxDegreeOfParallelism.ToString(CultureInfo.InvariantCulture));
        Compare("collation", Collation, other.Collation);

        return differences;

        void Compare(string field, string mine, string theirs)
        {
            if (!string.Equals(mine, theirs, StringComparison.Ordinal))
            {
                differences.Add($"{field}: {mine} → {theirs}");
            }
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
}

/// <summary>Reads the server facts a comparison depends on.</summary>
public static class ServerFingerprintProbe
{
    private const string ProbeSql = """
        SELECT
            CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128))   AS ProductVersion,
            CAST(SERVERPROPERTY('Edition') AS nvarchar(128))          AS Edition,
            CAST(DATABASEPROPERTYEX(DB_NAME(), 'CompatibilityLevel') AS int) AS CompatibilityLevel,
            CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128)) AS Collation,
            (SELECT CAST(value_in_use AS int) FROM sys.configurations
             WHERE name = 'max degree of parallelism')                AS Maxdop;
        """;

    /// <summary>Probes an open or closed connection, leaving it as it was found.</summary>
    /// <param name="connectionString">Connection string to probe with.</param>
    /// <returns>The fingerprint.</returns>
    public static ServerFingerprint Read(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        using var connection = new SqlConnection(connectionString);
        connection.Open();
        return Read(connection);
    }

    /// <summary>Probes an already-open connection.</summary>
    /// <param name="connection">An open connection.</param>
    /// <returns>The fingerprint.</returns>
    public static ServerFingerprint Read(SqlConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var command = connection.CreateCommand();

        // Marked so the probe is never mistaken for the application's own work.
        InternalCommandMarker.Mark(command);
        command.CommandType = CommandType.Text;
        command.CommandText = ProbeSql;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("The server fingerprint probe returned no rows.");
        }

        var fingerprint = new ServerFingerprint(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
            reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            reader.IsDBNull(3) ? string.Empty : reader.GetString(3));

        while (reader.NextResult())
        {
        }

        return fingerprint;
    }
}
