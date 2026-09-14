namespace QueryWatt.Configuration;

public sealed class QueryWattConfiguration
{
    public int SchemaVersion { get; set; }
    public string BaselineFile { get; set; } = "querywatt-baseline.json";
    public ConnectionConfiguration Connection { get; set; } = new();
    public EnvironmentConfiguration Environment { get; set; } = new();
    public MeasurementConfiguration Measurement { get; set; } = new();
    public ThresholdsConfiguration Thresholds { get; set; } = new();
    public List<QueryConfiguration> Queries { get; set; } = [];
}

public sealed class ConnectionConfiguration
{
    public string EnvironmentVariable { get; set; } = "QUERYWATT_CONNECTION_STRING";
}

public sealed class MeasurementConfiguration
{
    public int WarmupRuns { get; set; } = 3;
    public int MeasuredRuns { get; set; } = 20;
    public int CommandTimeoutSeconds { get; set; } = 60;
}

public sealed class EnvironmentConfiguration
{
    public string? ContainerImageTag { get; set; }
    public List<string> SeedScripts { get; set; } = [];
    public List<string> Tables { get; set; } = [];
}

public sealed class ThresholdsConfiguration
{
    public MetricThresholdConfiguration LogicalReads { get; set; } = new()
    {
        Percent = 25,
        Absolute = 1000
    };

    public MetricThresholdConfiguration? CpuTimeMilliseconds { get; set; }
    public MetricThresholdConfiguration? ClientDurationMilliseconds { get; set; }
}

public sealed class MetricThresholdConfiguration
{
    public double Percent { get; set; }
    public double Absolute { get; set; }
}

public sealed class QueryConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string CommandType { get; set; } = "text";
    public List<ParameterConfiguration> Parameters { get; set; } = [];
    public ThresholdsConfiguration? Thresholds { get; set; }
}

public sealed class ParameterConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Value { get; set; }
    public int? Size { get; set; }
    public byte? Precision { get; set; }
    public byte? Scale { get; set; }
}

public sealed record ResolvedQueryWattConfiguration(
    string ConfigurationPath,
    string ConnectionStringEnvironmentVariable,
    string BaselinePath,
    string? ContainerImageTag,
    IReadOnlyList<string> SeedScriptPaths,
    IReadOnlyList<string> TableNames,
    IReadOnlyList<ResolvedQueryConfiguration> Queries)
{
    public IReadOnlyList<QueryWatt.Core.MeasurementRequest> Requests =>
        Queries.Select(query => query.Request).ToArray();
}

public sealed record ResolvedQueryConfiguration(
    QueryWatt.Core.MeasurementRequest Request,
    QueryWatt.Core.QueryThresholds Thresholds);
