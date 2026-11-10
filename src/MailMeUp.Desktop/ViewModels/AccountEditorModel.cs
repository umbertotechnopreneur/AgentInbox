/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Retained account draft, provider defaults and field validation.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.Collections.ObjectModel;
using System.Globalization;
using MailMeUp.Desktop.Models;
using MailMeUp.Desktop.Services;

namespace MailMeUp.Desktop.ViewModels;

/// <summary>Owns an editable account, provider defaults and validation independently of its view.</summary>
/// <param name="defaultMicrosoftClientId">Public-client ID shipped with the app, never entered as a user credential.</param>
internal sealed class AccountEditorModel(string defaultMicrosoftClientId) : ObservableModel
{
    private readonly string _defaultMicrosoftClientId = defaultMicrosoftClientId;
    private bool _loading;
    private SavedAccount? _saved;
    private IReadOnlyList<AccountFieldError> _errors = [];
    private AccountEditorPolicy _policy = new();

    public string Id { get; private set; } = "";
    public int? Revision { get; private set; }
    public bool HasChanges { get; private set; }
    public bool IsLoading => _loading;
    public string MicrosoftClientId { get; private set; } = defaultMicrosoftClientId;
    public ObservableCollection<string> AuthenticationChoices { get; } = ["Use a password"];
    public ObservableCollection<string> AvailableFolders { get; } = [];
    public ObservableCollection<string> SelectedFolders { get; } = [];
    public IReadOnlyList<AccountFieldError> Errors => _errors;
    public IReadOnlyList<ProviderChoice> Providers { get; private set; } = [];
    internal int MaxFolders => _policy.MaxFolders;
    private string Protocol => ProtocolIndex switch { 0 => "imap", 1 => "pop3", _ => "" };
    private ProviderPreset? Preset => _policy.Presets.GetValueOrDefault(PresetId);

    /// <summary>Load the provider choices and presentation constraints returned by the engine.</summary>
    /// <param name="policy">Current engine catalog, supported Microsoft hosts and folder limit.</param>
    internal void SetPolicy(AccountEditorPolicy policy)
    {
        if (!policy.Presets.ContainsKey("custom") || policy.MaxFolders < 1)
            throw new EngineException("The engine returned an invalid account editor catalog.");
        _policy = policy;
        var providers = policy.Presets.Select(entry => new ProviderChoice(entry.Key,
            entry.Key == "custom" ? "Other mail provider" : entry.Value.Label)).ToArray();
        if (!Providers.SequenceEqual(providers)) Providers = providers;
    }

    private string _presetId = "custom";
    public string PresetId
    {
        get => _presetId;
        set
        {
            // The picker can briefly clear its selection while binding the catalog.
            if (value is not null) Set(ref _presetId, value);
        }
    }

    private string _label = "";
    public string Label { get => _label; set => Set(ref _label, value); }

    private string _email = "";
    public string Email { get => _email; set => Set(ref _email, value); }

    private int _authenticationIndex = 0;
    public int AuthenticationIndex { get => _authenticationIndex; set => Set(ref _authenticationIndex, value); }

    private int _tenantIndex = 0;
    public int TenantIndex { get => _tenantIndex; set => Set(ref _tenantIndex, value); }

    private string _organizationId = "";
    public string OrganizationId { get => _organizationId; set => Set(ref _organizationId, value); }

    private int _protocolIndex = 0;
    public int ProtocolIndex { get => _protocolIndex; set => Set(ref _protocolIndex, value); }

    private string _incomingHost = "";
    public string IncomingHost { get => _incomingHost; set => Set(ref _incomingHost, value); }

    private string _incomingPort = "993";
    public string IncomingPort { get => _incomingPort; set => Set(ref _incomingPort, value); }

    private int _incomingSecurityIndex = 0;
    public int IncomingSecurityIndex { get => _incomingSecurityIndex; set => Set(ref _incomingSecurityIndex, value); }

