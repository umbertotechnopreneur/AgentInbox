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
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using MailMeUp.Desktop.Models;
using MailMeUp.Desktop.Services;

namespace MailMeUp.Desktop.ViewModels;

/// <summary>Owns an editable account, provider defaults and validation independently of its view.</summary>
/// <param name="defaultMicrosoftClientId">Public-client ID shipped with the app, never entered as a user credential.</param>
internal sealed class AccountEditorModel(string defaultMicrosoftClientId) : ObservableModel
{
    private readonly string _defaultMicrosoftClientId = defaultMicrosoftClientId;
    private bool _loading;
    private bool _validationShown;
    private bool _checking;
    private bool _smtpOnly;
    private SavedAccount? _saved;
    private IReadOnlyList<AccountFieldError> _errors = [];
    private IReadOnlyDictionary<string, ProviderPreset> _presets = new Dictionary<string, ProviderPreset>();
    private static readonly string[] PresetIds = ["custom", "gmail", "icloud", "outlook", "microsoft365"];

    public string Id { get; private set; } = "";
    public int? Revision { get; private set; }
    public bool HasChanges { get; private set; }
    public bool IsLoading => _loading;
    public string MicrosoftClientId { get; private set; } = defaultMicrosoftClientId;
    public ObservableCollection<string> AuthenticationChoices { get; } = ["Use a password"];
    public ObservableCollection<string> AvailableFolders { get; } = [];
    public ObservableCollection<string> SelectedFolders { get; } = [];
    public IReadOnlyList<AccountFieldError> Errors => _errors;
    private string PresetId => PresetIndex is >= 0 and < 5 ? PresetIds[PresetIndex] : "";

    /// <summary>Load the shared engine provider catalog before applying preset choices.</summary>
    /// <param name="presets">Typed provider catalog returned by the shared Python preset operation.</param>
    internal void SetPresets(IReadOnlyDictionary<string, ProviderPreset> presets)
    {
        if (presets.Count != PresetIds.Length || PresetIds.Any(key => !presets.ContainsKey(key)))
            throw new EngineException("The engine returned unsupported email providers.");
        _presets = presets;
    }

    private int _presetIndex = 0;
    public int PresetIndex { get => _presetIndex; set => Set(ref _presetIndex, value); }

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
    public bool CanUseMicrosoft => PresetIndex is not (1 or 2) && UsesMicrosoftServers(includeSmtp: false);
    public string IncomingPasswordPlaceholder => _saved is null ? "Required for a new account" : "Leave blank to keep the saved password";
    public string SendingPasswordPlaceholder => _saved?.Smtp is null ? "Enter the password for sending" : "Leave blank to keep the saved password";
    public string IncomingServerLabel => IsPop ? "Reading server (POP3)" : "Reading server (IMAP)";
    public string IncomingServerPlaceholder => IsPop ? "pop.example.com" : "imap.example.com";
    public string FolderHint => IsPop
        ? "POP3 has one inbox, shown as INBOX. It has no other folders and can't tell which messages are unread. Your emails stay with your mail provider."
        : "Choose up to twenty IMAP folders for this account. Add more mailboxes to let your AI agent work across dozens of selected folders. Folder names currently need basic English letters, numbers or punctuation.";
    public string ReadCheckHint => IsPop ? "Check saved POP3 settings by reading mail without deleting messages."
        : "Check saved IMAP settings by reading mail without marking messages as read.";
    public string Tenant => TenantIndex switch { 0 => "common", 1 => "organizations", 2 => "consumers", _ => OrganizationId.Trim() };

