using System.Text.Json;
using QueryWatt.Configuration;
using QueryWatt.Core;
using QueryWatt.SqlServer;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project samples/QueryWatt.MeasurementProbe -- <querywatt.yml|query.sql>");
    return 3;
}

var queryPath = Path.GetFullPath(args[0]);
if (!File.Exists(queryPath))
{
    Console.Error.WriteLine($"Query file was not found: {queryPath}");
    return 3;
}

try
{
    object output;

    if (Path.GetExtension(queryPath).Equals(".yml", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(queryPath).Equals(".yaml", StringComparison.OrdinalIgnoreCase))
    {
        var resolved = new QueryWattConfigurationLoader().Load(queryPath);
        var configuredConnectionString = Environment.GetEnvironmentVariable(
            resolved.ConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            Console.Error.WriteLine(
                $"Set {resolved.ConnectionStringEnvironmentVariable} before running the probe.");
            return 3;
        }

        var runner = new SqlServerMeasurementRunner(configuredConnectionString);
        output = await new MeasurementSessionRunner(runner)
            .MeasureAsync(resolved.Requests)
            .ConfigureAwait(false);
    }
    else
    {
        var connectionString = Environment.GetEnvironmentVariable("QUERYWATT_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Set QUERYWATT_CONNECTION_STRING before running the probe.");
            return 3;
        }

        var runner = new SqlServerMeasurementRunner(connectionString);
        var queryText = await File.ReadAllTextAsync(queryPath).ConfigureAwait(false);
        var request = new MeasurementRequest(
            Path.GetFileNameWithoutExtension(queryPath),
            queryText,
            WarmupRuns: 3,
            MeasuredRuns: 20,
            CommandTimeoutSeconds: 60);

        var sample = await runner.MeasureAsync(request).ConfigureAwait(false);
        output = new MeasurementSessionResult(
            [sample],
            [MeasurementStatistics.Summarize(sample)]);
    }

    var options = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    Console.WriteLine(JsonSerializer.Serialize(output, options));
    return 0;
}
catch (ConfigurationException exception)
{
    Console.Error.WriteLine($"Configuration failed: {exception.Message}");
    return 3;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Measurement failed: {exception.Message}");
    return 2;
}
