using System.Text.Json;
using QueryWatt.Core;
using QueryWatt.SqlServer;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project samples/QueryWatt.MeasurementProbe -- <query.sql>");
    return 3;
}

var connectionString = Environment.GetEnvironmentVariable("QUERYWATT_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set QUERYWATT_CONNECTION_STRING before running the probe.");
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
    var queryText = await File.ReadAllTextAsync(queryPath).ConfigureAwait(false);
    var request = new MeasurementRequest(
        Path.GetFileNameWithoutExtension(queryPath),
        queryText,
        WarmupRuns: 3,
        MeasuredRuns: 20,
        CommandTimeoutSeconds: 60);

    var runner = new SqlServerMeasurementRunner(connectionString);
    var sample = await runner.MeasureAsync(request).ConfigureAwait(false);

    var options = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    Console.WriteLine(JsonSerializer.Serialize(sample, options));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Measurement failed: {exception.Message}");
    return 2;
}
