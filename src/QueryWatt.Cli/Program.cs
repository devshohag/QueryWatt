using QueryWatt.Baselines;
using QueryWatt.Cli;
using Spectre.Console.Cli;

var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

var app = new CommandApp();
app.Configure(configuration =>
{
    configuration.SetApplicationName("querywatt");
    configuration.SetApplicationVersion(BaselineContract.ToolVersion);
    configuration.AddCommand<InitCommand>("init")
        .WithDescription("Create a safe, runnable QueryWatt example scaffold.");
    configuration.AddCommand<BaselineCommand>("baseline")
        .WithDescription("Measure configured queries and write an approved baseline.");
    configuration.AddCommand<ReportCommand>("report")
        .WithDescription("Write a self-contained HTML report for a run.");
    configuration.AddCommand<CheckCommand>("check")
        .WithDescription("Compare a run with the accepted baseline, or accept it with --accept.");
    configuration.AddCommand<ReportCommand>("report")
        .WithDescription("Write a self-contained HTML report for a run.");
    configuration.AddCommand<CheckCommand>("check")
        .WithDescription("Compare a run with the accepted baseline, or accept it with --accept.");
    configuration.AddCommand<ObserveCommand>("observe")
        .WithDescription("Report what an application run did to the database. Compares nothing.");
    configuration.AddCommand<VerifyCommand>("verify")
        .WithDescription("Measure configured queries and compare them with the baseline.");
});

var exitCode = await app.RunAsync(args, cancellationSource.Token).ConfigureAwait(false);
return exitCode < 0 ? 3 : exitCode;