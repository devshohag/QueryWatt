using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using QueryWatt.Baselines;
using QueryWatt.Baselines.InApp;
using QueryWatt.Core.Instrumentation;
using QueryWatt.Reporting;
using Spectre.Console.Cli;

namespace QueryWatt.Cli;

/// <summary>Settings for <c>querywatt report</c>.</summary>
public sealed class ReportSettings : CommandSettings
{
    /// <summary>The measurement file an application run wrote.</summary>
    [CommandArgument(0, "[FILE]")]
    [Description("Measurement file written by JsonLinesMeasurementSink, for example querywatt-run.jsonl.")]
    public string InputPath { get; init; } = "querywatt-run.jsonl";

    /// <summary>Where the accepted baseline lives.</summary>
    [CommandOption("-b|--baseline <PATH>")]
    [Description("Baseline to compare with. When it is missing, the report is observation only.")]
    [DefaultValue(InAppBaselineContract.DefaultFileName)]
    public string BaselinePath { get; init; } = InAppBaselineContract.DefaultFileName;

    /// <summary>Where the page is written.</summary>
    [CommandOption("-o|--out <PATH>")]
    [Description("Where to write the HTML file.")]
    [DefaultValue("querywatt-report.html")]
    public string OutputPath { get; init; } = "querywatt-report.html";

    /// <summary>Open the page when it has been written.</summary>
    [CommandOption("--open")]
    [Description("Open the report in the default browser once it is written.")]
    public bool Open { get; init; }

    /// <summary>How many repetitions of one command in a scope count as a finding.</summary>
    [CommandOption("--repeat-threshold <COUNT>")]
    [Description("How many executions of the same command in one scope are reported as a repetition.")]
    [DefaultValue(ObservationReportFactory.DefaultRepetitionThreshold)]
    public int RepetitionThreshold { get; init; } = ObservationReportFactory.DefaultRepetitionThreshold;
}

/// <summary>
/// Writes a self-contained HTML report for a run. This is the surface for teams who never see a
/// pull request: a file they can open, keep, and send to somebody.
/// </summary>
public sealed class ReportCommand : Command<ReportSettings>
{
    /// <inheritdoc />
    protected override int Execute(
        CommandContext context,
        ReportSettings settings,
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

            var records = MeasurementRecordFile.Read(inputPath);
            var observation = ObservationReportFactory.Create(records, settings.RepetitionThreshold);

            cancellationToken.ThrowIfCancellationRequested();

            InAppVerificationResult? verification = null;
            var baseline = InAppBaselineStore.Read(settings.BaselinePath);
            if (baseline is not null)
            {
                verification = VerdictMatrix.Compare(baseline, InAppBaselineFactory.Summarise(records));
            }

            var model = HtmlReportModelFactory.Create(
                observation,
                verification,
                BaselineContract.ToolVersion);

            var outputPath = Path.GetFullPath(settings.OutputPath);
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, HtmlReportRenderer.Render(model));

            Console.WriteLine($"QueryWatt report written to {outputPath}");

            if (baseline is null)
            {
                Console.WriteLine(
                    $"No baseline at {Path.GetFullPath(settings.BaselinePath)}, so the report shows "
                    + "what the run did rather than what changed.");
            }

            if (settings.Open)
            {
                OpenInBrowser(outputPath);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Report cancelled.");
            return 2;
        }
        catch (InAppBaselineException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Report failed: {exception.Message}");
            return 3;
        }
    }

    private static void OpenInBrowser(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", path).Dispose();
            }
            else
            {
                Process.Start("xdg-open", path).Dispose();
            }
        }
        catch (Exception exception)
        {
            // Failing to open a browser must never fail the command: the file is already written.
            Console.Error.WriteLine($"The report was written but could not be opened: {exception.Message}");
        }
    }
}
