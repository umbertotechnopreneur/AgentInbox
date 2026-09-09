namespace MailMeUp.Desktop.Services;

// Kept independent of WinUI so state-specific guidance can use synthetic regression tests.
internal sealed record CodexSetupPresentation(string Title, string HelpLabel, IReadOnlyList<string> Steps)
{
    internal static CodexSetupPresentation FromStatus(CodexSetupStatus status) => status.Code switch
    {
        "ReadyToInstall" => new("Ready to install", "What happens next?",
            ["Install the local plugin using the button on this page. This adds MailMeUp's local marketplace and plugin to Codex.",
             "Start a new Codex task after installation. In its composer, use /mcp to review loaded servers."]),
        "PluginConfigured" => new("Plugin installed and enabled", "How to use it",
            ["Start a new Codex task to load the MailMeUp plugin. In its composer, use /mcp to review loaded servers.",
             "Choose MailMeUp when asking about your shared mail or calendars. This setup check has not tested a live connection.",
             "Use Update local plugin only when refreshing the plugin files or configuration."]),
        "DirectRegistrationExists" => new(
            status.IsPluginConfigured ? "Two connection methods found" : "Direct MCP setup found",
            "Review connections",
            ["Open Codex Settings > MCP servers and review the MailMeUp entry. A direct MCP registration is an alternative to the plugin, not a mailbox sign-in error.",
             status.IsPluginConfigured
                 ? "The local plugin is also enabled. Choose one connection method to avoid duplicate tools: keep the direct entry and disable the plugin in Codex, or remove the direct entry and keep the plugin."
                 : "You can keep the direct connection without installing this plugin. To switch to the plugin, remove only the direct MailMeUp entry in Codex first.",
             "Return here and refresh status after any change. MailMeUp does not remove Codex connections automatically; their runtime status is not confirmed here."]),
        "PluginDisabled" => new("Plugin is disabled", "How to enable it",
            ["Open MailMeUp in Codex's plugin settings and enable the local plugin.",
             "Return here and refresh status, then start a new Codex task."]),
        "OtherPluginExists" => new("Another MailMeUp plugin found", "Review connections",
            ["Review the installed MailMeUp plugin in Codex. You can keep that source without adding this local copy.",
             "If you want to switch sources, remove the other copy in Codex first, then return here and refresh status."]),
        "AliasUnavailable" => new("MailMeUp command unavailable", "How to restore it",
            ["Install the MailMeUp MSIX if needed. In Windows Settings > Apps > Advanced app settings > App execution aliases, enable mailmeup.exe.",
             "Return here and refresh status. Do not use an executable path inside a versioned WindowsApps package folder."]),
        "CodexUnavailable" => new("Codex CLI not found", "Setup help",
            ["Automatic setup needs the native Codex CLI. This does not establish whether the Codex desktop app is installed or signed in.",
             "If you already installed the CLI, restart MailMeUp to pick up PATH changes and refresh status. Otherwise, use the manual setup fallback below."]),
        "MarketplaceConflict" => new("Local marketplace name in use", "Review next steps",
            ["A different source uses the mailmeup-local marketplace name. Review its source in Codex before changing it.",
             "Keep the existing source if you need it. Only resolve or remove that registration deliberately, then refresh status here."]),
        _ => new("Setup check needs attention", "Review next steps",
            [status.Message,
             "Refresh status to retry. If a check stays unavailable, review Codex settings or use the manual setup fallback. No successful connection is assumed."])
    };
}