    private string _incomingEmail = "";
    public string IncomingEmail { get => _incomingEmail; set => Set(ref _incomingEmail, value); }

    private string _incomingPassword = "";
    public string IncomingPassword { get => _incomingPassword; set => Set(ref _incomingPassword, value); }

    private bool _readEnabled = false;
    public bool ReadEnabled { get => _readEnabled; set => Set(ref _readEnabled, value); }

    private bool _configureSending = false;
    public bool ConfigureSending { get => _configureSending; set => Set(ref _configureSending, value); }

    private bool _sendEnabled = false;
    public bool SendEnabled { get => _sendEnabled; set => Set(ref _sendEnabled, value); }

    private string _sendingHost = "";
    public string SendingHost { get => _sendingHost; set => Set(ref _sendingHost, value); }

    private string _sendingPort = "465";
    public string SendingPort { get => _sendingPort; set => Set(ref _sendingPort, value); }

    private int _sendingSecurityIndex = 0;
    public int SendingSecurityIndex { get => _sendingSecurityIndex; set => Set(ref _sendingSecurityIndex, value); }

    private string _sendingEmail = "";
    public string SendingEmail { get => _sendingEmail; set => Set(ref _sendingEmail, value); }

    private string _sendingPassword = "";
    public string SendingPassword { get => _sendingPassword; set => Set(ref _sendingPassword, value); }


    public bool IsMicrosoft => AuthenticationIndex == 1;
    public bool IsPassword => AuthenticationIndex == 0;
    public bool IsPop => ProtocolIndex == 1;
    public bool UsesOrganizationId => IsMicrosoft && TenantIndex == 3;
    public bool CanSelectFolders => !IsPop;
    public bool HasSavedAccount => Id.Length > 0;
    public bool CanCheckReading => HasSavedAccount && !HasChanges;
    public bool NeedsCheckBeforeSave => !HasSavedAccount && IsPassword;
    public bool CanUseMicrosoft => (PresetId == "custom" || Preset?.AuthenticationMethods.Contains("microsoft_oauth") == true)
        && _policy.MicrosoftIncomingHosts.TryGetValue(Protocol, out var hosts)
        && hosts.Contains(IncomingHost.Trim().TrimEnd('.'), StringComparer.OrdinalIgnoreCase);
    public string IncomingPasswordPlaceholder => _saved is null ? "Required for a new account" : "Leave blank to keep the saved password";
    public string SendingPasswordPlaceholder => _saved?.Smtp is null ? "Enter the password for sending" : "Leave blank to keep the saved password";
    public string IncomingServerLabel => IsPop ? "Reading server (POP3)" : "Reading server (IMAP)";
    public string IncomingServerPlaceholder => IsPop ? "pop.example.com" : "imap.example.com";
    public string FolderHint => IsPop
        ? "POP3 has one inbox, shown as INBOX. It has no other folders and can't tell which messages are unread. Your emails stay with your mail provider."
        : $"Choose up to {MaxFolders} IMAP folders for this account. Add more mailboxes to let your AI agent work across dozens of selected folders. Folder names currently need basic English letters, numbers or punctuation.";
    public string ReadCheckHint => IsPop ? "Check saved POP3 settings by reading mail without deleting messages."
        : "Check saved IMAP settings by reading mail without marking messages as read.";
    public string Tenant => TenantIndex switch { 0 => "common", 1 => "organizations", 2 => "consumers", 3 => OrganizationId, _ => "" };

