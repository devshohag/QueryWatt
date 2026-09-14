using System.ComponentModel;
using Spectre.Console.Cli;

namespace QueryWatt.Cli;

public sealed class InitSettings : CommandSettings
{
    [CommandArgument(0, "[DIRECTORY]")]
    [Description("Directory in which the scaffold will be created.")]
    public string Directory { get; init; } = ".";
}

public class MeasurementCommandSettings : CommandSettings
{
    [CommandArgument(0, "[CONFIG]")]
    [Description("Path to querywatt.yml.")]
    public string ConfigurationPath { get; init; } = "querywatt.yml";

    [CommandOption("-f|--format <FORMAT>")]
    [Description("Report format written to standard output: console, json, or markdown.")]
    [DefaultValue("console")]
    public string Format { get; init; } = "console";

    [CommandOption("-o|--output <FORMAT=PATH>")]
    [Description(
        "Also write the report to a file, for example markdown=artifacts/report.md. "
        + "Repeatable: one measurement can produce several report files.")]
    public string[] Outputs { get; init; } = [];
}

public sealed class BaselineCommandSettings : MeasurementCommandSettings;

public sealed class VerifyCommandSettings : MeasurementCommandSettings;
