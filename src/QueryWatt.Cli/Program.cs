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
    configuration.SetApplicationVersion("0.5.0-preview.1");
    configuration.AddCommand<InitCommand>("init")
        .WithDescription("Create a safe, runnable QueryWatt example scaffold.");
    configuration.AddCommand<BaselineCommand>("baseline")
        .WithDescription("Measure configured queries and write an approved baseline.");
    configuration.AddCommand<VerifyCommand>("verify")
        .WithDescription("Measure configured queries and compare them with the baseline.");
});

var exitCode = await app.RunAsync(args, cancellationSource.Token).ConfigureAwait(false);
return exitCode < 0 ? 3 : exitCode;