    public (string Message, string LinkText, string Address) ProviderGuidance
    {
        get
        {
            var microsoftHint = AuthenticationIndex == 1
                ? "Don't enter your email password here. Sign in with Microsoft is already selected below. Press the sign-in button to continue."
                : "Don't enter your email password here. Choose Sign in with Microsoft below, then press the sign-in button.";
            var (message, linkText, address) = PresetId switch
            {
                "gmail" => ("Your usual Gmail password won't work here. Google calls the special password you need an app password. Open the link below to create one, then use it for both reading and sending. Google may ask you to turn on 2-Step Verification first. If the option is missing, your account or work settings may not allow it.",
                    "Create a password for this app", "https://myaccount.google.com/apppasswords"),
                "icloud" => ("Your usual Apple password won't work here. Create a separate password for AgentInbox using the link below. Apple calls it an app-specific password. Use it for both reading and sending. Apple requires an extra sign-in check, such as a code on your phone. Keep IMAP selected for iCloud mail.",
                    "Create an Apple password for this app", "https://support.apple.com/en-us/102654"),
                "outlook" => ($"{microsoftHint} Outlook may also need you to allow mail apps in its settings. The guide below shows where.",
                    "Help connecting Outlook", "https://support.microsoft.com/en-us/outlook/pop-imap-and-smtp-settings-for-outlook-com"),
                "microsoft365" => ($"{microsoftHint} Your work or school may block mail apps or sending. If that happens, ask your IT team to allow AgentInbox.",
                    "Sending help for your IT team", "https://learn.microsoft.com/en-us/exchange/clients-and-mobile-in-exchange-online/authenticated-client-smtp-submission"),
                _ when CanUseMicrosoft => (microsoftHint, "Help connecting Microsoft mail",
                    "https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth"),
                _ => ("Connect GoDaddy or another provider that allows compatible IMAP or POP3 access. Enter your provider's mail server settings; choose Microsoft 365 above for Microsoft-hosted accounts. Some providers require a separate app password.", "", "")
            };
            return (message, linkText, address);
        }
    }
    public string ProviderHint => ProviderGuidance.Message;
    public string ProviderHelpText => ProviderGuidance.LinkText;
    public Uri? ProviderHelpUri => ProviderGuidance.Address.Length == 0 ? null : new Uri(ProviderGuidance.Address);
    public bool HasProviderHelp => ProviderGuidance.Address.Length > 0;

    /// <summary>Loads a fresh draft without retaining passwords from a previous selection.</summary>
    /// <param name="account">Public saved-account snapshot; null starts an empty editor draft.</param>
    internal void Load(SavedAccount? account)
    {
        Batch(() =>
        {
            _saved = account;
            Id = account?.Id ?? "";
            Revision = account?.Revision;
            PresetId = account?.Preset ?? "custom";
            Label = account?.Label ?? "";
            Email = account?.Email ?? "";
            var auth = account?.Authentication;
            MicrosoftClientId = auth?.Method == "microsoft_oauth" ? auth.ClientId! : _defaultMicrosoftClientId;
            LoadTenant(auth?.Tenant ?? "common");
            ProtocolIndex = account?.Incoming.Protocol == "pop3" ? 1 : 0;
            IncomingHost = account?.Incoming.Host ?? "";
            IncomingPort = account?.Incoming.Port.ToString() ?? "993";
            IncomingSecurityIndex = account?.Incoming.Tls == "starttls" ? 1 : 0;
            IncomingEmail = account?.Incoming.Username ?? "";
            IncomingPassword = "";
            ReadEnabled = account?.ReadEnabled ?? false;
            SendingHost = account?.Smtp?.Host ?? "";
            SendingPort = account?.Smtp?.Port.ToString() ?? "465";
            SendingSecurityIndex = account?.Smtp?.Tls == "starttls" ? 1 : 0;
            SendingEmail = account?.Smtp?.Username ?? "";
            SendingPassword = "";
            ConfigureSending = account?.Smtp is not null;
            SendEnabled = account?.SendEnabled ?? false;
            if (account?.Smtp is null) FillSendingPreset();
            UpdateAuthenticationChoices();
            AuthenticationIndex = auth?.Method == "microsoft_oauth" ? 1 : 0;
            SetFolderChoices(account?.Folders ?? ["INBOX"], account?.Folders ?? ["INBOX"]);
            _errors = [];
            HasChanges = false;
        });
    }

