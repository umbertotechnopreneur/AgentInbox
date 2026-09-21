using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MailMeUp.Application;
using MailMeUp.Core;
using Spectre.Console;

namespace MailMeUp.Cli;

internal sealed record AccountListOutput(IReadOnlyList<Account> Accounts);
internal sealed record ProviderListOutput(IReadOnlyList<ProviderSetupStatus> Providers);

internal sealed class CliPresentation
{
    private const string Mint = "#71DEB7";
    private const string Coral = "#FF8F79";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly CliOptions _options;
    private readonly IAnsiConsole _output;
    private readonly IAnsiConsole _error;

    internal CliPresentation(CliOptions options)
    {
        _options = options;
        _output = CreateConsole(Console.Out, Console.IsOutputRedirected, options.NoColor);
        _error = CreateConsole(Console.Error, Console.IsErrorRedirected, options.NoColor || options.Command == CliCommand.Stdio);
    }

    internal static string Version => typeof(CliPresentation).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

    private bool Json => _options.Json || Console.IsOutputRedirected;

    internal void WriteBanner()
    {
        if (Json || _options.Command == CliCommand.Stdio)
        {
            return;
        }

        _output.WriteLine();
        _output.MarkupLine($"[bold {Mint}]{Icon("📬", "::")} AgentInbox[/]  [dim]v{Markup.Escape(Version)}[/]");
        _output.MarkupLine("[dim]   All your inboxes. One conversation.[/]");
        _output.MarkupLine("[dim]   [link=https://github.com/umbertotechnopreneur/AgentInbox]GitHub repository[/][/]");
        _output.MarkupLine("[dim]   by Umberto Giacobbi · [link=https://umbertogiacobbi.biz]umbertogiacobbi.biz[/][/]");
        _output.WriteLine();
    }

    internal void WriteHelp()
    {
        WriteBanner();
        Section("Your local email and calendar bridge", "✨");
        _output.MarkupLine("Read-only access to Google and Microsoft accounts from your AI assistant.");
        _output.WriteLine();
        Section("Get started", "🚀");
        Command("agentinbox ui", "Open or activate the native Windows setup app, keeping its current page");
        Command("agentinbox setup google <client-json>", "Import your Google Desktop app registration");
        Command("agentinbox setup microsoft <client-id>", "Save your Microsoft app registration");
        Command("agentinbox accounts connect <google|microsoft>", "Sign in with read-only mail and calendar access");
        Command("agentinbox --stdio", "Start the MCP bridge for your assistant");
        _output.WriteLine();
        Section("Manage your setup", "⚙️");
        Command("agentinbox status", "Show the capabilities implemented in this build");
        Command("agentinbox setup status", "See which providers are ready for sign-in");
        Command("agentinbox accounts list", "Show connected accounts and their read access");
        Command("agentinbox accounts remove <account-id>", "Remove local account metadata and credentials");
        Command("agentinbox accounts connect <provider> --mail-only", "Request mail read access only");
        Command("agentinbox accounts connect <provider> --calendar-only", "Request calendar read access only");
        _output.WriteLine();
        Section("Explore the setup screens", "🪟");
        Command("agentinbox ui --list-steps", "List every screen without starting the desktop app");
        Command("agentinbox ui --step <welcome|accounts|sharing|codex>", "Open a specific setup screen on Windows");
        Command("agentinbox ui --step sharing --demo", "Preview a screen with sample data and no account access");
        Command("--desktop-path <AgentInbox.Desktop.exe>", "Use a built desktop executable with agentinbox ui");
        _output.WriteLine();
        Section("Make it yours", "🎛️");
        Command("--json", "Return JSON; automatic when stdout is redirected");
        Command("--no-color", "Disable colors and terminal escape sequences");
        Command("--no-animation", "Disable activity spinners");
        Command("--log-level <level>", "verbose, debug, information, warning (default), error, fatal");
        Command("--help / -h", "Show this help");
        Command("--version", "Print the application version");
        _output.WriteLine();
        _output.MarkupLine("[dim]AGENTINBOX_DATA_DIR selects an absolute local data directory.[/]");
        _output.MarkupLine("[dim]AGENTINBOX_LOG_LEVEL sets the default log level. NO_COLOR disables colors.[/]");
        _output.MarkupLine("[dim]Mail and calendar reads are available through MCP. Logs go to stderr.[/]");
        _output.WriteLine();
    }

