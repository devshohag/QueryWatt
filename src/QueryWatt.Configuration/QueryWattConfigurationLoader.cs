using System.Data;
using System.Globalization;
using QueryWatt.Core;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace QueryWatt.Configuration;

public sealed class QueryWattConfigurationLoader
{
    public const int SupportedSchemaVersion = 1;

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public ResolvedQueryWattConfiguration Load(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);

        var fullPath = Path.GetFullPath(configurationPath);
        if (!File.Exists(fullPath))
        {
            throw new ConfigurationException($"Configuration file was not found: {fullPath}");
        }

        QueryWattConfiguration configuration;
        try
        {
            configuration = _deserializer.Deserialize<QueryWattConfiguration>(File.ReadAllText(fullPath))
                ?? throw new ConfigurationException("Configuration file is empty.");
        }
        catch (YamlException exception)
        {
            throw new ConfigurationException(
                $"Invalid YAML at line {exception.Start.Line}, column {exception.Start.Column}: {exception.Message}",
                exception);
        }

        ValidateRoot(configuration);

        var configurationDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new ConfigurationException("Configuration directory could not be resolved.");

        var requests = configuration.Queries
            .Select(query => ResolveQuery(
                query,
                configuration.Measurement,
                query.Thresholds ?? configuration.Thresholds,
                configurationDirectory))
            .ToArray();

        var seedScriptPaths = configuration.Environment.SeedScripts
            .Select(path => ResolveExistingFile(path, configurationDirectory, "Seed script"))
            .ToArray();

