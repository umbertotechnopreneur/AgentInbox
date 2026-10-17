// SPDX-License-Identifier: MIT

/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.ComponentModel;
using System.Diagnostics;

namespace MailMeUp.Cli;

/// <summary>Describes one navigable desktop setup step exposed by the CLI.</summary>
internal sealed record UiStep(string Id, string Title, string Description)
{
    public string Command => $"agentinbox ui --step {Id}";
}
/// <summary>Defines the structured output for a request to list desktop setup steps.</summary>
internal sealed record UiStepsOutput(IReadOnlyList<UiStep> Steps);

/// <summary>Defines the structured output for a request to launch or activate desktop setup.</summary>
internal sealed record UiLaunchOutput(string Status, string? RequestedStep, bool Demo);

/// <summary>Locates and launches the Windows setup application for supported requested steps.</summary>
internal static class UiLauncher
{
    internal static IReadOnlyList<UiStep> Steps { get; } = Array.AsReadOnly<UiStep>(
    [
        new("welcome", "Welcome", "Discover how AgentInbox connects your inboxes to your assistant."),
        new("accounts", "Accounts", "Connect Google and Microsoft accounts and review their read access."),
        new("sharing", "Sharing", "Choose participating accounts, calendars and the default mail search period."),
        new("codex", "Codex", "Set up the local Codex connection and review its status.")
    ]);

    /// <summary>Determines whether a step identifier is exposed by the desktop setup application.</summary>
    internal static bool IsKnownStep(string step) => Steps.Any(item => item.Id == step);

    /// <summary>Lists known steps or starts the desktop executable with the requested activation arguments.</summary>
    internal static UiLaunchOutput Launch(CliOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new UiLaunchException("The desktop setup app is available only on Windows. Use agentinbox ui --list-steps to list its screens.");
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

    /// <summary>Creates a shell-free desktop process start request for a validated activation step.</summary>
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

    /// <summary>Resolves a supplied or adjacent desktop executable path after ensuring that it exists.</summary>
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

        var packagedPath = Path.GetFullPath(Path.Combine(cliDirectory, "..", "AgentInbox.Desktop.exe"));
        if (fileExists(packagedPath))
        {
            return packagedPath;
        }

        var adjacentPath = Path.GetFullPath(Path.Combine(cliDirectory, "AgentInbox.Desktop.exe"));
        if (fileExists(adjacentPath))
        {
            return adjacentPath;
        }

        throw new UiLaunchException("Desktop setup app not found. Install the Windows package or use --desktop-path <AgentInbox.Desktop.exe> with a built desktop executable.");
    }
}

/// <summary>Represents a safe error encountered while locating or starting the desktop setup application.</summary>
internal sealed class UiLaunchException(string message) : Exception(message)
{
}
