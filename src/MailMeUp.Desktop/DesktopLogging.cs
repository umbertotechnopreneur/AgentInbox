using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MailMeUp.Desktop;

internal static class DesktopLogging
{
    internal static Logger Create(string dataDirectory)
    {
        var logDirectory = Path.Combine(dataDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .Enrich.WithProperty("InstanceId", Guid.NewGuid().ToString("N"))
            .Enrich.WithProperty("DiagnosticVersion", 2)
            .Filter.ByIncludingOnly(logEvent =>
                logEvent.Properties.TryGetValue("SourceContext", out var source) &&
                source is ScalarValue { Value: string name } &&
                (name.StartsWith("AgentInbox.", StringComparison.Ordinal)
                    || name.StartsWith("MailMeUp.", StringComparison.Ordinal)))
            .WriteTo.File(
                Path.Combine(logDirectory, "agentinbox-.log"),
                restrictedToMinimumLevel: LogEventLevel.Debug,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1),
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] diag={DiagnosticVersion} pid={ProcessId} instance={InstanceId} op={OperationId} read={ReadId} provider={Provider} account={AccountKey} {SourceContext}: {Message:lj}{NewLine}")
            .CreateLogger();
    }
}
