using System.ComponentModel;
using QueryWatt.Core.Instrumentation;
using QueryWatt.Reporting;
using Spectre.Console.Cli;

namespace QueryWatt.Cli;

/// <summary>Settings for <c>querywatt observe</c>.</summary>
public sealed class ObserveSettings : CommandSettings
{
    /// <summary>The measurement file an application run wrote.</summary>
    [CommandArgument(0, "[FILE]")]
    [Description("Measurement file written by JsonLinesMeasurementSink, for example querywatt-run.jsonl.")]
    public string InputPath { get; init; } = "querywatt-run.jsonl";

    /// <summary>Report format written to standard output.</summary>
    [CommandOption("-f|--format <FORMAT>")]
    [Description("Report format written to standard output: console, json, or markdown.")]
    [DefaultValue("console")]
    public string Format { get; init; } = "console";

    /// <summary>Also write the report to files.</summary>
    [CommandOption("-o|--output <FORMAT=PATH>")]
    [Description(
        "Also write the report to a file, for example markdown=artifacts/observation.md. "
        + "Repeatable.")]
    public string[] Outputs { get; init; } = [];

    /// <summary>How many repetitions of one command in a scope count as a finding.</summary>
    [CommandOption("--repeat-threshold <COUNT>")]
    [Description("How many executions of the same command in one scope are reported as a repetition.")]
    [DefaultValue(ObservationReportFactory.DefaultRepetitionThreshold)]
    public int RepetitionThreshold { get; init; } = ObservationReportFactory.DefaultRepetitionThreshold;
}

/// <summary>
/// Reports what an application run did to the database. Observe mode compares nothing and fails
/// nothing: it always exits 0 unless the file itself could not be read.
/// </summary>
public sealed class ObserveCommand : Command<ObserveSettings>
{
    /// <inheritdoc />
    protected override int Execute(
        CommandContext context,
        ObserveSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var path = Path.GetFullPath(settings.InputPath);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine(
                    $"No measurement file at {path}. Point an application run at a "
                    + "JsonLinesMeasurementSink, then pass that file here.");
                return 3;
            }

            var format = ReportRenderer.ParseFormat(settings.Format);
            var outputs = ReportOutputSpec.ParseAll(settings.Outputs);

            var records = MeasurementRecordFile.Read(path);
            var report = ObservationReportFactory.Create(records, settings.RepetitionThreshold);

            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine(ObservationReportRenderer.Render(report, format));

            foreach (var output in outputs)
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(output.Path));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    output.Path,
                    ObservationReportRenderer.Render(report, output.Format));
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Observation cancelled.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Observation failed: {exception.Message}");
            return 3;
        }
    }
}
