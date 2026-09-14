using System.Text.Json;
using QueryWatt.Baselines;
using QueryWatt.Configuration;
using QueryWatt.SqlServer;

if (args.Length != 2
    || (args[0] != "baseline" && args[0] != "verify"))
{
    Console.Error.WriteLine(
        "Usage: dotnet run --project samples/QueryWatt.MeasurementProbe -- <baseline|verify> <querywatt.yml>");
    return 3;
}

var commandName = args[0];
var configurationPath = Path.GetFullPath(args[1]);

try
{
    var configuration = new QueryWattConfigurationLoader().Load(configurationPath);
    var connectionString = Environment.GetEnvironmentVariable(
        configuration.ConnectionStringEnvironmentVariable);

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine(
            $"Set {configuration.ConnectionStringEnvironmentVariable} before running the probe.");
        return 3;
    }

    var workflow = new BaselineWorkflow(
        new SqlServerMeasurementRunner(connectionString),
        new SqlServerEnvironmentInspector(connectionString),
        SqlServerMeasurementRunner.PinnedSetOptions);
    var store = new BaselineJsonStore();
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    if (commandName == "baseline")
    {
        var baseline = await workflow.CreateAsync(configuration).ConfigureAwait(false);
        store.Save(configuration.BaselinePath, baseline);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "baseline-created",
            path = configuration.BaselinePath,
            queryCount = baseline.Queries.Count,
            schemaVersion = baseline.SchemaVersion
        }, jsonOptions));
        return 0;
    }

    var storedBaseline = store.Load(configuration.BaselinePath);
    var verification = await workflow
        .VerifyAsync(configuration, storedBaseline)
        .ConfigureAwait(false);
    Console.WriteLine(JsonSerializer.Serialize(verification, jsonOptions));
    return verification.ExitCode;
}
catch (ConfigurationException exception)
{
    Console.Error.WriteLine($"Configuration failed: {exception.Message}");
    return 3;
}
catch (BaselineConfigurationException exception)
{
    Console.Error.WriteLine($"Baseline configuration failed: {exception.Message}");
    return 3;
}
catch (EnvironmentMismatchException exception)
{
    Console.Error.WriteLine($"Measurement refused: {exception.Message}");
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Measurement failed: {exception.Message}");
    return 2;
}