    public (string Message, string LinkText, string Address) ProviderGuidance
    {
        get
        {
            var microsoftHint = AuthenticationIndex == 1
                ? "Don't enter your email password here. Sign in with Microsoft is already selected below. Press the sign-in button to continue."
                : "Don't enter your email password here. Choose Sign in with Microsoft below, then press the sign-in button.";
            var (message, linkText, address) = PresetIndex switch
            {
                1 => ("Your usual Gmail password won't work here. Google calls the special password you need an app password. Open the link below to create one, then use it for both reading and sending. Google may ask you to turn on 2-Step Verification first. If the option is missing, your account or work settings may not allow it.",
                    "Create a password for this app", "https://myaccount.google.com/apppasswords"),
                2 => ("Your usual Apple password won't work here. Create a separate password for AgentInbox using the link below. Apple calls it an app-specific password. Use it for both reading and sending. Apple requires an extra sign-in check, such as a code on your phone. Keep IMAP selected for iCloud mail.",
                    "Create an Apple password for this app", "https://support.apple.com/en-us/102654"),
                3 => ($"{microsoftHint} Outlook may also need you to allow mail apps in its settings. The guide below shows where.",
                    "Help connecting Outlook", "https://support.microsoft.com/en-us/outlook/pop-imap-and-smtp-settings-for-outlook-com"),
                4 => ($"{microsoftHint} Your work or school may block mail apps or sending. If that happens, ask your IT team to allow AgentInbox.",
                    "Sending help for your IT team", "https://learn.microsoft.com/en-us/exchange/clients-and-mobile-in-exchange-online/authenticated-client-smtp-submission"),
                _ when UsesMicrosoftServers(includeSmtp: false) => (microsoftHint, "Help connecting Microsoft mail",
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
            PresetIndex = account?.Preset switch { "gmail" => 1, "icloud" => 2, "outlook" => 3, "microsoft365" => 4, _ => 0 };
            Label = account?.Label ?? "";
            Email = account?.Email ?? "";
            var auth = account?.Authentication;
            MicrosoftClientId = auth?.Method == "microsoft_oauth" ? auth.ClientId! : _defaultMicrosoftClientId;
            var tenant = auth?.Tenant ?? "common";
            TenantIndex = tenant switch { "common" => 0, "organizations" => 1, "consumers" => 2, _ => 3 };
            OrganizationId = TenantIndex == 3 ? tenant : "";
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
            _validationShown = false;
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
    /// <param name="property">Observable property name or native dependency property being updated.</param>
    protected override void Changed(string property)
    {
        if (_loading) return;
        Batch(() =>
        {
            if (property == nameof(PresetIndex)) ApplyPreset();
            if (property == nameof(ProtocolIndex))
            {
                IncomingPort = IsPop ? "995" : "993";
                IncomingSecurityIndex = 0;
                if (PresetIndex > 0) ApplyPreset();
            }
            if (property == nameof(ConfigureSending) && !ConfigureSending) SendEnabled = false;
            if (property == nameof(SendEnabled) && SendEnabled) ConfigureSending = true;
            UpdateAuthenticationChoices();
            if (property == nameof(AuthenticationIndex) && IsMicrosoft) ClearPasswords();
            if (IsPop) SetFolderChoices(["INBOX"], ["INBOX"]);
            HasChanges = true;
            if (_validationShown) Validate(_checking, _smtpOnly);
        });
    }

    /// <summary>Suppress nested change handling and notify observers after the outer update.</summary>
    /// <param name="action">Action callback or native action control placed with the content.</param>
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
        if (PresetIndex is <= 0 or > 4) return;
        var preset = _presets[PresetId];
        if (!preset.Incoming.ContainsKey(IsPop ? "pop3" : "imap")) ProtocolIndex = 0;
        var incoming = preset.Incoming[IsPop ? "pop3" : "imap"];
        IncomingHost = incoming.Host;
        IncomingPort = incoming.Port.ToString();
        IncomingSecurityIndex = incoming.Tls == "starttls" ? 1 : 0;
        FillSendingPreset();
        UpdateAuthenticationChoices();
        AuthenticationIndex = preset.AuthenticationMethods.Contains("microsoft_oauth") ? 1 : 0;
        if (IsMicrosoft)
        {
            MicrosoftClientId = _defaultMicrosoftClientId;
            TenantIndex = PresetIndex == 3 ? 2 : 1;
            ClearPasswords();
        }
    }

    /// <summary>Fill SMTP defaults when the selected provider supplies them.</summary>
    private void FillSendingPreset()
    {
        if (PresetIndex == 0) return;
        var sending = _presets[PresetId].Smtp;
        if (sending is null) return;
        SendingHost = sending.Host;
        SendingPort = sending.Port.ToString();
        SendingSecurityIndex = sending.Tls == "starttls" ? 1 : 0;
        if (string.IsNullOrWhiteSpace(SendingEmail)) SendingEmail = Email.Trim();
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

    /// <summary>Check incoming and optional sending hosts against the supported Microsoft endpoints.</summary>
    /// <param name="includeSmtp">Whether configured SMTP hosts must also match supported Microsoft endpoints.</param>
    internal bool UsesMicrosoftServers(bool includeSmtp = true)
    {
        var host = IncomingHost.Trim();
        var incomingMatches = host.Equals("outlook.office365.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals(IsPop ? "pop-mail.outlook.com" : "imap-mail.outlook.com", StringComparison.OrdinalIgnoreCase);
        return incomingMatches && (!includeSmtp || !ConfigureSending
            || SendingHost.Trim().Equals("smtp.office365.com", StringComparison.OrdinalIgnoreCase)
            || SendingHost.Trim().Equals("smtp-mail.outlook.com", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Release password values held by this draft or operation snapshot.</summary>
    internal void ClearPasswords() { IncomingPassword = ""; SendingPassword = ""; }

    /// <summary>Captures model data, never control values, for the common management contract.</summary>
    /// <param name="checking">Whether to validate a connection check rather than a settings save.</param>
    /// <param name="smtpOnly">Whether to check only SMTP without requiring incoming credentials.</param>
    internal AccountSnapshot Snapshot(bool checking = false, bool smtpOnly = false)
    {
        FillSignInEmails();
        _validationShown = true;
        Validate(checking, smtpOnly);
        Notify();
        if (Errors.Count > 0) throw new EngineException("Check the fields marked in red.\n" + string.Join("\n", Errors.Select(error => error.Message).Distinct()));
        if (IsMicrosoft && !Guid.TryParse(MicrosoftClientId, out _))
            throw new EngineException("This app's Microsoft settings need fixing. The saved app ID is invalid.");
        var authentication = IsMicrosoft
            ? new AccountAuthentication { Method = "microsoft_oauth", ClientId = Guid.Parse(MicrosoftClientId).ToString(), Tenant = Tenant }
            : new AccountAuthentication();
        string[] folders = IsPop ? ["INBOX"] : SelectedFolders.ToArray();
        if (checking && folders.Length == 0) folders = ["INBOX"];
        var settings = new AccountSettings
        {
            Id = Id, Label = Label.Trim(), Email = Email.Trim(),
            Preset = PresetId,
            Authentication = authentication, Incoming = IncomingEndpoint(),
            Smtp = ConfigureSending ? SendingEndpoint() : null, ReadEnabled = ReadEnabled,
            SendEnabled = ConfigureSending && SendEnabled, Folders = folders
        };
        return new AccountSnapshot(settings, new()
        {
            ["incoming"] = IsPassword ? IncomingPassword : "", ["smtp"] = IsPassword && ConfigureSending ? SendingPassword : ""
        }, Revision);
    }

    /// <summary>Capture normalized incoming settings from the editor model.</summary>
    private MailEndpoint IncomingEndpoint() => new() { Protocol = IsPop ? "pop3" : "imap", Host = IncomingHost.Trim().TrimEnd('.').ToLowerInvariant(),
        Port = int.Parse(IncomingPort), Tls = IncomingSecurityIndex == 1 ? "starttls" : "tls", Username = IncomingEmail.Trim() };
    /// <summary>Capture normalized SMTP settings from the editor model.</summary>
    private MailEndpoint SendingEndpoint() => new() { Host = SendingHost.Trim().TrimEnd('.').ToLowerInvariant(), Port = int.Parse(SendingPort),
        Tls = SendingSecurityIndex == 1 ? "starttls" : "tls", Username = SendingEmail.Trim() };

    /// <summary>Require unchanged password-based endpoint settings before reusing saved credentials.</summary>
    /// <param name="current">Normalized draft endpoint being compared for safe credential reuse.</param>
    /// <param name="saved">Saved endpoint or committed account result used to refresh model state.</param>
    private bool CanKeepPassword(MailEndpoint? current, MailEndpoint? saved) =>
        _saved?.Authentication.Method == "password" && current == saved;

    /// <summary>Collect actionable field errors for the requested save or connection check.</summary>
    /// <param name="checking">Whether to validate a connection check rather than a settings save.</param>
    /// <param name="smtpOnly">Whether to check only SMTP without requiring incoming credentials.</param>
    private void Validate(bool checking, bool smtpOnly)
    {
        _checking = checking;
        _smtpOnly = smtpOnly;
        var errors = new List<AccountFieldError>();
        /// <summary>Append one field error when its validation predicate fails.</summary>
        /// <param name="property">Observable property name or native dependency property being updated.</param>
        /// <param name="valid">Result of the field predicate; false appends an actionable error.</param>
        /// <param name="message">Safe user-facing outcome or guidance; never raw credentials or mail content.</param>
        /// <param name="tab">Account tab index containing the invalid field.</param>
        void Check(string property, bool valid, string message, int tab)
        {
            if (!valid) errors.Add(new(property, message, tab));
        }
        /// <summary>Check the supported hostname shape without changing the draft.</summary>
        /// <param name="host">Draft hostname to validate without a network request.</param>
        static bool Host(string host)
        {
            var normalized = host.Trim().TrimEnd('.');
            return normalized.Length is > 0 and <= 255
                && Regex.IsMatch(normalized, @"^[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?$")
                && !host.Contains("..");
        }
        /// <summary>Require a numeric TCP port in the supported range.</summary>
        /// <param name="port">Draft TCP port text to validate without a network request.</param>
        static bool Port(string port) => int.TryParse(port, out var number) && number is >= 1 and <= 65535;

        // Identity and authentication errors belong to the Account tab.
        Check(nameof(Label), Label.Trim().Length is > 0 and <= 100, "Enter a connection name with 100 characters or fewer.", 0);
        Check(nameof(PresetIndex), PresetIndex is >= 0 and <= 4, "Choose your email provider.", 0);
        var email = Email.Trim();
        Check(nameof(Email), email.Length is > 0 and <= 254
            && Regex.IsMatch(email, @"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?$")
            && !email.Contains("..") && !email.StartsWith('.') && !email.Contains(".@"), "Enter your email address, such as you@example.com.", 0);
        Check(nameof(AuthenticationIndex), AuthenticationIndex is 0 or 1, "Choose how to connect to these mail servers.", 0);
        Check(nameof(TenantIndex), !IsMicrosoft || TenantIndex is >= 0 and <= 3, "Choose your Microsoft account type.", 0);
        Check(nameof(OrganizationId), !UsesOrganizationId || Guid.TryParse(OrganizationId.Trim(), out _), "Enter the organization ID from your IT team.", 0);

        // Incoming credentials may be reused only for an unchanged saved endpoint.
        Check(nameof(IncomingHost), Host(IncomingHost), "Enter your reading server, such as imap.example.com.", 1);
        Check(nameof(ProtocolIndex), ProtocolIndex is 0 or 1, "Choose how to read mail.", 1);
        Check(nameof(IncomingSecurityIndex), IncomingSecurityIndex is 0 or 1, "Choose a secure connection for reading mail.", 1);
        var incomingPortValid = Port(IncomingPort);
        Check(nameof(IncomingPort), incomingPortValid, "Enter a reading port number from 1 to 65535.", 1);
        Check(nameof(IncomingEmail), IncomingEmail.Trim().Length is > 0 and <= 255 && (!IsMicrosoft || IncomingEmail.Trim().Equals(email, StringComparison.OrdinalIgnoreCase)),
            IsMicrosoft ? "Use the same email address as in Account for Microsoft." : "Enter the email address used to read your mail.", 1);
        Check(nameof(IncomingHost), !IsMicrosoft || UsesMicrosoftServers(includeSmtp: false), "Microsoft sign-in needs a Microsoft reading server.", 1);
        if (IsPassword && !smtpOnly)
            Check(nameof(IncomingPassword), IncomingPassword.Length > 0 || (incomingPortValid && CanKeepPassword(IncomingEndpoint(), _saved?.Incoming)),
                "Enter your mail password or app password. Changed server settings need it again.", 1);

        // Sending remains optional, but configured endpoints must be complete.
        if (ConfigureSending)
        {
            Check(nameof(SendingHost), Host(SendingHost), "Enter your sending server, such as smtp.example.com.", 2);
            Check(nameof(SendingSecurityIndex), SendingSecurityIndex is 0 or 1, "Choose a secure connection for sending mail.", 2);
            var sendingPortValid = Port(SendingPort);
            Check(nameof(SendingPort), sendingPortValid, "Enter a sending port number from 1 to 65535.", 2);
            Check(nameof(SendingEmail), SendingEmail.Trim().Length is > 0 and <= 255 && (!IsMicrosoft || SendingEmail.Trim().Equals(email, StringComparison.OrdinalIgnoreCase)),
                IsMicrosoft ? "Use the same email address as in Account for Microsoft." : "Enter the email address used to send your mail.", 2);
            Check(nameof(SendingHost), !IsMicrosoft || UsesMicrosoftServers(), "Microsoft sign-in needs a Microsoft sending server.", 2);
            if (IsPassword)
                Check(nameof(SendingPassword), SendingPassword.Length > 0 || (sendingPortValid && CanKeepPassword(SendingEndpoint(), _saved?.Smtp)),
                    "Enter the password or app password for sending. Changed server settings need it again.", 2);
        }

        // Folder limits are per account; POP3 always keeps its single INBOX.
        string[] folders = IsPop ? ["INBOX"] : SelectedFolders.ToArray();
        Check(nameof(SelectedFolders), (checking && folders.Length == 0) || folders.Length is >= 1 and <= 20,
            "Choose between one and twenty mail folders under Read mail.", 1);
        Check(nameof(SelectedFolders), !folders.Any(folder => folder.Any(character => character < 32 || character > 126)),
            "Folder names currently need basic English letters, numbers or punctuation, such as INBOX.", 1);
        _errors = errors;
    }
}
