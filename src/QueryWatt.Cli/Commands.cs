using QueryWatt.Baselines;
using QueryWatt.Configuration;
using QueryWatt.Reporting;
using QueryWatt.SqlServer;
using Spectre.Console.Cli;

namespace QueryWatt.Cli;

public sealed class InitCommand : Command<InitSettings>
{
    protected override int Execute(
        CommandContext context,
        InitSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = Path.GetFullPath(settings.Directory);
            var files = ScaffoldFiles.Create(root);
            var existing = files.Keys.Where(File.Exists).ToArray();
            if (existing.Length > 0)
            {
                Console.Error.WriteLine(
                    "Initialization refused; existing files would be overwritten: "
                    + string.Join(", ", existing));
                return 3;
            }

            foreach (var (path, contents) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, contents);
            }

            Console.WriteLine($"QueryWatt scaffold created at {root}");
            Console.WriteLine("Next: run seed/setup.sql, set QUERYWATT_CONNECTION_STRING, then querywatt baseline.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Initialization cancelled.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Initialization failed: {exception.Message}");
            return 3;
        }
    }
}

public sealed class BaselineCommand : AsyncCommand<BaselineCommandSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        BaselineCommandSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var format = ReportRenderer.ParseFormat(settings.Format);
            var outputs = ReportOutputSpec.ParseAll(settings.Outputs);
            var runtime = CliRuntime.Load(settings.ConfigurationPath);
            var baseline = await runtime.Workflow
                .CreateAsync(runtime.Configuration, cancellationToken)
                .ConfigureAwait(false);
            var stored = settings.IncludeRuns ? baseline : BaselineDetail.Strip(baseline);
            runtime.Store.Save(runtime.Configuration.BaselinePath, stored);

            string Render(ReportFormat target) => ReportRenderer.RenderBaselineCreated(
                runtime.Configuration.BaselinePath,
                stored.Queries.Count,
                stored.SchemaVersion,
                settings.IncludeRuns,
                target);

            ReportFiles.WriteAll(outputs, Render, cancellationToken);
            Console.WriteLine(Render(format));
            return 0;
        }
        catch (Exception exception)
        {
            return CliErrors.Write(exception);
        }
    }
}

public sealed class VerifyCommand : AsyncCommand<VerifyCommandSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        VerifyCommandSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var format = ReportRenderer.ParseFormat(settings.Format);
            var outputs = ReportOutputSpec.ParseAll(settings.Outputs);
            var runtime = CliRuntime.Load(settings.ConfigurationPath);
            var baseline = runtime.Store.Load(runtime.Configuration.BaselinePath);
            var verification = await runtime.Workflow
                .VerifyAsync(runtime.Configuration, baseline, cancellationToken)
                .ConfigureAwait(false);
            var report = VerificationReportFactory.Create(
                verification,
                runtime.Configuration);

            ReportFiles.WriteAll(
                outputs,
                target => ReportRenderer.Render(report, target),
                cancellationToken);
            Console.WriteLine(ReportRenderer.Render(report, format));
            return verification.ExitCode;
        }
        catch (Exception exception)
        {
            return CliErrors.Write(exception);
        }
    }
}

internal static class ReportFiles
{
    public static void WriteAll(
        IReadOnlyList<ReportOutputSpec> outputs,
        Func<ReportFormat, string> render,
        CancellationToken cancellationToken)
    {
        foreach (var output in outputs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = Path.GetFullPath(output.Path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, render(output.Format) + Environment.NewLine);
        }
    }
}

internal sealed record CliRuntime(
    ResolvedQueryWattConfiguration Configuration,
    BaselineWorkflow Workflow,
    BaselineJsonStore Store)
{
    public static CliRuntime Load(string configurationPath)
    {
        var configuration = new QueryWattConfigurationLoader().Load(configurationPath);
        var connectionString = Environment.GetEnvironmentVariable(
            configuration.ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ConfigurationException(
                $"Environment variable {configuration.ConnectionStringEnvironmentVariable} is not set.");
        }

        return new CliRuntime(
            configuration,
            new BaselineWorkflow(
                new SqlServerMeasurementRunner(connectionString),
                new SqlServerEnvironmentInspector(connectionString),
                SqlServerMeasurementRunner.PinnedSetOptions),
            new BaselineJsonStore());
    }
}

internal static class CliErrors
{
    public static int Write(Exception exception)
    {
        var (exitCode, category) = exception switch
        {
            ConfigurationException => (3, "Configuration failed"),
            BaselineConfigurationException => (3, "Baseline configuration failed"),
            ArgumentException => (3, "Usage failed"),
            EnvironmentMismatchException => (2, "Measurement refused"),
            OperationCanceledException => (2, "Operation cancelled"),
            _ => (2, "Measurement failed")
        };

        Console.Error.WriteLine($"{category}: {exception.Message}");
        return exitCode;
    }
}
