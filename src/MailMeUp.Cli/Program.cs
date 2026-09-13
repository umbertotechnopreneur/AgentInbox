using MailMeUp.Cli;
using MailMeUp.Hosting;
using MailMeUp.Mcp;
using MailMeUp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

CliOptions options;
try
{
    options = CliOptions.Parse(args);
}
catch (CliUsageException exception)
{
    new CliPresentation(new(CliCommand.Help, NoColor: args.Contains("--no-color")))
        .WriteError(exception.Message);
    return 2;
}

var presentation = new CliPresentation(options);
if (options.Command == CliCommand.Help)
{
    presentation.WriteHelp();
    return 0;
}

if (options.Command == CliCommand.Version)
{
    Console.Out.WriteLine(CliPresentation.Version);
    return 0;
}

if (options.Command == CliCommand.Ui)
{
    try
    {
        presentation.WriteBanner();
        presentation.WriteResult(options.UiListSteps ? new UiStepsOutput(UiLauncher.Steps) : UiLauncher.Launch(options));
        return 0;
    }
    catch (UiLaunchException exception)
    {
        presentation.WriteError(exception.Message);
        return 1;
    }
}

var dataDirectory = DataDirectory.Resolve(Environment.GetEnvironmentVariable("MAILMEUP_DATA_DIR"));
using var diagnostics = CliLogging.Create(options, dataDirectory);
var startupLogger = diagnostics.ForContext("SourceContext", "MailMeUp.Cli");
using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler onCancel = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += onCancel;
try
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
    builder.Logging.ClearProviders();
    builder.Services.AddSerilog(diagnostics, dispose: false);
    builder.Services.AddMailMeUp(dataDirectory);
    builder.Services.AddSingleton(presentation);
    builder.Services.AddTransient<CliRunner>();

    if (options.Command == CliCommand.Stdio)
    {
        builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<MailTools>();
    }

    using var host = builder.Build();
    if (options.Command == CliCommand.Stdio)
    {
        startupLogger.Information("Starting read-only MCP bridge on stdio");
        await host.RunAsync(cancellation.Token);
        startupLogger.Information("MCP bridge stopped");
    }
    else
    {
        await host.Services.GetRequiredService<CliRunner>().RunAsync(options, cancellation.Token);
    }

    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    startupLogger.Information("Operation cancelled");
    if (options.Command != CliCommand.Stdio)
    {
        presentation.WriteCancelled();
    }

    return 130;
}
catch (Exception exception)
{
    // Keep even unexpected provider/transport failures from leaking exception messages or inner exceptions.
    startupLogger.Debug("Command {Command} failed ({ErrorType})", options.Command, exception.GetType().Name);
    presentation.WriteError($"Could not complete the operation ({exception.GetType().Name}).", options.Command switch
    {
        CliCommand.Connect => "Run mailmeup setup status, then check provider consent and retry sign-in.",
        CliCommand.Setup => "Check the app registration input, file access and operating-system credential storage.",
        CliCommand.Remove => "Run mailmeup accounts list and use the exact local account ID.",
        _ => "Review the local setup and MAILMEUP_DATA_DIR, then try again."
    });
    return 1;
}
finally
{
    Console.CancelKeyPress -= onCancel;
}