    /// <summary>Fill empty endpoint sign-in names from the account email.</summary>
    internal void FillSignInEmails()
    {
        if (string.IsNullOrWhiteSpace(IncomingEmail)) IncomingEmail = Email.Trim();
        if (string.IsNullOrWhiteSpace(SendingEmail)) SendingEmail = Email.Trim();
    }

    /// <summary>Replace available and selected folder collections without marking a loading draft dirty.</summary>
    /// <param name="available">Exact selectable server folder names.</param>
    /// <param name="selected">Folder names to retain when present in the available set.</param>
    internal void SetFolderChoices(IEnumerable<string> available, IEnumerable<string> selected)
    {
        var choices = available.ToArray();
        var selection = selected.ToArray();
        var wasLoading = _loading;
        _loading = true;
        try
        {
            AvailableFolders.Clear();
            foreach (var folder in choices) AvailableFolders.Add(folder);
            SelectedFolders.Clear();
            foreach (var folder in selection.Where(choices.Contains)) SelectedFolders.Add(folder);
        }
        finally { _loading = wasLoading; }
        Notify(nameof(SelectedFolders));
    }

    /// <summary>Forward an explicit folder selection into operation or editor state.</summary>
    /// <param name="folders">Exact folder names returned by the server or explicitly selected by the user.</param>
    internal void SelectFolders(IEnumerable<string> folders)
    {
        if (_loading) return;
        SelectedFolders.Clear();
        foreach (var folder in folders) SelectedFolders.Add(folder);
        Changed(nameof(SelectedFolders));
    }

    /// <summary>Apply checked server folders to the editor and mark the draft changed.</summary>
    /// <param name="available">Exact selectable server folder names.</param>
    /// <param name="selected">Folder names to retain when present in the available set.</param>
    internal void ApplyFolderChoices(string[] available, string[] selected)
    {
        SetFolderChoices(available, selected);
        Changed(nameof(SelectedFolders));
    }

    /// <summary>React to a model property change while preserving batched update behavior.</summary>
    /// <param name="property">Name of the edited draft property.</param>
    protected override void Changed(string property)
    {
        if (_loading) return;
        Batch(() =>
        {
            if (property == nameof(PresetId)) ApplyPreset();
            if (property == nameof(ProtocolIndex))
            {
                IncomingPort = IsPop ? "995" : "993";
                IncomingSecurityIndex = 0;
                ApplyPreset();
            }
            if (property == nameof(ConfigureSending) && !ConfigureSending) SendEnabled = false;
            if (property == nameof(SendEnabled) && SendEnabled) ConfigureSending = true;
            if (property is nameof(PresetId) or nameof(ProtocolIndex) or nameof(IncomingHost))
            {
                UpdateAuthenticationChoices();
                if (IsPop && property != nameof(IncomingHost)) SetFolderChoices(["INBOX"], ["INBOX"]);
            }
            if (property == nameof(AuthenticationIndex) && IsMicrosoft) ClearPasswords();
            HasChanges = true;
            _errors = [];
        });
    }

    /// <summary>Suppress nested change handling and notify observers after the outer update.</summary>
    /// <param name="action">Group of draft updates whose notifications are batched.</param>
    private void Batch(Action action)
    {
        var wasLoading = _loading;
        _loading = true;
        try { action(); }
        finally { _loading = wasLoading; }
        if (!wasLoading) Notify();
    }

    /// <summary>Apply the selected provider defaults and explicit authentication choice.</summary>
    private void ApplyPreset()
    {
        if (Preset is not { } preset || preset.Incoming.Count == 0) return;
        if (!preset.Incoming.TryGetValue(Protocol, out var incoming))
        {
            var fallback = preset.Incoming.First();
            ProtocolIndex = fallback.Key == "pop3" ? 1 : 0;
            incoming = fallback.Value;
        }
        IncomingHost = incoming.Host;
        IncomingPort = incoming.Port.ToString();
        IncomingSecurityIndex = incoming.Tls == "starttls" ? 1 : 0;
        FillSendingPreset();
        UpdateAuthenticationChoices();
        AuthenticationIndex = preset.MicrosoftTenant is null ? 0 : 1;
        if (IsMicrosoft)
        {
            MicrosoftClientId = _defaultMicrosoftClientId;
            LoadTenant(preset.MicrosoftTenant!);
            ClearPasswords();
        }
    }

