namespace QueryWatt.Configuration;

public sealed class QueryWattConfiguration
{
    public int SchemaVersion { get; set; }
    public ConnectionConfiguration Connection { get; set; } = new();
    public MeasurementConfiguration Measurement { get; set; } = new();
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

public sealed class QueryConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string CommandType { get; set; } = "text";
    public List<ParameterConfiguration> Parameters { get; set; } = [];
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
    IReadOnlyList<QueryWatt.Core.MeasurementRequest> Requests);
