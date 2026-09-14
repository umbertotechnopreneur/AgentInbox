using System.ComponentModel;
using System.Diagnostics;

namespace MailMeUp.Cli;

internal sealed record UiStep(string Id, string Title, string Description)
{
    public string Command => $"mailmeup ui --step {Id}";
}
internal sealed record UiStepsOutput(IReadOnlyList<UiStep> Steps);
internal sealed record UiLaunchOutput(string Status, string? RequestedStep, bool Demo);

internal static class UiLauncher
{
    internal static IReadOnlyList<UiStep> Steps { get; } = Array.AsReadOnly<UiStep>(
    [
        new("welcome", "Welcome", "Discover how MailMeUp connects your inboxes to your assistant."),
        new("accounts", "Accounts", "Connect Google and Microsoft accounts and review their read access."),
        new("sharing", "Sharing", "Choose participating accounts, calendars and the default mail search period."),
        new("codex", "Codex", "Set up the local Codex connection and review its status.")
    ]);

    internal static bool IsKnownStep(string step) => Steps.Any(item => item.Id == step);

    internal static UiLaunchOutput Launch(CliOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new UiLaunchException("The desktop setup app is available only on Windows. Use mailmeup ui --list-steps to list its screens.");
        }

        try
        {
            var startInfo = CreateStartInfo(AppContext.BaseDirectory, options.DesktopPath, options.UiStep, options.UiDemo);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new UiLaunchException("Windows did not start the desktop setup app. Check the installation and try again.");
            }

            // The request may activate an existing desktop instance; it does not confirm that a window has rendered.
            return new("launch_requested", options.UiStep, options.UiDemo);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            throw new UiLaunchException("Could not start the desktop setup app. Check its executable path and installation, then try again.");
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string cliDirectory,
        string? desktopPath,
        string? step,
        bool demo,
        Func<string, bool>? fileExists = null)
    {
        if (step is not null && !IsKnownStep(step))
        {
            throw new CliUsageException("Unknown UI step. Use welcome, accounts, sharing or codex.");
        }

        var executable = ResolveDesktopPath(cliDirectory, desktopPath, fileExists ?? File.Exists);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        };
        if (step is not null)
        {
            startInfo.ArgumentList.Add("--step");
            startInfo.ArgumentList.Add(step);
        }
        if (demo)
        {
            startInfo.ArgumentList.Add("--demo");
        }

        return startInfo;
    }

    private static string ResolveDesktopPath(string cliDirectory, string? desktopPath, Func<string, bool> fileExists)
    {
        if (desktopPath is not null)
        {
            var explicitPath = Path.GetFullPath(desktopPath);
            if (!string.Equals(Path.GetExtension(explicitPath), ".exe", StringComparison.OrdinalIgnoreCase) || !fileExists(explicitPath))
            {
                throw new UiLaunchException("--desktop-path must point to an existing desktop .exe file.");
            }

            return explicitPath;
        }

        var packagedPath = Path.GetFullPath(Path.Combine(cliDirectory, "..", "MailMeUp.Desktop.exe"));
        if (fileExists(packagedPath))
        {
            return packagedPath;
        }

        var adjacentPath = Path.GetFullPath(Path.Combine(cliDirectory, "MailMeUp.Desktop.exe"));
        if (fileExists(adjacentPath))
        {
            return adjacentPath;
        }

        throw new UiLaunchException("Desktop setup app not found. Install the Windows package or use --desktop-path <MailMeUp.Desktop.exe> with a built desktop executable.");
    }
}

internal sealed class UiLaunchException(string message) : Exception(message)
{
}