    /// <summary>Fill SMTP defaults when the selected provider supplies them.</summary>
    private void FillSendingPreset()
    {
        var sending = Preset?.Smtp;
        if (sending is null) return;
        SendingHost = sending.Host;
        SendingPort = sending.Port.ToString();
        SendingSecurityIndex = sending.Tls == "starttls" ? 1 : 0;
        if (string.IsNullOrWhiteSpace(SendingEmail)) SendingEmail = Email.Trim();
    }

    /// <summary>Project an engine tenant identifier onto the desktop account-type choices.</summary>
    /// <param name="tenant">Built-in Microsoft tenant name or an organization ID.</param>
    private void LoadTenant(string tenant)
    {
        TenantIndex = tenant switch { "common" => 0, "organizations" => 1, "consumers" => 2, _ => 3 };
        OrganizationId = TenantIndex == 3 ? tenant : "";
    }

    /// <summary>Offer Microsoft sign-in only for compatible server settings.</summary>
    private void UpdateAuthenticationChoices()
    {
        if (CanUseMicrosoft && AuthenticationChoices.Count == 1) AuthenticationChoices.Add("Sign in with Microsoft");
        if (!CanUseMicrosoft && AuthenticationChoices.Count == 2)
        {
            var selectedMicrosoft = IsMicrosoft;
            AuthenticationChoices.RemoveAt(1);
            if (selectedMicrosoft) AuthenticationIndex = -1;
        }
    }

    /// <summary>Release password values held by the current draft.</summary>
    internal void ClearPasswords() { IncomingPassword = ""; SendingPassword = ""; }

    /// <summary>Capture the account draft for saving or discovering folders.</summary>
    internal AccountSnapshot Snapshot()
    {
        ClearErrors();
        FillSignInEmails();
        var account = new AccountSettings
        {
            Id = Id, Label = Label, Email = Email, Preset = PresetId,
            Authentication = CaptureAuthentication(), Incoming = CaptureIncoming(),
            Smtp = ConfigureSending ? CaptureSending() : null, ReadEnabled = ReadEnabled,
            SendEnabled = ConfigureSending && SendEnabled, Folders = SelectedFolders.ToArray()
        };
        return new AccountSnapshot(account, new()
        {
            ["incoming"] = IsPassword ? IncomingPassword : "",
            ["smtp"] = IsPassword && ConfigureSending ? SendingPassword : ""
        }, Revision);
    }

    /// <summary>Capture sending settings and include incoming settings only when bound to Microsoft authorization.</summary>
    internal SmtpCheckSnapshot CaptureSmtpCheck()
    {
        ClearErrors();
        if (IsMicrosoft) FillSignInEmails();
        else if (string.IsNullOrWhiteSpace(SendingEmail)) SendingEmail = Email.Trim();

        var settings = new SmtpCheckSettings
        {
            Id = Id, Authentication = CaptureAuthentication(), Smtp = ConfigureSending ? CaptureSending() : null,
            Email = IsMicrosoft ? Email : null,
            Incoming = IsMicrosoft ? CaptureIncoming() : null,
            SendEnabled = IsMicrosoft ? SendEnabled : null
        };
        return new SmtpCheckSnapshot(settings, new() { ["smtp"] = IsPassword ? SendingPassword : "" }, Revision);
    }

    /// <summary>Capture the selected sign-in method without validating provider policy locally.</summary>
    private AccountAuthentication CaptureAuthentication() => AuthenticationIndex switch
    {
        0 => new(),
        1 => new() { Method = "microsoft_oauth", ClientId = MicrosoftClientId, Tenant = Tenant.Trim() },
        _ => new() { Method = "" }
    };

