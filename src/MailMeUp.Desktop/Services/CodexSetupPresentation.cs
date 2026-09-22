namespace MailMeUp.Desktop.Services;

// Kept independent of WinUI so state-specific guidance can use synthetic regression tests.
internal sealed record CodexSetupPresentation(string Title, string HelpLabel, IReadOnlyList<string> Steps)
{
    internal static bool IsLocalSetupReady(CodexSetupStatus? status) => status is
    { Code: "PluginConfigured", IsPluginConfigured: true, HasDirectRegistration: false }
        or
    {
        Code: "DirectConfigured", HasDirectRegistration: true, DirectRegistrationCount: 1,
        IsDirectRegistrationEnabled: true, IsPluginConfigured: false
    };

    internal static string ConnectionInstructions(CodexSetupStatus status, bool keepPlugin)
    {
        if (keepPlugin)
        {
            var entries = status.DirectRegistrationCount > 1 ? "the direct AgentInbox entries" : "the direct AgentInbox entry";
            return $"1. Open Codex Settings > MCP servers and remove {entries}."
                + Environment.NewLine
                + (status.IsPluginConfigured
                    ? "2. In Codex Settings > Plugins, keep AgentInbox enabled."
                    : "2. Return here to install the AgentInbox plugin after the direct entry is removed.");
        }

        var next = status.DirectRegistrationCount > 1
            ? "In Codex Settings > MCP servers, keep one AgentInbox entry and make sure it is enabled."
            : status.IsDirectRegistrationEnabled == false
                ? "In Codex Settings > MCP servers, enable the AgentInbox entry."
                : status.IsDirectRegistrationEnabled is null
                    ? "In Codex Settings > MCP servers, review the AgentInbox entry and make sure it is enabled."
                    : "Keep your direct AgentInbox entry enabled in Codex Settings > MCP servers.";
        return "1. In Codex Settings > Plugins, disable any enabled AgentInbox plugin."
            + Environment.NewLine + "2. " + next;
    }

    internal static CodexSetupPresentation FromStatus(CodexSetupStatus status) => status.Code switch
    {
        "ReadyToInstall" => new("Ready to install", "What happens next?",
            ["Select Install plugin to add AgentInbox to Codex. Your saved sharing choices are kept.",
             "After installation, start a new Codex task to try the connection."]),
        "PluginConfigured" => new("Ready to try in Codex", "How to use it",
            ["Start a new Codex task, then copy and send the request below.",
             "Once Codex can see your accounts, ask AgentInbox to find an email or show your upcoming appointments.",
             "Your settings are ready. Your first request will check the connection."]),
        "DirectConfigured" => new("Ready to try in Codex", "How to use it",
            ["Your existing AgentInbox connection is enabled in Codex.",
             "Start a new Codex task, then copy and send the request below to check the connection."]),
        "DirectRegistrationExists" => new(
            status.DirectRegistrationCount > 1 ? "Several connection entries found"
                : status.IsPluginConfigured ? "Two connection entries found" : "Direct connection needs review",
            "Review connections",
            ["Open Codex Settings > MCP servers and review the AgentInbox entry. A direct MCP registration is an alternative to the plugin, not a mailbox sign-in error.",
             status.IsPluginConfigured
                 ? "The local plugin is also enabled. Choose one connection method to avoid duplicate tools: keep the direct entry and disable the plugin in Codex, or remove the direct entry and keep the plugin."
                 : "You can keep the direct connection without installing this plugin. To switch to the plugin, remove only the direct AgentInbox entry in Codex first.",
             "Return here and refresh status after any change. AgentInbox does not remove Codex connections automatically; their runtime status is not confirmed here."]),
        "PluginDisabled" => new("Plugin is disabled", "How to enable it",
            ["Open AgentInbox in Codex's plugin settings and enable the local plugin.",
             "Return here and refresh status, then start a new Codex task."]),
        "OtherPluginExists" => new("Another AgentInbox plugin found", "Review connections",
            ["Review the installed AgentInbox plugin in Codex. You can keep that source without adding this local copy.",
             "If you want to switch sources, remove the other copy in Codex first, then return here and refresh status."]),
        "AliasUnavailable" => new("AgentInbox command unavailable", "How to restore it",
            ["For the portable edition, extract the complete ZIP and keep its cli folder beside AgentInbox.Desktop.exe.",
             "For the installed edition, enable agentinbox.exe in Windows Settings > Apps > Advanced app settings > App execution aliases. Return here and refresh status."]),
        "CodexUnavailable" => new("Codex CLI not found", "Setup help",
            ["Automatic setup needs the Codex command-line app (CLI), even if you already use the Codex desktop app.",
             "If you just installed it, restart AgentInbox and select Verify connection. Otherwise, open Need help? and select Manual setup."]),
        "MarketplaceConflict" => new("Local marketplace name in use", "Review next steps",
            ["A different source uses the agentinbox-local marketplace name. Review its source in Codex before changing it.",
             "Keep the existing source if you need it. Only resolve or remove that registration deliberately, then refresh status here."]),
        _ => new("Setup check needs attention", "Review next steps",
            [status.Message,
             "Select Verify connection to retry. If it still does not work, review your Codex settings or open Need help?.",
             "No successful connection is assumed until Codex reports the AgentInbox configuration ready."])
    };
}
