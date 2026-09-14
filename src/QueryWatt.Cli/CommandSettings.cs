using System.ComponentModel;
using Spectre.Console.Cli;

namespace QueryWatt.Cli;

public sealed class InitSettings : CommandSettings
{
    [CommandArgument(0, "[DIRECTORY]")]
    [Description("Directory in which the scaffold will be created.")]
    public string Directory { get; init; } = ".";
}

public sealed class MeasurementCommandSettings : CommandSettings
{
    [CommandArgument(0, "[CONFIG]")]
    [Description("Path to querywatt.yml.")]
    public string ConfigurationPath { get; init; } = "querywatt.yml";

    [CommandOption("-f|--format <FORMAT>")]
    [Description("Report format: console, json, or markdown.")]
    [DefaultValue("console")]
    public string Format { get; init; } = "console";
}
