using System.ComponentModel;
using QueryWatt.Baselines;
using QueryWatt.Baselines.InApp;
using QueryWatt.Core.Instrumentation;
using QueryWatt.Reporting;
using Spectre.Console.Cli;

namespace QueryWatt.Cli;

/// <summary>Settings for <c>querywatt check</c>.</summary>
public sealed class CheckSettings : CommandSettings
{
    /// <summary>The measurement file an application run wrote.</summary>
    [CommandArgument(0, "[FILE]")]
    [Description("Measurement file written by JsonLinesMeasurementSink, for example querywatt-run.jsonl.")]
    public string InputPath { get; init; } = "querywatt-run.jsonl";

    /// <summary>Where the accepted baseline lives.</summary>
    [CommandOption("-b|--baseline <PATH>")]
    [Description("Path to the accepted baseline file.")]
    [DefaultValue(InAppBaselineContract.DefaultFileName)]
    public string BaselinePath { get; init; } = InAppBaselineContract.DefaultFileName;

    /// <summary>Accept this run's numbers instead of judging them.</summary>
    [CommandOption("--accept")]
    [Description(
        "Write this run's numbers into the baseline instead of comparing them. Scenarios already "
        + "in the baseline are replaced; the rest are kept.")]
    public bool Accept { get; init; }

    /// <summary>Report format written to standard output.</summary>
    [CommandOption("-f|--format <FORMAT>")]
    [Description("Report format written to standard output: console, json, or markdown.")]
    [DefaultValue("console")]
    public string Format { get; init; } = "console";

    /// <summary>Also write the report to files.</summary>
    [CommandOption("-o|--output <FORMAT=PATH>")]
    [Description("Also write the report to a file, for example markdown=artifacts/check.md. Repeatable.")]
    public string[] Outputs { get; init; } = [];
}

/// <summary>
/// Compares a run with the accepted baseline, or accepts it. Exits 1 only on a regression: a new
/// scenario, a data change or an incomparable environment are reported, never failed.
/// </summary>
public sealed class CheckCommand : Command<CheckSettings>
{
    /// <inheritdoc />
    protected override int Execute(
        CommandContext context,
        CheckSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var inputPath = Path.GetFullPath(settings.InputPath);
            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine(
                    $"No measurement file at {inputPath}. Point an application run at a "
                    + "JsonLinesMeasurementSink, then pass that file here.");
                return 3;
            }

            var format = ReportRenderer.ParseFormat(settings.Format);
            var outputs = ReportOutputSpec.ParseAll(settings.Outputs);

            var records = MeasurementRecordFile.Read(inputPath);
            var samples = InAppBaselineFactory.Summarise(records);

            cancellationToken.ThrowIfCancellationRequested();

            if (samples.Count == 0)
            {
                Console.Error.WriteLine(
                    "This run has nothing measurable in it. Scopes that failed, were abandoned, or "
                    + "are missing server metrics are never baselined.");
                return 3;
            }

            var existing = InAppBaselineStore.Read(settings.BaselinePath);

            if (settings.Accept)
            {
                var merged = InAppBaselineFactory.Merge(existing, samples, BaselineContract.ToolVersion);
                InAppBaselineStore.Write(settings.BaselinePath, merged);

                Console.WriteLine(
                    $"Accepted {samples.Count} scenarios into {Path.GetFullPath(settings.BaselinePath)}.");
                return 0;
            }

            var result = VerdictMatrix.Compare(existing, samples);

            Console.WriteLine(InAppVerificationRenderer.Render(result, format));

            foreach (var output in outputs)
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(output.Path));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(output.Path, InAppVerificationRenderer.Render(result, output.Format));
            }

            return result.ExitCode;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Check cancelled.");
            return 2;
        }
        catch (InAppBaselineException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Check failed: {exception.Message}");
            return 3;
        }
    }
}