    internal async Task<T> RunWithStatusAsync<T>(string message, Func<Task<T>> action)
    {
        if (Json)
        {
            return await action();
        }

        if (!_options.NoAnimation && !_options.NoColor &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")) &&
            Environment.GetEnvironmentVariable("TERM") != "dumb" &&
            !Console.IsInputRedirected && _output.Profile.Capabilities.Interactive)
        {
            return await _output.Status()
                .Spinner(_output.Profile.Capabilities.Unicode ? Spinner.Known.Dots : Spinner.Known.Line)
                .SpinnerStyle(new Style(new Color(113, 222, 183)))
                .StartAsync(message, _ => action());
        }

        _output.MarkupLine($"[dim]{Markup.Escape(message)}[/]");
        return await action();
    }

    internal void WriteResult(object result)
    {
        if (Json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        switch (result)
        {
            case UiStepsOutput uiSteps:
                Section("Desktop setup screens", "🪟");
                foreach (var step in uiSteps.Steps)
                {
                    Command(step.Command, $"{step.Title}: {step.Description}");
                }

                _output.WriteLine();
                _output.MarkupLine("[dim]Add --demo to preview with sample data. No account access or configuration changes occur in demo mode.[/]");
                break;
            case UiLaunchOutput uiLaunch:
                Section("Desktop launch requested", "🪟");
                Field("Screen", uiLaunch.RequestedStep ?? "Keep current page; Welcome for a new window");
                Field("Mode", uiLaunch.Demo ? "Demo with sample data" : "Your local setup");
                _output.WriteLine();
                _output.MarkupLine("[dim]The desktop app will open or activate its existing window.[/]");
                break;
            case ApplicationStatus status:
                Section("Bridge status", "🔎");
                Field("Stage", status.Stage.Replace('_', ' '));
                Field("Transport", status.Transport);
                Field("Access", status.ReadOnly ? "Read-only" : "Read/write");
                _output.WriteLine();
                foreach (var provider in status.Providers)
                {
                    _output.MarkupLine($"[bold]{Safe(provider.DisplayName)}[/]");
                    Capability("Sign-in", provider.AuthenticationAvailable);
                    Capability("Mail", provider.MailReadAvailable);
                    Capability("Calendars", provider.CalendarReadAvailable);
                    _output.WriteLine();
                }

                _output.MarkupLine("[dim]Capabilities describe this build. Provider setup and account access are shown separately.[/]");
                Next("Check provider setup", "agentinbox setup status");
                break;
            case AccountListOutput accounts:
                Section("Connected accounts", "👤");
                if (accounts.Accounts.Count == 0)
                {
                    _output.MarkupLine("No accounts connected yet.");
                    Next("Check provider setup", "agentinbox setup status");
                }
                else
                {
                    for (var index = 0; index < accounts.Accounts.Count; index++)
                    {
                        if (index > 0)
                        {
                            _output.WriteLine();
                        }

                        WriteAccount(accounts.Accounts[index]);
                    }

                    _output.WriteLine();
                    _output.MarkupLine($"[dim]{accounts.Accounts.Count} connected account(s). Account IDs can be used with accounts remove.[/]");
                    Next("Start the bridge from your MCP client", "agentinbox --stdio");
                }

                break;
            case AccountConnectionResult connection:
                Section("Account connected", "✅");
                WriteAccount(connection.Account);
                Next("See all connected accounts", "agentinbox accounts list");
                break;
            case AccountRemovalResult removal:
                Section(removal.Removed ? "Account removed" : "Account not found", removal.Removed ? "✅" : "ℹ️");
                _output.WriteLine(removal.Removed
                    ? "Local account metadata and cached credentials were removed."
                    : "No matching local account was found. Nothing was removed.");
                if (removal.Removed)
                {
                    _output.MarkupLine("[dim]Provider consent is unchanged; manage it in your Google or Microsoft account.[/]");
                }

                Next("See connected accounts and IDs", "agentinbox accounts list");
                break;
            case ProviderListOutput providers:
                Section("Provider setup", "⚙️");
                foreach (var provider in providers.Providers)
                {
                    WriteProvider(provider);
                    _output.WriteLine();
                    SetupNext(provider);
                    _output.WriteLine();
                }

                _output.MarkupLine("[dim]App registration and account sign-in are separate steps.[/]");
                break;
            case ProviderSetupResult setup:
                Section(setup.Status.Configured ? "Provider configured" : "Provider setup incomplete", setup.Status.Configured ? "✅" : "⚠️");
                WriteProvider(setup.Status);
                if (setup.SourceRetained)
                {
                    _output.WriteLine();
                    _output.MarkupLine($"[{Coral}]Your source client file is still on disk. Keep it private and remove it when no longer needed.[/]");
                }

                _output.WriteLine();
                SetupNext(setup.Status);
                break;
            default:
                throw new InvalidOperationException("No presentation is defined for this command result.");
        }

        _output.WriteLine();
    }

    internal void WriteError(string message, string? nextStep = null)
    {
        var symbol = _error.Profile.Capabilities.Unicode ? "❌" : "Error:";
        _error.MarkupLine($"[bold {Coral}]{symbol} AgentInbox[/]  {Safe(message)}");
        if (nextStep is not null)
        {
            _error.MarkupLine($"[dim]{Safe(nextStep)}[/]");
        }
    }

    internal void WriteCancelled() => _error.MarkupLine("[yellow]AgentInbox: operation cancelled.[/]");

    private void WriteAccount(Account account)
    {
        _output.MarkupLine($"[bold]{Safe(account.DisplayName)}[/]  [dim]({Safe(ProviderName(account.Provider))})[/]");
        Field("Email", account.EmailAddress);
        var access = (account.MailReadEnabled, account.CalendarReadEnabled) switch
        {
            (true, true) => "Mail + calendars (read-only)",
            (true, false) => "Mail (read-only)",
            (false, true) => "Calendars (read-only)",
            _ => "No read categories recorded; reconnect this account"
        };
        Field("Access", access);
        Field("Account ID", account.Id);
    }

    private void WriteProvider(ProviderSetupStatus provider)
    {
        var state = provider.Configured ? $"[{Mint}]Ready for sign-in[/]" : $"[{Coral}]Setup needed[/]";
        _output.MarkupLine($"[bold]{Safe(ProviderName(provider.ProviderId))}[/]  {state}");
        if (!string.IsNullOrEmpty(provider.ClientIdHint))
        {
            Field("Client ID", provider.ClientIdHint);
        }

        if (provider.ProviderId == "google")
        {
            Field("Credential", provider.ProtectedSecretConfigured ? "Protected by your operating system" : "Not configured");
        }
    }

    private void SetupNext(ProviderSetupStatus provider)
    {
        // Only construct executable suggestions for known providers, never from provider-controlled text.
        var command = provider.ProviderId switch
        {
            "google" => provider.Configured ? "agentinbox accounts connect google" : "agentinbox setup google <client-json>",
            "microsoft" => provider.Configured ? "agentinbox accounts connect microsoft" : "agentinbox setup microsoft <client-id>",
            _ => "agentinbox setup status"
        };
        Command(command, provider.Configured ? "Next: connect an account" : "Next: register your app");
    }

    private void Section(string title, string emoji)
    {
        var rule = new Rule($"[bold {Mint}]{Icon(emoji, "")} {Markup.Escape(title)}[/]")
        {
            Justification = Justify.Left,
            Style = new Style(new Color(110, 135, 158))
        };
        _output.Write(rule);
        _output.WriteLine();
    }

    private void Field(string label, string value) => _output.MarkupLine($"  [dim]{label,-11}[/] {Safe(value)}");

    private void Capability(string label, bool available) => _output.MarkupLine(
        $"  [dim]{label,-11}[/] {(available ? $"[{Mint}]Available[/]" : "[dim]Not implemented[/]")}");

    private void Command(string command, string description)
    {
        _output.MarkupLine($"  [{Mint}]{Markup.Escape(command)}[/]");
        _output.MarkupLine($"    [dim]{Markup.Escape(description)}[/]");
    }

    private void Next(string description, string command)
    {
        _output.WriteLine();
        Command(command, $"Next: {description.ToLowerInvariant()}");
    }

    private string Icon(string emoji, string fallback) => _output.Profile.Capabilities.Unicode ? emoji : fallback;

    private static string ProviderName(string id) => id switch
    {
        "google" => "Google",
        "microsoft" => "Microsoft",
        _ => id
    };

    private static string Safe(string value)
    {
        // Provider metadata must not inject markup, newlines, terminal commands or bidi overrides.
        var clean = string.Concat(value.Select(character => char.GetUnicodeCategory(character) is
            UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                ? ' '
                : character));
        return Markup.Escape(clean);
    }

    private static IAnsiConsole CreateConsole(TextWriter writer, bool redirected, bool noColor)
    {
        var plain = redirected || noColor || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")) ||
            Environment.GetEnvironmentVariable("TERM") == "dumb";
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = plain ? AnsiSupport.No : AnsiSupport.Detect,
            ColorSystem = plain ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
            Interactive = redirected || Console.IsInputRedirected ? InteractionSupport.No : InteractionSupport.Detect,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = !plain }
        });
    }
}