    /// <summary>Convert incoming draft fields into the engine endpoint payload.</summary>
    private MailEndpoint CaptureIncoming() => new()
    {
        Protocol = Protocol, Host = IncomingHost, Port = ParsePort(IncomingPort, "incoming.port"),
        Tls = Security(IncomingSecurityIndex), Username = IncomingEmail
    };

    /// <summary>Convert sending draft fields into the engine endpoint payload.</summary>
    private MailEndpoint CaptureSending() => new()
    {
        Host = SendingHost, Port = ParsePort(SendingPort, "smtp.port"),
        Tls = Security(SendingSecurityIndex), Username = SendingEmail
    };

    /// <summary>Translate a connection security choice into the engine's protocol value.</summary>
    /// <param name="index">Desktop choice index, or an invalid selection sent for engine validation.</param>
    private static string Security(int index) => index switch { 0 => "tls", 1 => "starttls", _ => "" };

    /// <summary>Convert the form's port text to an integer before serializing the endpoint.</summary>
    /// <param name="value">Port text as entered in the form.</param>
    /// <param name="field">Engine field path for the endpoint's port.</param>
    private static int ParsePort(string value, string field) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            ? port : throw new EngineException("Enter a whole number in Port.", field: field);

    /// <summary>Clear errors from a previous operation before submitting the draft again.</summary>
    private void ClearErrors()
    {
        if (_errors.Count == 0) return;
        _errors = [];
        Notify(nameof(Errors));
    }

    /// <summary>Map a safe engine field path to its editor property and tab.</summary>
    /// <param name="error">Engine validation error or local field conversion error.</param>
    internal void ShowError(EngineException error)
    {
        var field = error.Field switch
        {
            "label" => (nameof(Label), 0),
            "email" => (nameof(Email), 0),
            "preset" => (nameof(PresetId), 0),
            "authentication.method" => (nameof(AuthenticationIndex), 0),
            "authentication.tenant" => (UsesOrganizationId ? nameof(OrganizationId) : nameof(TenantIndex), 0),
            "incoming.protocol" => (nameof(ProtocolIndex), 1),
            "incoming.host" => (nameof(IncomingHost), 1),
            "incoming.port" => (nameof(IncomingPort), 1),
            "incoming.tls" => (nameof(IncomingSecurityIndex), 1),
            "incoming.username" => (nameof(IncomingEmail), 1),
            "passwords.incoming" => (nameof(IncomingPassword), 1),
            "folders" => (nameof(SelectedFolders), 1),
            "smtp.configured" => (nameof(ConfigureSending), 2),
            "smtp.host" => (nameof(SendingHost), 2),
            "smtp.port" => (nameof(SendingPort), 2),
            "smtp.tls" => (nameof(SendingSecurityIndex), 2),
            "smtp.username" => (nameof(SendingEmail), 2),
            "passwords.smtp" => (nameof(SendingPassword), 2),
            _ => ((string?)null, 0)
        };
        if (field.Item1 is { } property)
        {
            _errors = [new(property, error.Message, field.Item2)];
            Notify(nameof(Errors));
        }
    }
}

/// <summary>One provider choice with an ID from the engine and desktop presentation.</summary>
/// <param name="Id">Stable provider ID used in account settings.</param>
/// <param name="Label">Provider name shown in the native picker.</param>
internal sealed record ProviderChoice(string Id, string Label)
{
    public string Glyph => Id switch { "custom" => "\uE713", "icloud" => "\uE753", _ => "\uE715" };
    public string Accent => Id switch
    {
        "gmail" => "#D94235", "icloud" => "#3282CF", "outlook" => "#1274C4",
        "microsoft365" => "#8661C5", _ => "#667789"
    };

    /// <summary>Expose the visible provider name to native selection and text search.</summary>
    public override string ToString() => Label;
}
