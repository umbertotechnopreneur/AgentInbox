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
            .Filter.ByIncludingOnly(logEvent =>
                logEvent.Properties.TryGetValue("SourceContext", out var source) &&
                source is ScalarValue { Value: string name } &&
                name.StartsWith("MailMeUp.", StringComparison.Ordinal))
            .WriteTo.File(
                Path.Combine(logDirectory, "mailmeup-.log"),
                restrictedToMinimumLevel: LogEventLevel.Debug,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1),
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}")
            .CreateLogger();
    }
}