        return new ResolvedQueryWattConfiguration(
            fullPath,
            configuration.Connection.EnvironmentVariable,
            Path.GetFullPath(configuration.BaselineFile, configurationDirectory),
            configuration.Environment.ContainerImageTag,
            seedScriptPaths,
            configuration.Environment.Tables.ToArray(),
            new ResolvedEnergyConfiguration(
                configuration.Energy.Enabled,
                configuration.Energy.WattsPerBusyCore),
            requests);
    }

    private static void ValidateRoot(QueryWattConfiguration configuration)
    {
        if (configuration.SchemaVersion != SupportedSchemaVersion)
        {
            throw new ConfigurationException(
                $"Unsupported schemaVersion '{configuration.SchemaVersion}'. Expected {SupportedSchemaVersion}.");
        }

        if (configuration.Connection is null
            || string.IsNullOrWhiteSpace(configuration.Connection.EnvironmentVariable))
        {
            throw new ConfigurationException("connection.environmentVariable is required.");
        }

        if (configuration.Measurement is null)
        {
            throw new ConfigurationException("measurement is required.");
        }

        if (string.IsNullOrWhiteSpace(configuration.BaselineFile))
        {
            throw new ConfigurationException("baselineFile is required.");
        }

        if (configuration.Environment is null
            || configuration.Environment.SeedScripts is null
            || configuration.Environment.Tables is null)
        {
            throw new ConfigurationException("environment, seedScripts, and tables are required.");
        }

        if (configuration.Environment.SeedScripts.Count == 0
            || configuration.Environment.Tables.Count == 0)
        {
            throw new ConfigurationException(
                "At least one environment.seedScripts entry and one environment.tables entry are required.");
        }

        EnsureUniqueEntries(configuration.Environment.SeedScripts, "environment.seedScripts");
        EnsureUniqueEntries(configuration.Environment.Tables, "environment.tables");

        if (configuration.Thresholds is null)
        {
            throw new ConfigurationException("thresholds is required.");
        }

        if (configuration.Energy is null)
        {
            throw new ConfigurationException("energy is required.");
        }

        if (configuration.Energy.Enabled
            && (configuration.Energy.WattsPerBusyCore is null
                || !double.IsFinite(configuration.Energy.WattsPerBusyCore.Value)
                || configuration.Energy.WattsPerBusyCore <= 0))
        {
            throw new ConfigurationException(
                "energy.wattsPerBusyCore must be a finite positive number when energy is enabled.");
        }

        if (configuration.Queries is null || configuration.Queries.Count == 0)
        {
            throw new ConfigurationException("At least one query must be configured.");
        }

        var duplicateQuery = configuration.Queries
            .GroupBy(query => query.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;

        if (duplicateQuery is not null)
        {
            throw new ConfigurationException($"Query names must be unique. Duplicate: '{duplicateQuery}'.");
        }
    }

    private static void EnsureUniqueEntries(IReadOnlyList<string> values, string fieldName)
    {
        var duplicate = values
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;

        if (duplicate is not null)
        {
            throw new ConfigurationException(
                $"{fieldName} contains duplicate value '{duplicate}'.");
        }
    }

    private static ResolvedQueryConfiguration ResolveQuery(
        QueryConfiguration query,
        MeasurementConfiguration measurement,
        ThresholdsConfiguration thresholds,
        string configurationDirectory)
    {
        if (string.IsNullOrWhiteSpace(query.Name))
        {
            throw new ConfigurationException("Every query requires a name.");
        }

        if (string.IsNullOrWhiteSpace(query.File))
        {
            throw new ConfigurationException($"Query '{query.Name}' requires a file.");
        }

        var queryPath = Path.GetFullPath(query.File, configurationDirectory);
        if (!File.Exists(queryPath))
        {
            throw new ConfigurationException($"Query '{query.Name}' file was not found: {queryPath}");
        }

        if (query.Parameters is null)
        {
            throw new ConfigurationException($"Query '{query.Name}' parameters must be a list when provided.");
        }

        var commandType = (query.CommandType ?? string.Empty).ToLowerInvariant() switch
        {
            "text" => CommandType.Text,
            "storedprocedure" or "stored-procedure" => CommandType.StoredProcedure,
            _ => throw new ConfigurationException(
                $"Query '{query.Name}' has unsupported commandType '{query.CommandType}'.")
        };

        var duplicateParameter = query.Parameters
            .GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;

        if (duplicateParameter is not null)
        {
            throw new ConfigurationException(
                $"Query '{query.Name}' has duplicate parameter '{duplicateParameter}'.");
        }

        var parameters = query.Parameters
            .Select(parameter => ResolveParameter(query.Name, parameter))
            .ToArray();

        var request = new MeasurementRequest(
            query.Name,
            File.ReadAllText(queryPath),
            measurement.WarmupRuns,
            measurement.MeasuredRuns,
            parameters,
            commandType,
            measurement.CommandTimeoutSeconds);

        try
        {
            request.Validate();
        }
        catch (ArgumentException exception)
        {
            throw new ConfigurationException(
                $"Query '{query.Name}' has invalid measurement settings: {exception.Message}",
                exception);
        }

        if (query.ExecutionsPerDay is not null
            && (!double.IsFinite(query.ExecutionsPerDay.Value)
                || query.ExecutionsPerDay <= 0))
        {
            throw new ConfigurationException(
                $"Query '{query.Name}' executionsPerDay must be a finite positive number.");
        }

        var resolvedThresholds = ResolveThresholds(query.Name, thresholds);
        return new ResolvedQueryConfiguration(
            request,
            resolvedThresholds,
            query.ExecutionsPerDay);
    }

    private static QueryThresholds ResolveThresholds(
        string queryName,
        ThresholdsConfiguration thresholds)
    {
        if (thresholds.LogicalReads is null)
        {
            throw new ConfigurationException(
                $"Query '{queryName}' requires a logicalReads threshold.");
        }

        var resolved = new QueryThresholds(
            ResolveThreshold(thresholds.LogicalReads),
            thresholds.CpuTimeMilliseconds is null
                ? null
                : ResolveThreshold(thresholds.CpuTimeMilliseconds),
            thresholds.ClientDurationMilliseconds is null
                ? null
                : ResolveThreshold(thresholds.ClientDurationMilliseconds));

        try
        {
            resolved.Validate();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new ConfigurationException(
                $"Query '{queryName}' has an invalid threshold: {exception.Message}",
                exception);
        }

        return resolved;
    }

    private static RegressionThreshold ResolveThreshold(MetricThresholdConfiguration threshold) =>
        new(threshold.Percent, threshold.Absolute);

    private static string ResolveExistingFile(
        string path,
        string configurationDirectory,
        string description)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ConfigurationException($"{description} path cannot be empty.");
        }

        var fullPath = Path.GetFullPath(path, configurationDirectory);
        if (!File.Exists(fullPath))
        {
            throw new ConfigurationException($"{description} was not found: {fullPath}");
        }

        return fullPath;
    }

    private static QueryParameter ResolveParameter(
        string queryName,
        ParameterConfiguration parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter.Name))
        {
            throw new ConfigurationException($"Query '{queryName}' contains a parameter without a name.");
        }

        var dbType = ParseDbType(queryName, parameter);

        try
        {
            return new QueryParameter(
                parameter.Name,
                dbType,
                ParseValue(parameter.Value, dbType),
                parameter.Size,
                parameter.Precision,
                parameter.Scale);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new ConfigurationException(
                $"Query '{queryName}' parameter '{parameter.Name}' value is not valid for type '{parameter.Type}'.",
                exception);
        }
    }

    private static DbType ParseDbType(string queryName, ParameterConfiguration parameter) =>
        (parameter.Type ?? string.Empty).ToLowerInvariant() switch
        {
            "string" => DbType.String,
            "ansistring" or "ansi-string" => DbType.AnsiString,
            "int16" => DbType.Int16,
            "int32" or "int" => DbType.Int32,
            "int64" or "long" => DbType.Int64,
            "decimal" => DbType.Decimal,
            "double" => DbType.Double,
            "single" or "float" => DbType.Single,
            "boolean" or "bool" => DbType.Boolean,
            "guid" or "uniqueidentifier" => DbType.Guid,
            "date" => DbType.Date,
            "datetime" => DbType.DateTime,
            "datetime2" => DbType.DateTime2,
            "datetimeoffset" => DbType.DateTimeOffset,
            "time" => DbType.Time,
            "binary" => DbType.Binary,
            _ => throw new ConfigurationException(
                $"Query '{queryName}' parameter '{parameter.Name}' has unsupported type '{parameter.Type}'.")
        };

    private static object? ParseValue(string? value, DbType dbType)
    {
        if (value is null)
        {
            return null;
        }

        return dbType switch
        {
            DbType.String or DbType.AnsiString => value,
            DbType.Int16 => short.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            DbType.Int32 => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            DbType.Int64 => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            DbType.Decimal => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture),
            DbType.Double => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
            DbType.Single => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
            DbType.Boolean => bool.Parse(value),
            DbType.Guid => Guid.Parse(value),
            DbType.Date => DateOnly.Parse(value, CultureInfo.InvariantCulture),
            DbType.DateTime or DbType.DateTime2 => DateTime.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            DbType.DateTimeOffset => DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            DbType.Time => TimeSpan.Parse(value, CultureInfo.InvariantCulture),
            DbType.Binary => Convert.FromBase64String(value),
            _ => throw new NotSupportedException($"DbType '{dbType}' is not supported.")
        };
    }
}
