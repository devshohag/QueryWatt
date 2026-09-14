using System.Text.Json;
using System.Text.Json.Serialization;

namespace QueryWatt.Baselines;

public sealed class BaselineJsonStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public void Save(string path, BaselineDocument baseline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(baseline);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Baseline directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var temporaryPath = fullPath + ".tmp";
        var json = JsonSerializer.Serialize(baseline, SerializerOptions) + Environment.NewLine;
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, fullPath, overwrite: true);
    }

    public BaselineDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new BaselineConfigurationException($"Baseline file was not found: {fullPath}");
        }

        try
        {
            var baseline = JsonSerializer.Deserialize<BaselineDocument>(
                File.ReadAllText(fullPath),
                SerializerOptions)
                ?? throw new BaselineConfigurationException("Baseline file is empty.");

            if (baseline.SchemaVersion != BaselineContract.SchemaVersion)
            {
                throw new BaselineConfigurationException(
                    $"Unsupported baseline schemaVersion '{baseline.SchemaVersion}'. "
                    + $"Expected {BaselineContract.SchemaVersion}; run baseline again.");
            }

            if (string.IsNullOrWhiteSpace(baseline.ToolVersion)
                || baseline.Environment is null
                || baseline.Queries is null)
            {
                throw new BaselineConfigurationException(
                    "Baseline JSON is missing required toolVersion, environment, or queries data.");
            }

            return baseline;
        }
        catch (JsonException exception)
        {
            throw new BaselineConfigurationException(
                $"Baseline JSON is invalid: {exception.Message}",
                exception);
        }
    }
}

public sealed class BaselineConfigurationException : Exception
{
    public BaselineConfigurationException(string message)
        : base(message)
    {
    }

    public BaselineConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class EnvironmentMismatchException(string message) : Exception(message);
