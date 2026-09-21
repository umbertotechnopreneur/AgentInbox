namespace MailMeUp.Desktop.Services;

/// <summary>Shows the local paths and commands before Codex configuration changes.</summary>
/// <param name="StableExecutablePath">The installed Windows app execution alias or the portable folder's CLI executable.</param>
/// <param name="PluginDirectory">The writable local marketplace root.</param>
/// <param name="ManualCommands">PowerShell commands for installing the prepared plugin.</param>
/// <param name="ManualMcpCommand">An alternative direct MCP registration, separate from the plugin route.</param>
public sealed record CodexSetupPreview(
    string StableExecutablePath,
    string PluginDirectory,
    string ManualCommands,
    string ManualMcpCommand);

/// <summary>Reports observed local configuration without claiming a successful MCP connection.</summary>
/// <param name="Code">A stable machine-readable state identifier.</param>
/// <param name="Message">The observed state and any required next step.</param>
/// <param name="CanInstall">Whether the guided installer can safely proceed.</param>
/// <param name="IsPluginConfigured">Whether Codex reports the AgentInbox plugin installed and enabled.</param>
/// <param name="HasDirectRegistration">Whether a direct AgentInbox MCP registration was observed.</param>
public sealed record CodexSetupStatus(
    string Code,
    string Message,
    bool CanInstall,
    bool IsPluginConfigured,
    bool HasDirectRegistration)
{
    /// <summary>Gets the number of matching direct entries, or null when their configuration could not be inspected.</summary>
    public int? DirectRegistrationCount { get; init; }

    /// <summary>Gets the explicit enabled flag for one matching direct entry; null means no single known enabled state.</summary>
    public bool? IsDirectRegistrationEnabled { get; init; }

    /// <summary>Gets safe, individual observations; an unperformed check is never reported as absent or successful.</summary>
    public IReadOnlyList<CodexSetupCheck> Checks { get; init; } = [];
}

/// <summary>Describes one local configuration check without exposing command output or credentials.</summary>
/// <param name="Name">The component being inspected.</param>
/// <param name="Result">A safe description of the observed result or why it is unknown.</param>
public sealed record CodexSetupCheck(string Name, string Result)
{
    internal static IReadOnlyList<CodexSetupCheck> Pending() =>
    [
        new("AgentInbox command", "Not checked"),
        new("Codex CLI", "Not checked"),
        new("Local marketplace", "Not checked"),
        new("AgentInbox plugin", "Not checked"),
        new("Direct MCP connection", "Not checked")
    ];
}

/// <summary>Reports the outcome of an explicit guided installation request.</summary>
/// <param name="Success">Whether Codex reports the plugin installed and enabled after installation.</param>
/// <param name="Message">The outcome and next step, including partial installation when relevant.</param>
/// <param name="Status">The last observed configuration state.</param>
public sealed record CodexSetupResult(bool Success, string Message, CodexSetupStatus Status);
