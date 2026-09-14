using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using QueryWatt.Configuration;
using QueryWatt.Core;

namespace QueryWatt.Baselines;

public static class FingerprintCalculator
{
    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string CalculateParameterSet(MeasurementRequest request)
    {
        var canonical = new StringBuilder();
        foreach (var parameter in request.EffectiveParameters.OrderBy(
                     parameter => parameter.Name,
                     StringComparer.OrdinalIgnoreCase))
        {
            canonical.Append(parameter.Name).Append('|')
                .Append(parameter.Type).Append('|')
                .Append(parameter.Size?.ToString(CultureInfo.InvariantCulture) ?? "null").Append('|')
                .Append(parameter.Precision?.ToString(CultureInfo.InvariantCulture) ?? "null").Append('|')
                .Append(parameter.Scale?.ToString(CultureInfo.InvariantCulture) ?? "null").Append('|')
                .Append(FormatValue(parameter.Value)).Append('\n');
        }

        return Sha256(canonical.ToString());
    }

    public static string CalculateSeed(
        ResolvedQueryWattConfiguration configuration,
        MeasurementEnvironmentDetails environment)
    {
        var canonical = new StringBuilder();
        foreach (var path in configuration.SeedScriptPaths)
        {
            canonical.Append(Sha256(File.ReadAllText(path))).Append('\n');
        }

        foreach (var table in configuration.TableNames)
        {
            if (!environment.TableRowCounts.TryGetValue(table, out var rowCount))
            {
                throw new InvalidOperationException($"Row count is missing for table '{table}'.");
            }

            canonical.Append(table).Append('|')
                .Append(rowCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return Sha256(canonical.ToString());
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        byte[] bytes => Convert.ToBase64String(bytes),
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
