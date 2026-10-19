// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Text.Json;
using MailMeUp.Desktop.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace MailMeUp.Desktop;

/// <summary>Configures standard mail connections and collects approval for each outgoing email.</summary>
public sealed partial class MainWindow : Window
{
    private readonly PythonEngine _engine;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Queue<string> _reviewRequests = new();
    private readonly List<JsonElement> _accounts = [];
    private bool _loaded;
    private bool _busy;
    private bool _reviewing;
    private bool _loadingForm;
    private string _accountId = "";
    private string _lastRequestId = "";
    private int? _revision;
    private ComboBox _accountPicker = new();
    private TabView _accountTabs = new();
    private Grid _accountToolbar = new();
    private StackPanel _accountEditor = new();
    private StackPanel _accountEmptyState = new();
    private Button _createFirstAccount = new();
    private Button _removeAccount = new();
    private Button _cancelAccount = new();
    private TextBlock _newAccountHeading = new();
    private bool _accountsLoaded;
    private bool _creatingAccount;
    private bool _hasUnsavedChanges;
    private string _previousAccountId = "";
    private ComboBox _preset = new();
    private TextBox _label = new();
    private TextBox _email = new();
    private ComboBox _authentication = new();
    private TextBlock _authenticationHint = new();
    private TextBox _clientId = new();
    private ComboBox _tenant = new();
    private TextBox _tenantId = new();
    private StackPanel _microsoftFields = new();
    private Button _microsoftSignIn = new();
    private ComboBox _incomingProtocol = new();
    private TextBox _incomingHost = new();
    private TextBox _incomingPort = new();
    private ComboBox _incomingTls = new();
    private TextBox _incomingUser = new();
    private PasswordBox _incomingPassword = new();
    private TextBox _folders = new();
    private TextBlock _folderHint = new();
    private ToggleSwitch _readEnabled = new();
    private ToggleSwitch _configureSmtp = new();
    private ToggleSwitch _sendEnabled = new();
    private TextBox _smtpHost = new();
    private TextBox _smtpPort = new();
    private ComboBox _smtpTls = new();
    private TextBox _smtpUser = new();
    private PasswordBox _smtpPassword = new();
    private StackPanel _smtpFields = new();
    private Button _save = new();
    private Button _testRead = new();
    private StackPanel _incomingChecks = new();
    private StackPanel _smtpChecks = new();
    private Button _testSmtp = new();
    private Button _sendTest = new();

    internal MainWindow(PythonEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (dpi == 0) throw new InvalidOperationException("The window display scale could not be determined.");
        var scale = dpi / 96.0;
        // AppWindow uses physical pixels; size the form in logical pixels at the monitor's scale.
        var width = Math.Min((int)Math.Round(1280 * scale), area.Width);
        var height = Math.Min((int)Math.Round(960 * scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2, width, height));
        Closed += (_, _) => _lifetime.Cancel();
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr window);

    internal void RequestReview(string? requestId)
    {
        if (requestId is null) return;
        if (!_reviewRequests.Contains(requestId)) _reviewRequests.Enqueue(requestId);
        if (_loaded) _ = DrainReviewsAsync();
    }

    private async void Root_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        Navigation.SelectedItem = Navigation.MenuItems[0];
        await DrainReviewsAsync();
    }

    private async void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_loaded || args.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is string page) await OpenPageAsync(page);
    }

    private async Task OpenPageAsync(string page)
    {
        await RunAsync(async () =>
        {
            Page.Children.Clear();
            PageScroll.ChangeView(0, 0, null, disableAnimation: true);
            try
            {
                switch (page)
                {
                    case "accounts": BuildAccountsPage(); await LoadAccountsAsync(); break;
                    case "outgoing": await BuildOutgoingPageAsync(); break;
                    case "assistant": await BuildAssistantPageAsync(); break;
                    default: throw new InvalidOperationException("Unknown setup page.");
                }
            }
            catch
            {
                // Never leave a partially constructed form with actionable Save or Cancel buttons.
                Page.Children.Clear();
                Page.Children.Add(Text("This page could not be opened", 22));
                Page.Children.Add(Hint("Check the error below. You can try again or choose another page."));
                Page.Children.Add(Action("Try again", () => _ = OpenPageAsync(page),
                    tooltip: "Reload this page without repeating a mail operation."));
                throw;
            }
        });
    }

    private void BuildAccountsPage()
    {
        _accountsLoaded = false;
        _creatingAccount = false;
        _save = Action("Save changes", () => _ = RunAsync(SaveAccountAsync), accent: true);
        _cancelAccount = Action("Cancel", CancelNewAccount, tooltip: "Discard this new account and return to your saved accounts.");
        Page.Children.Add(PageHeading("Email accounts", "Manage your connections and choose what your assistant can read or send.", Actions(_cancelAccount, _save)));
        _createFirstAccount = Action("Create your first account", StartNewAccount, accent: true,
            tooltip: "Open the setup form for your first email account.");
        _accountEmptyState = Stack(Text("There are no accounts yet", 22), _createFirstAccount);
        Page.Children.Add(_accountEmptyState);
        _accountTabs = new TabView
        {
            IsAddTabButtonVisible = false, CanDragTabs = false, CanReorderTabs = false,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top
        };
        AutomationProperties.SetName(_accountTabs, "Account settings");
        _accountPicker = new ComboBox { Header = "Edit account", PlaceholderText = "Choose an account", HorizontalAlignment = HorizontalAlignment.Stretch };
        _accountPicker.SelectionChanged += (_, _) =>
        {
            if (_loadingForm || _accountPicker.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            var selected = _accounts.FirstOrDefault(account => Value(account, "id") == id);
            if (selected.ValueKind != JsonValueKind.Undefined) FillAccount(selected);
        };
        var accountActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom };
        accountActions.Children.Add(IconAction("Add account", "\uE710", 0x16856A, StartNewAccount,
            "Open the setup form for another email account."));
        _removeAccount = IconAction("Remove account", "\uE74D", 0xD83B35, () => _ = RunAsync(RemoveAccountAsync),
            "Remove this account's local settings and credentials. Messages on the server stay in place.");
        accountActions.Children.Add(_removeAccount);
        accountActions.Children.Add(IconAction("Refresh accounts", "\uE72C", 0x2477C8, () => _ = RunAsync(LoadAccountsAsync),
            "Reload saved accounts and their settings."));
        _accountToolbar = new Grid { ColumnSpacing = 12 };
        _accountToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _accountToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _accountToolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _accountToolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _accountToolbar.Children.Add(_accountPicker);
        Grid.SetColumn(accountActions, 1);
        _accountToolbar.Children.Add(accountActions);
        _accountToolbar.SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width < 400;
            Grid.SetColumnSpan(_accountPicker, compact ? 2 : 1);
            Grid.SetRow(accountActions, compact ? 1 : 0);
            Grid.SetColumn(accountActions, compact ? 0 : 1);
            Grid.SetColumnSpan(accountActions, compact ? 2 : 1);
            _accountToolbar.RowSpacing = compact ? 8 : 0;
        };
        Page.Children.Add(_accountToolbar);

        _preset = new ComboBox { Header = "Server preset", HorizontalAlignment = HorizontalAlignment.Stretch };
        _preset.Items.Add(PresetItem("Custom mail server", "\uE713", 0x667789));
        _preset.Items.Add(PresetItem("Gmail", "\uE715", 0xD94235));
        _preset.Items.Add(PresetItem("iCloud", "\uE753", 0x3282CF));
        _preset.Items.Add(PresetItem("Outlook.com", "\uE715", 0x1274C4));
        _preset.Items.Add(PresetItem("Microsoft 365", "\uE71D", 0x8661C5));
        _preset.SelectionChanged += (_, _) => ApplyPreset();
        _label = Field("Account name", "Personal, work, or a name you recognize");
        _email = Field("Email address", "you@example.com");
        _email.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_incomingUser.Text)) _incomingUser.Text = _email.Text.Trim();
            if (string.IsNullOrWhiteSpace(_smtpUser.Text)) _smtpUser.Text = _email.Text.Trim();
        };
        _authentication = new ComboBox { Header = "Authentication", PlaceholderText = "Choose authentication", HorizontalAlignment = HorizontalAlignment.Stretch };
        _authentication.Items.Add("Password — account or app password");
        _authentication.SelectionChanged += (_, _) =>
        {
            if (!_loadingForm) UpdateAuthenticationPresentation(clearPasswords: true);
        };
        _clientId = Field("Microsoft app registration · Application (client) ID", "Public client ID from your app registration");
        _tenant = new ComboBox { Header = "Microsoft account type", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var name in new[] { "Personal and work accounts (common)", "Work or school accounts (organizations)", "Personal accounts (consumers)", "Specific tenant ID" })
            _tenant.Items.Add(name);
        _tenantId = Field("Tenant ID", "Directory (tenant) GUID");
        _tenant.SelectionChanged += (_, _) => _tenantId.Visibility = _tenant.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        _microsoftSignIn = Action("Sign in to Microsoft…", () => _ = RunAsync(SignInMicrosoftAsync));
        _microsoftFields = Stack(_clientId, _tenant, _tenantId,
            Hint("Use an app registration configured for public-client device sign-in and your account type. The client ID is public, not a password. AgentInbox does not create or verify the registration."),
            _microsoftSignIn,
            Hint("Save this authentication choice before signing in. It applies to incoming mail and SMTP on this account. Password failures never switch authentication automatically."));
        _authenticationHint = Hint("");
        var accountContent = Section("", _preset, Pair(_label, _email), _authentication, _microsoftFields, _authenticationHint);

        _incomingProtocol = new ComboBox { Header = "Incoming protocol", HorizontalAlignment = HorizontalAlignment.Stretch };
        _incomingProtocol.Items.Add("IMAP — mail and server folders");
        _incomingProtocol.Items.Add("POP3 — mailbox messages");
        _incomingProtocol.SelectionChanged += (_, _) => IncomingProtocolChanged();
        _incomingHost = Field("Incoming server", "imap.example.com");
        _incomingPort = Field("Port", "993");
        _incomingTls = TlsField();
        _incomingUser = Field("Username", "Usually your full email address");
        _incomingPassword = Password("Incoming password");
        _folders = Field("Folders available to your assistant", "INBOX");
        _folderHint = Hint("");
        _readEnabled = new ToggleSwitch { Header = "Share this account with my assistant", OnContent = "Enabled", OffContent = "Disabled" };
        var incomingSettings = Section("", _incomingProtocol, Pair(_incomingHost, _incomingPort), _incomingTls,
            Pair(_incomingUser, _incomingPassword), _folders, _folderHint,
            Hint("Passwords and Microsoft sign-in tokens stay in Windows Credential Manager. Assistant configuration contains neither."), _readEnabled);
        _testRead = Action("Test IMAP reading", () => _ = RunAsync(TestReadAsync));
        _incomingChecks = Stack(_testRead, Hint("Save changes before testing. This check uses the saved incoming settings."));
        var incomingContent = Stack(incomingSettings, _incomingChecks);

        _configureSmtp = new ToggleSwitch { Header = "Configure outgoing SMTP", OnContent = "Configured", OffContent = "Off" };
        _smtpHost = Field("SMTP server", "smtp.example.com");
        _smtpPort = Field("Port", "465");
        _smtpTls = TlsField();
        _smtpUser = Field("Username", "Usually your full email address");
        _smtpPassword = Password("SMTP password");
        _sendEnabled = new ToggleSwitch { Header = "Enable sending from this account", OnContent = "Enabled", OffContent = "Disabled" };
        _testSmtp = Action("Test SMTP connection", () => _ = RunAsync(TestSmtpAsync),
            tooltip: "Check the saved SMTP connection without sending an email.");
        _sendTest = Action("Send a test email…", () => _ = RunAsync(PrepareTestAsync),
            tooltip: "Choose a recipient, then review and approve the exact test message before sending.");
        _smtpChecks = Stack(Actions(_testSmtp, _sendTest), Hint("Enable sending and save changes before testing SMTP. A test email still requires your approval."));
        _smtpFields = Stack(Pair(_smtpHost, _smtpPort), _smtpTls, Pair(_smtpUser, _smtpPassword), _sendEnabled,
            Hint("Every email still needs your approval in a separate review window. Enabling SMTP never approves a message."), _smtpChecks);
        _configureSmtp.Toggled += (_, _) =>
        {
            _smtpFields.Visibility = _configureSmtp.IsOn ? Visibility.Visible : Visibility.Collapsed;
            if (!_configureSmtp.IsOn) _sendEnabled.IsOn = false;
            if (!_loadingForm) UpdateAuthenticationChoices();
        };
        _incomingHost.TextChanged += (_, _) => { if (!_loadingForm) UpdateAuthenticationChoices(); };
        _smtpHost.TextChanged += (_, _) => { if (!_loadingForm) UpdateAuthenticationChoices(); };
        var outgoingContent = Stack(_configureSmtp,
            Hint("Outgoing mail is optional. Configure SMTP only if you want to send messages."), _smtpFields);
        foreach (var tab in new[] {
            AccountTab("Account", "\uE77B", accountContent),
            AccountTab("Incoming", "\uE715", incomingContent),
            AccountTab("Outgoing", "\uE724", outgoingContent) })
            _accountTabs.TabItems.Add(tab);
        _accountTabs.SelectedIndex = 0;
        _newAccountHeading = Text("New account", 22);
        _accountEditor = Stack(_newAccountHeading, _accountTabs);
        Page.Children.Add(_accountEditor);
        TrackAccountChanges();
        FillAccount(null);
    }

    private void StartNewAccount()
    {
        if (_busy) return;
        _previousAccountId = _accountId;
        _creatingAccount = true;
        _accountPicker.SelectedIndex = -1;
        FillAccount(null);
        _accountTabs.SelectedIndex = 0;
        _label.Focus(FocusState.Programmatic);
    }

    private void CancelNewAccount()
    {
        if (_busy) return;
        _creatingAccount = false;
        var index = _accounts.FindIndex(account => Value(account, "id") == _previousAccountId);
        _accountPicker.SelectedIndex = index >= 0 ? index : _accounts.Count > 0 ? 0 : -1;
        if (_accounts.Count == 0) FillAccount(null);
        UpdateAccountPresentation();
        if (_accounts.Count == 0) _createFirstAccount.Focus(FocusState.Programmatic);
        else _accountPicker.Focus(FocusState.Programmatic);
    }

    private void UpdateAccountPresentation()
    {
        var editing = _accountsLoaded && (_creatingAccount || _accountId.Length > 0);
        _accountEmptyState.Visibility = _accountsLoaded && _accounts.Count == 0 && !_creatingAccount ? Visibility.Visible : Visibility.Collapsed;
        _accountToolbar.Visibility = _accountsLoaded && _accounts.Count > 0 && !_creatingAccount ? Visibility.Visible : Visibility.Collapsed;
        _accountEditor.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _save.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _cancelAccount.Visibility = _creatingAccount ? Visibility.Visible : Visibility.Collapsed;
        _newAccountHeading.Visibility = _creatingAccount ? Visibility.Visible : Visibility.Collapsed;
        _removeAccount.IsEnabled = _accountId.Length > 0;
        _save.Content = _creatingAccount ? "Create account" : "Save changes";
        AutomationProperties.SetName(_save, (string)_save.Content);
        ToolTipService.SetToolTip(_save, _creatingAccount
            ? "Save this new account and store its credentials securely."
            : "Save changes to the selected account and its credentials.");
        UpdateAccountChecks();
    }

    private void TrackAccountChanges()
    {
        foreach (var field in new[] { _label, _email, _clientId, _tenantId, _incomingHost, _incomingPort, _incomingUser, _folders, _smtpHost, _smtpPort, _smtpUser })
            field.TextChanged += (_, _) => MarkAccountChanged();
        foreach (var field in new[] { _incomingPassword, _smtpPassword })
            field.PasswordChanged += (_, _) => MarkAccountChanged();
        foreach (var field in new[] { _preset, _authentication, _tenant, _incomingProtocol, _incomingTls, _smtpTls })
            field.SelectionChanged += (_, _) => MarkAccountChanged();
        foreach (var field in new[] { _readEnabled, _configureSmtp, _sendEnabled })
            field.Toggled += (_, _) => MarkAccountChanged();
    }

    private void MarkAccountChanged()
    {
        if (_loadingForm) return;
        _hasUnsavedChanges = true;
        UpdateAccountChecks();
    }

    private void UpdateAccountChecks()
    {
        var saved = _accountId.Length > 0 && !_creatingAccount;
        var ready = saved && !_hasUnsavedChanges;
        _incomingChecks.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
        _smtpChecks.Visibility = saved && _configureSmtp.IsOn ? Visibility.Visible : Visibility.Collapsed;
        _testRead.IsEnabled = ready;
        _testSmtp.IsEnabled = ready && _sendEnabled.IsOn;
        _sendTest.IsEnabled = ready;
        _sendTest.Visibility = _sendEnabled.IsOn ? Visibility.Visible : Visibility.Collapsed;
        _microsoftSignIn.IsEnabled = ready;
    }

    private async Task LoadAccountsAsync()
    {
        var selected = _accountId;
        var result = await _engine.ManageAsync("list_accounts", new { }, _lifetime.Token);
        _accounts.Clear();
        _accounts.AddRange(result.EnumerateArray().Select(account => account.Clone()));
        _accountsLoaded = true;
        _creatingAccount = false;
        PopulateAccountPicker(selected);
    }

    private void PopulateAccountPicker(string selected)
    {
        _loadingForm = true;
        _accountPicker.Items.Clear();
        foreach (var account in _accounts)
            _accountPicker.Items.Add(new ComboBoxItem { Content = $"{Value(account, "label")} · {Value(account, "email")}", Tag = Value(account, "id") });
        _loadingForm = false;
        var index = _accounts.FindIndex(account => Value(account, "id") == selected);
        _accountPicker.SelectedIndex = index >= 0 ? index : _accounts.Count > 0 ? 0 : -1;
        if (_accounts.Count == 0) FillAccount(null);
    }

    private void FillAccount(JsonElement? account)
    {
        _loadingForm = true;
        if (account is not null) _creatingAccount = false;
        _accountId = account is { } value ? Value(value, "id") : "";
        _revision = account is { } current ? current.GetProperty("revision").GetInt32() : null;
        _preset.SelectedIndex = 0;
        _label.Text = account is { } name ? Value(name, "label") : "";
        _email.Text = account is { } email ? Value(email, "email") : "";
        var authentication = account?.GetProperty("authentication");
        var microsoft = authentication is { } method && Value(method, "method") == "microsoft_oauth";
        _clientId.Text = microsoft ? Value(authentication!.Value, "client_id") : "";
        var tenant = microsoft ? Value(authentication!.Value, "tenant") : "common";
        _tenant.SelectedIndex = tenant switch { "common" => 0, "organizations" => 1, "consumers" => 2, _ => 3 };
        _tenantId.Text = _tenant.SelectedIndex == 3 ? tenant : "";
        var connection = account?.GetProperty("incoming");
        _incomingProtocol.SelectedIndex = connection is { } protocol && Value(protocol, "protocol") == "pop3" ? 1 : 0;
        _incomingHost.Text = connection is { } incoming ? Value(incoming, "host") : "";
        _incomingPort.Text = connection is { } incomingPort ? incomingPort.GetProperty("port").ToString() : "993";
        _incomingTls.SelectedIndex = connection is { } incomingTls && Value(incomingTls, "tls") == "starttls" ? 1 : 0;
        _incomingUser.Text = connection is { } incomingUser ? Value(incomingUser, "username") : "";
        _incomingPassword.Password = "";
        _incomingPassword.PlaceholderText = account is null ? "Required for a new account" : "Leave blank to keep the saved password";
        _folders.Text = account is { } folders ? string.Join(", ", folders.GetProperty("folders").EnumerateArray().Select(folder => folder.GetString())) : "INBOX";
        _readEnabled.IsOn = account is { } readable && readable.GetProperty("read_enabled").GetBoolean();
        var smtp = account?.GetProperty("smtp");
        var hasSmtp = smtp is { ValueKind: JsonValueKind.Object };
        _smtpHost.Text = hasSmtp ? Value(smtp!.Value, "host") : "";
        _smtpPort.Text = hasSmtp ? smtp!.Value.GetProperty("port").ToString() : "465";
        _smtpTls.SelectedIndex = hasSmtp && Value(smtp!.Value, "tls") == "starttls" ? 1 : 0;
        _smtpUser.Text = hasSmtp ? Value(smtp!.Value, "username") : "";
        _smtpPassword.Password = "";
        _smtpPassword.PlaceholderText = hasSmtp ? "Leave blank to keep the saved password" : "Enter the SMTP password";
        _configureSmtp.IsOn = hasSmtp;
        _smtpFields.Visibility = hasSmtp ? Visibility.Visible : Visibility.Collapsed;
        _sendEnabled.IsOn = account is { } sendable && sendable.GetProperty("send_enabled").GetBoolean();
        UpdateAuthenticationChoices();
        _authentication.SelectedIndex = microsoft ? 1 : 0;
        _save.IsEnabled = true;
        _loadingForm = false;
        UpdateIncomingPresentation();
        UpdateAuthenticationPresentation(clearPasswords: false);
        _hasUnsavedChanges = false;
        UpdateAccountPresentation();
    }

    private void ApplyPreset()
    {
        if (_loadingForm) return;
        if (_preset.SelectedIndex == 0)
        {
            UpdateAuthenticationChoices();
            return;
        }
        _loadingForm = true;
        if (_preset.SelectedIndex == 2 && _incomingProtocol.SelectedIndex == 1)
        {
            _incomingProtocol.SelectedIndex = 0;
            ShowNotice("iCloud uses IMAP", "The iCloud preset uses IMAP for incoming mail. POP3 is not available with this preset.", InfoBarSeverity.Informational);
        }
        var pop = _incomingProtocol.SelectedIndex == 1;
        _incomingHost.Text = _preset.SelectedIndex switch
        {
            1 => pop ? "pop.gmail.com" : "imap.gmail.com",
            2 => "imap.mail.me.com",
            3 or 4 => "outlook.office365.com",
            _ => ""
        };
        _incomingPort.Text = pop ? "995" : "993";
        _incomingTls.SelectedIndex = 0;
        _smtpHost.Text = _preset.SelectedIndex switch
        {
            1 => "smtp.gmail.com",
            2 => "smtp.mail.me.com",
            3 => "smtp-mail.outlook.com",
            4 => "smtp.office365.com",
            _ => ""
        };
        _smtpPort.Text = _preset.SelectedIndex == 1 ? "465" : "587";
        _smtpTls.SelectedIndex = _preset.SelectedIndex == 1 ? 0 : 1;
        _loadingForm = false;
        UpdateIncomingPresentation();
        UpdateAuthenticationChoices();
        if (_preset.SelectedIndex is 3 or 4)
            ShowNotice("Microsoft cloud accounts require modern authentication", "Outlook.com requires modern authentication. Exchange Online removed Basic authentication for IMAP and POP3. Choose Microsoft sign-in and supply your app registration for these services; tenant policies can still restrict access. AgentInbox never switches authentication automatically.", InfoBarSeverity.Warning);
    }

    private void IncomingProtocolChanged()
    {
        if (_loadingForm) return;
        _incomingPort.Text = _incomingProtocol.SelectedIndex == 1 ? "995" : "993";
        _incomingTls.SelectedIndex = 0;
        if (_preset.SelectedIndex > 0) ApplyPreset();
        else
        {
            UpdateIncomingPresentation();
            UpdateAuthenticationChoices();
        }
    }

    private bool UsesMicrosoftMailServers()
    {
        var incoming = _incomingHost.Text.Trim();
        var protocolHost = _incomingProtocol.SelectedIndex == 1 ? "pop-mail.outlook.com" : "imap-mail.outlook.com";
        var incomingMatches = incoming.Equals("outlook.office365.com", StringComparison.OrdinalIgnoreCase)
            || incoming.Equals(protocolHost, StringComparison.OrdinalIgnoreCase);
        var smtp = _smtpHost.Text.Trim();
        return incomingMatches && (!_configureSmtp.IsOn
            || smtp.Equals("smtp.office365.com", StringComparison.OrdinalIgnoreCase)
            || smtp.Equals("smtp-mail.outlook.com", StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateAuthenticationChoices()
    {
        var allowMicrosoft = _preset.SelectedIndex is not (1 or 2) && UsesMicrosoftMailServers();
        var selectedMicrosoft = _authentication.SelectedIndex == 1;
        if (allowMicrosoft && _authentication.Items.Count == 1)
            _authentication.Items.Add("Microsoft sign-in (OAuth2)");
        else if (!allowMicrosoft && _authentication.Items.Count == 2)
        {
            _authentication.Items.RemoveAt(1);
            if (selectedMicrosoft)
            {
                // Changing servers invalidates the choice; do not silently switch authentication.
                _authentication.SelectedIndex = -1;
                if (!_loadingForm)
                    ShowNotice("Choose authentication for this server", "The selected servers do not support Microsoft sign-in. Choose password or app password before saving.", InfoBarSeverity.Informational);
            }
        }
        _authenticationHint.Text = _preset.SelectedIndex switch
        {
            1 => "Use a Gmail app password. Availability depends on your Google account settings.",
            2 => "Use an app-specific password for iCloud.",
            _ when allowMicrosoft => "Choose Microsoft sign-in explicitly for Outlook.com or Microsoft 365. It requires your public-client app registration.",
            _ => "Use the password or app password required by your mail provider. A preset fills in the server settings."
        };
        if (!_loadingForm) UpdateAuthenticationPresentation(clearPasswords: false);
    }

    private void UpdateIncomingPresentation()
    {
        var pop = _incomingProtocol.SelectedIndex == 1;
        _folders.IsEnabled = !pop;
        if (pop) _folders.Text = "INBOX";
        _folderHint.Text = pop
            ? "POP3 exposes one mailbox as INBOX. It has no server folders or unread flags. AgentInbox leaves messages on the server."
            : "Separate exact folder names with commas. Use ASCII folder names, such as INBOX. Reading does not mark messages as read.";
        _incomingHost.Header = pop ? "POP3 server" : "IMAP server";
        _incomingHost.PlaceholderText = pop ? "pop.example.com" : "imap.example.com";
        _incomingPassword.Header = pop ? "POP3 password" : "IMAP password";
        _testRead.Content = pop ? "Test POP3 reading" : "Test IMAP reading";
        AutomationProperties.SetName(_testRead, (string)_testRead.Content);
        ToolTipService.SetToolTip(_testRead, pop
            ? "Check saved POP3 settings by reading mail without deleting messages."
            : "Check saved IMAP settings by reading mail without marking messages as read.");
    }

    private void UpdateAuthenticationPresentation(bool clearPasswords)
    {
        var microsoft = _authentication.SelectedIndex == 1;
        _microsoftFields.Visibility = microsoft ? Visibility.Visible : Visibility.Collapsed;
        _incomingPassword.IsEnabled = !microsoft;
        _smtpPassword.IsEnabled = !microsoft;
        if (microsoft && clearPasswords)
        {
            _incomingPassword.Password = "";
            _smtpPassword.Password = "";
        }
        if (microsoft)
        {
            _incomingPassword.PlaceholderText = "Uses your Microsoft sign-in";
            _smtpPassword.PlaceholderText = "Uses your Microsoft sign-in";
        }
        else
        {
            _incomingPassword.PlaceholderText = _accountId.Length == 0 ? "Required for a new account" : "Enter a password or keep the saved password";
            _smtpPassword.PlaceholderText = "Enter a password or keep the saved password";
        }
    }

    private async Task SaveAccountAsync()
    {
        if (_authentication.SelectedIndex < 0)
            throw new EngineException("Choose an authentication method for the selected mail servers.");
        if (_authentication.SelectedIndex == 1 && !UsesMicrosoftMailServers())
            throw new EngineException("Microsoft sign-in is available only for Outlook.com and Microsoft 365 mail servers.");
        var pop = _incomingProtocol.SelectedIndex == 1;
        var folders = pop ? new[] { "INBOX" } : _folders.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (folders.Length == 0 || folders.Any(folder => folder.Any(character => character < 32 || character > 126)))
            throw new EngineException("Enter at least one ASCII folder name, such as INBOX.");
        var passwords = new Dictionary<string, string> { ["incoming"] = "", ["smtp"] = "" };
        var authentication = new Dictionary<string, string> { ["method"] = "password" };
        if (_authentication.SelectedIndex == 1)
        {
            if (!Guid.TryParse(_clientId.Text.Trim(), out var clientId))
                throw new EngineException("Enter the Application (client) ID from your Microsoft app registration.");
            var tenant = _tenant.SelectedIndex switch { 0 => "common", 1 => "organizations", 2 => "consumers", _ => _tenantId.Text.Trim() };
            if (_tenant.SelectedIndex == 3 && !Guid.TryParse(tenant, out _))
                throw new EngineException("Enter your Microsoft directory's tenant GUID.");
            authentication = new() { ["method"] = "microsoft_oauth", ["client_id"] = clientId.ToString(), ["tenant"] = tenant };
            passwords["incoming"] = "";
            passwords["smtp"] = "";
        }
        else
        {
            if (_incomingPassword.Password.Length > 0) passwords["incoming"] = _incomingPassword.Password;
            if (_configureSmtp.IsOn && _smtpPassword.Password.Length > 0) passwords["smtp"] = _smtpPassword.Password;
        }
        var account = new
        {
            id = _accountId,
            label = _label.Text.Trim(),
            email = _email.Text.Trim(),
            authentication,
            incoming = Connection(_incomingHost, _incomingPort, _incomingTls, _incomingUser, pop ? "pop3" : "imap"),
            smtp = _configureSmtp.IsOn ? Connection(_smtpHost, _smtpPort, _smtpTls, _smtpUser) : null,
            read_enabled = _readEnabled.IsOn,
            send_enabled = _configureSmtp.IsOn && _sendEnabled.IsOn,
            folders
        };
        var saved = await _engine.ManageAsync("save_account", new { account, passwords, expected_revision = _revision }, _lifetime.Token);
        passwords.Clear();
        // The save response contains the committed public account, including its new revision.
        var savedId = Value(saved, "id");
        _accounts.RemoveAll(item => Value(item, "id") == savedId);
        _accounts.Add(saved.Clone());
        _creatingAccount = false;
        PopulateAccountPicker(savedId);
        ShowNotice("Account saved", "Your assistant can use only the access you enabled.", InfoBarSeverity.Success);
        ShowCleanup(saved);
    }

    private async Task RemoveAccountAsync()
    {
        RequireSavedAccount();
        var dialog = Dialog("Remove this account?", Text("This removes local connection settings and saved credentials. Mail on the server stays in place."), "Remove account");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var removed = await _engine.ManageAsync("remove_account", new { account_id = _accountId }, _lifetime.Token);
        _accounts.RemoveAll(item => Value(item, "id") == _accountId);
        _accountId = "";
        PopulateAccountPicker("");
        ShowNotice("Account removed", "The local connection is no longer available to your assistant.", InfoBarSeverity.Success);
        ShowCleanup(removed);
    }

    private async Task SignInMicrosoftAsync()
    {
        RequireSavedAccount();
        var saved = _accounts.First(account => Value(account, "id") == _accountId);
        var savedAuthentication = saved.GetProperty("authentication");
        if (Value(savedAuthentication, "method") != "microsoft_oauth")
            throw new EngineException("Save the Microsoft sign-in authentication choice before starting sign-in.");
        var selectedTenant = _tenant.SelectedIndex switch { 0 => "common", 1 => "organizations", 2 => "consumers", _ => _tenantId.Text.Trim() };
        if (_authentication.SelectedIndex != 1 ||
            !string.Equals(Value(savedAuthentication, "client_id"), _clientId.Text.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Value(savedAuthentication, "tenant"), selectedTenant, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Value(saved, "email"), _email.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new EngineException("Save your account and Microsoft app-registration changes before starting sign-in.");
        var start = await _engine.ManageAsync("start_microsoft_signin", new { account_id = _accountId }, _lifetime.Token);
        var flowId = Value(start, "flow_id");
        var verificationUri = MicrosoftVerificationUri(Value(start, "verification_uri"));
        var expiresIn = start.GetProperty("expires_in").GetInt32();
        var interval = start.GetProperty("interval").GetInt32();
        if (flowId.Length == 0 || expiresIn is < 1 or > 3600 || interval is < 1 or > 300)
            throw new EngineException("Microsoft returned an invalid sign-in flow. Start a new sign-in request.");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        using var flowCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var status = Text("Waiting for you to complete sign-in with Microsoft.");
        var userCode = new TextBox
        {
            Header = "Enter this code on Microsoft's page", Text = Value(start, "user_code"),
            IsReadOnly = true, FontSize = 24, FontFamily = new FontFamily("Consolas")
        };
        var openBrowser = Action("Open Microsoft sign-in", async () =>
        {
            try
            {
                var opened = await Windows.System.Launcher.LaunchUriAsync(verificationUri);
                status.Text = opened
                    ? "Complete sign-in in your browser. This window will wait for Microsoft's response."
                    : "Windows could not open the browser. Open the displayed Microsoft address manually.";
            }
            catch (Exception)
            {
                status.Text = "Windows could not open the browser. Open the displayed Microsoft address manually.";
            }
        }, accent: true);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "Sign in to Microsoft", CloseButtonText = "Cancel",
            CloseButtonStyle = DialogButtonStyle("Cancel Microsoft sign-in."),
            DefaultButton = ContentDialogButton.Close,
            Content = Stack(
                Hint("You chose Microsoft sign-in for this account. The browser opens only when you press the button below."),
                userCode, Text(verificationUri.AbsoluteUri), openBrowser, status,
                Hint($"This code expires at {deadline.ToLocalTime():t}. Sign-in does not test mail reading or sending."))
        };
        var connected = false;
        string? failure = null;
        JsonElement? cleanupResult = null;

        async Task PollAsync()
        {
            try
            {
                while (!flowCancellation.IsCancellationRequested)
                {
                    var remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero) throw new EngineException("The Microsoft sign-in code expired. Start sign-in again.");
                    await Task.Delay(remaining < TimeSpan.FromSeconds(interval) ? remaining : TimeSpan.FromSeconds(interval), flowCancellation.Token);
                    if (DateTimeOffset.UtcNow >= deadline) throw new EngineException("The Microsoft sign-in code expired. Start sign-in again.");
                    var result = await _engine.ManageAsync("poll_microsoft_signin", new { flow_id = flowId }, flowCancellation.Token);
                    switch (Value(result, "status"))
                    {
                        case "connected":
                            cleanupResult = result.Clone();
                            connected = true;
                            dialog.Hide();
                            return;
                        case "pending":
                            interval = result.GetProperty("interval").GetInt32();
                            if (interval is < 1 or > 300) throw new EngineException("Microsoft returned an invalid polling interval.");
                            break;
                        default:
                            throw new EngineException("Microsoft returned an unexpected sign-in status.");
                    }
                }
            }
            catch (OperationCanceledException) when (flowCancellation.IsCancellationRequested) { }
            catch (EngineException exception) { failure = exception.Message; dialog.Hide(); }
            catch (Exception)
            {
                failure = "Microsoft sign-in could not finish. Check the account and app registration before trying again.";
                dialog.Hide();
            }
        }

        using var windowCancellation = _lifetime.Token.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
        var polling = PollAsync();
        try { await dialog.ShowAsync(); }
        finally
        {
            flowCancellation.Cancel();
            await polling;
            if (!connected && !_lifetime.IsCancellationRequested)
            {
                try { cleanupResult = await _engine.ManageAsync("cancel_microsoft_signin", new { flow_id = flowId }, _lifetime.Token); }
                catch (EngineException exception)
                {
                    CleanupNotice.Message = "Local sign-in cleanup could not be confirmed. " + exception.Message;
                    CleanupNotice.IsOpen = true;
                }
            }
        }
        if (_lifetime.IsCancellationRequested) return;
        if (cleanupResult is { } cleanup) ShowCleanup(cleanup);
        if (failure is not null) throw new EngineException(failure);
        if (connected)
        {
            await LoadAccountsAsync();
            ShowNotice("Microsoft sign-in completed", "Test incoming mail reading and the optional SMTP connection separately.", InfoBarSeverity.Success);
        }
        else ShowNotice("Microsoft sign-in closed", "No completed connection was reported for this sign-in request. Authentication was not switched to a password.", InfoBarSeverity.Informational);
    }

    private static Uri MicrosoftVerificationUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            value.Contains('@') || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (uri.Host != "microsoft.com" && uri.Host != "www.microsoft.com") ||
            (uri.AbsolutePath != "/devicelogin" && uri.AbsolutePath != "/devicelogin/"))
            throw new EngineException("Microsoft returned an unexpected verification address. Sign-in was stopped.");
        return uri;
    }

    private async Task TestReadAsync()
    {
        RequireSavedAccount();
        var result = await _engine.ManageAsync("check_read", new { account_id = _accountId }, _lifetime.Token);
        var sampleRead = Value(result, "message_read") == "passed";
        var saved = _accounts.First(account => Value(account, "id") == _accountId);
        var protocol = Value(saved.GetProperty("incoming"), "protocol").ToUpperInvariant();
        ShowNotice(sampleRead ? $"{protocol} reading succeeded" : $"{protocol} connection succeeded", SafeResult(result),
            sampleRead ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
    }

    private async Task TestSmtpAsync()
    {
        RequireSavedAccount();
        await _engine.ManageAsync("check_smtp", new { account_id = _accountId }, _lifetime.Token);
        ShowNotice("SMTP connection succeeded", "The server accepted authentication. No email was sent.", InfoBarSeverity.Success);
    }

    private async Task PrepareTestAsync()
    {
        RequireSavedAccount();
        var saved = _accounts.First(account => Value(account, "id") == _accountId);
        var recipient = Field("Recipient", "you@example.com");
        recipient.Text = Value(saved, "email");
        var dialog = Dialog("Prepare a test email", Stack(recipient, Hint("The next screen shows the exact email. It sends only after you approve it.")), "Review test email");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var result = await _engine.ManageAsync("prepare_email", new
        {
            account_id = _accountId, to = new[] { recipient.Text.Trim() }, cc = Array.Empty<string>(), bcc = Array.Empty<string>(),
            subject = "AgentInbox SMTP test", body = "This is a test email sent from AgentInbox after your explicit approval.", attachments = Array.Empty<string>()
        }, _lifetime.Token);
        var requestId = Value(result, "request_id");
        if (requestId.Length == 0) throw new EngineException("The engine did not return a review request.");
        RequestReview(requestId);
    }

    private async Task BuildOutgoingPageAsync()
    {
        Page.Children.Clear();
        Page.Children.Add(PageHeading("Outgoing mail", "Review the exact recipients, message and attachments before any email can leave.",
            IconAction("Refresh outgoing mail", "\uE72C", 0x2477C8, () => _ = RunAsync(BuildOutgoingPageAsync))));
        var requestLookup = Field("Check an earlier request", "Request ID");
        requestLookup.Text = _lastRequestId;
        Page.Children.Add(Section("Request status", requestLookup, Action("Check status", () => _ = RunAsync(async () =>
        {
            var result = await _engine.ManageAsync("get_send_status", new { request_id = requestLookup.Text.Trim() }, _lifetime.Token);
            ShowSendOutcome(result);
        }))));
        var requests = await _engine.ManageAsync("list_pending", new { }, _lifetime.Token);
        if (requests.GetArrayLength() == 0) Page.Children.Add(Hint("There are no outgoing requests. Ask your assistant to prepare an email, or send a test from Accounts."));
        else Page.Children.Add(Hint("The latest 100 requests are shown, including completed and expired requests."));
        foreach (var request in requests.EnumerateArray())
        {
            ShowCleanup(request);
            var id = Value(request, "id");
            var status = Value(request, "status");
            var content = Stack(Text($"Request {id}", 16), Text($"Status: {StatusLabel(status)}"), Hint($"Created: {Timestamp(request, "created")} · Expires: {Timestamp(request, "expires")}"));
            if (status == "pending_approval") content.Children.Add(Action("Review email…", () => RequestReview(id), accent: true));
            else
            {
                content.Children.Add(Hint("Only requests waiting for approval can be reviewed."));
                content.Children.Add(Action("Show result", () => _ = RunAsync(async () =>
                {
                    var result = await _engine.ManageAsync("get_send_status", new { request_id = id }, _lifetime.Token);
                    ShowSendOutcome(result);
                })));
            }
            Page.Children.Add(Section("", content));
        }
        Page.Children.Add(Hint("An SMTP acceptance means the server accepted responsibility for the email. It does not confirm delivery to the recipient. An uncertain send must be checked before preparing another request."));
    }

    private async Task BuildAssistantPageAsync()
    {
        Page.Children.Clear();
        Page.Children.Add(Heading("Connect your assistant", "Make your shared email accounts available in Codex."));
        Page.Children.Add(Hint("Checking Codex on this computer…"));
        var configuration = _engine.CreateClientConfiguration();
        var service = new AssistantSetupService(configuration);
        var clients = await service.DetectAsync(_lifetime.Token);
        var codex = clients.Single(client => client.ClientId == "codex");
        RenderAssistantPage(service, configuration, codex);
    }

    private void RenderAssistantPage(AssistantSetupService service, string configuration, AssistantClientStatus client)
    {
        Page.Children.Clear();
        Page.Children.Add(PageHeading("Connect your assistant", "Make your shared email accounts available in Codex.",
            IconAction("Check Codex again", "\uE72C", 0x2477C8, () => _ = RunAsync(BuildAssistantPageAsync))));
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(ColoredIcon("\uE71B", 0x8B62CF));
        title.Children.Add(Text(client.DisplayName, 20));
        var state = client.RequiresReplacement ? "Different AgentInbox settings found"
            : client.IsConfigured ? "Configuration saved"
            : !client.IsDetected ? "Codex not found"
            : client.CanConnect ? "Ready to connect" : "Setup needs attention";
        var status = Text(state);
        status.Foreground = IconBrush(client.IsConfigured ? 0x16856AU : client.CanConnect ? 0x2477C8U : 0xC77616U);
        var connect = Action(client.IsConfigured || client.RequiresReplacement ? "Update connection" : "Connect Codex",
            () => _ = RunAsync(() => ConnectAssistantAsync(service, configuration, client)), accent: true);
        connect.IsEnabled = client.CanConnect;
        AutomationProperties.SetName(connect, client.IsConfigured || client.RequiresReplacement ? "Update AgentInbox connection in Codex" : "Connect AgentInbox to Codex");
        Page.Children.Add(Section("", title, status, Text(client.Message), connect));
        Page.Children.Add(Section("Next step",
            Text("After saving, start a new conversation in Codex. Restart Codex if AgentInbox does not appear."),
            Hint("Ask Codex to list your shared accounts. Saving settings does not verify the connection or access to your mailbox.")));
        Page.Children.Add(Hint("Only accounts you share are available for reading. Every outgoing email still needs your approval in AgentInbox."));

        var path = new TextBox
        {
            Header = "Codex configuration file", Text = client.ConfigurationPath.Length > 0 ? client.ConfigurationPath : "Not available",
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var box = new TextBox
        {
            Text = configuration, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"), MinHeight = 200, MaxHeight = 360,
            Header = "Local MCP configuration", HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var copy = Action("Copy MCP configuration", () => _ = RunAsync(() =>
        {
            try
            {
                var package = new DataPackage();
                package.SetText(configuration);
                Clipboard.SetContent(package);
            }
            catch (Exception) { throw new EngineException("Windows could not copy the configuration. Try the clipboard action again."); }
            ShowNotice("Configuration copied", "Add the AgentInbox entry to your client's MCP settings, then start a new conversation.", InfoBarSeverity.Success);
            return Task.CompletedTask;
        }));
        Page.Children.Add(new Expander
        {
            Header = "Advanced setup", IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Stack(path, Hint("Use manual configuration if automatic setup is unavailable, or when connecting another MCP client. These settings contain paths, not passwords."), box, copy)
        });
    }

    private async Task ConnectAssistantAsync(AssistantSetupService service, string configuration, AssistantClientStatus client)
    {
        var replaceExisting = false;
        if (client.RequiresReplacement)
        {
            var confirmation = Dialog("Update AgentInbox in Codex?", Stack(
                Text("Codex already has different settings for AgentInbox."),
                Hint("Replace only the AgentInbox connection with this app's settings. Other MCP connections stay unchanged.")),
                "Replace AgentInbox connection");
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            replaceExisting = true;
        }
        AssistantSetupResult result;
        try { result = await service.ConnectAsync(client, _lifetime.Token, replaceExisting); }
        catch (InvalidOperationException exception) { throw new EngineException(exception.Message); }
        RenderAssistantPage(service, configuration, result.Status);
        ShowNotice(result.ConfigurationWritten ? "Codex configuration saved" : "Codex configuration unchanged",
            result.Message, result.CleanupPending ? InfoBarSeverity.Warning : InfoBarSeverity.Informational);
    }

    private async Task DrainReviewsAsync()
    {
        if (!_loaded || _busy || _reviewing || _lifetime.IsCancellationRequested) return;
        _reviewing = true;
        try
        {
            while (_reviewRequests.TryDequeue(out var id) && !_lifetime.IsCancellationRequested)
                await RunAsync(() => ReviewAsync(id));
        }
        finally { _reviewing = false; }
    }

    private async Task ReviewAsync(string requestId)
    {
        _lastRequestId = requestId;
        using var session = await _engine.OpenReviewAsync(requestId, _lifetime.Token);
        var preview = session.Preview;
        var content = Stack(
            Hint("Approval applies to this exact email once. Closing or cancelling never approves a send."),
            Hint("Request: " + requestId),
            Text("From: " + Value(preview, "from")),
            Text("To: " + Addresses(preview, "to")),
            Text("CC: " + Addresses(preview, "cc")),
            Text("BCC: " + Addresses(preview, "bcc")),
            Text("Subject: " + Value(preview, "subject"), 16),
            Text(Value(preview, "body")));
        foreach (var attachment in preview.GetProperty("attachments").EnumerateArray())
            content.Children.Add(Text($"Attachment: {Value(attachment, "name")} ({attachment.GetProperty("size")} bytes)\nSHA-256: {Value(attachment, "sha256")}"));
        content.Children.Add(Hint("Expires: " + Timestamp(preview, "expires")));
        var dialog = Dialog("Review outgoing email", new ScrollViewer
        {
            Content = content, MaxHeight = 520, MinWidth = 420,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        }, "Send this email");
        using var cancellation = _lifetime.Token.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
        var decision = await dialog.ShowAsync();
        if (_lifetime.IsCancellationRequested) return;
        // Only this direct native dialog result can authorize the private review channel.
        var result = await session.CompleteAsync(decision == ContentDialogResult.Primary, _lifetime.Token);
        ShowSendOutcome(result);
        if (Navigation.SelectedItem is NavigationViewItem { Tag: "outgoing" }) await BuildOutgoingPageAsync();
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        _busy = true;
        Navigation.IsEnabled = false;
        CleanupRetryButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Notice.IsOpen = false;
        try { await operation(); }
        catch (EngineException exception) { ShowNotice("Could not complete this action", exception.Message + ReviewFailureHint(), InfoBarSeverity.Error); }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            ShowNotice("The operation timed out", "Check the saved settings and server connection. If this happened during a send, check its status before preparing another email." + ReviewFailureHint(), InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            // Report code locations only; exception messages can contain account or provider data.
            var locations = new System.Diagnostics.StackTrace(exception, false).GetFrames()
                .Select(frame => frame.GetMethod())
                .Where(method => method?.DeclaringType?.Namespace?.StartsWith("MailMeUp.Desktop", StringComparison.Ordinal) == true)
                .Take(8).Select(method => $"{method!.DeclaringType!.Name}.{method.Name}");
            Console.Error.WriteLine($"Setup error: {exception.GetType().Name} (0x{exception.HResult:X8}); {string.Join("; ", locations)}");
            ShowNotice("Could not complete this action", $"AgentInbox encountered an unexpected error ({exception.GetType().Name}, 0x{exception.HResult:X8}). No automatic retry was made." + ReviewFailureHint(), InfoBarSeverity.Error);
        }
        finally
        {
            _busy = false;
            Navigation.IsEnabled = true;
            CleanupRetryButton.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
        }
        if (!_reviewing) await DrainReviewsAsync();
    }

    private void RequireSavedAccount()
    {
        if (_accountId.Length == 0) throw new EngineException("Save an account before using this action.");
    }

    private string ReviewFailureHint() => _reviewing && _lastRequestId.Length > 0
        ? $" Use Outgoing mail to check request {_lastRequestId}." : "";

    private void ShowNotice(string title, string message, InfoBarSeverity severity)
    {
        Notice.Title = title;
        Notice.Message = message;
        Notice.Severity = severity;
        Notice.IsOpen = true;
    }

    private void ShowCleanup(JsonElement result, bool clearWhenComplete = false)
    {
        if (!result.TryGetProperty("cleanup_pending", out var pending) ||
            pending.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return;
        if (!pending.GetBoolean())
        {
            // Only the aggregate retry result can clear warnings from other accounts or requests.
            if (clearWhenComplete) CleanupNotice.IsOpen = false;
            return;
        }
        var message = "Protected local data is still waiting to be removed. Retry cleanup when secure storage is available.";
        if (result.TryGetProperty("cleanup_error", out var error) && error.ValueKind == JsonValueKind.String &&
            error.GetString() is { Length: > 0 and <= 2048 } detail) message += " " + detail;
        CleanupNotice.Message = message;
        CleanupNotice.IsOpen = true;
    }

    private async void RetryCleanup_Click(object sender, RoutedEventArgs args)
    {
        await RunAsync(async () =>
        {
            var result = await _engine.ManageAsync("retry_cleanup", new { }, _lifetime.Token);
            ShowCleanup(result, clearWhenComplete: true);
            if (!result.GetProperty("cleanup_pending").GetBoolean())
                ShowNotice("Local cleanup completed", "The engine reports no remaining queued cleanup.", InfoBarSeverity.Success);
        });
    }

    private void ShowSendOutcome(JsonElement result)
    {
        var status = Value(result, "status");
        var id = Value(result, "request_id");
        if (id.Length == 0) id = Value(result, "id");
        var message = status switch
        {
            "accepted_by_server" => "The SMTP server accepted the email. Delivery to the recipient has not been confirmed.",
            "outcome_unknown" => "The connection ended without a definite result. Check your mailbox and provider before preparing another request. No automatic retry was made.",
            "rejected" => "The email was not accepted for sending. Check your connection settings before preparing a new request.",
            "cancelled" => "This request was cancelled without sending.",
            "expired" => "This approval request expired. Prepare a new message to continue.",
            "submitting" => "The request is being submitted or ended during submission. Check its status before preparing another email.",
            "pending_approval" => "The message is waiting for your approval. Nothing has been sent.",
            _ => "The engine returned an unrecognized state. Check the request before preparing another email."
        };
        var severity = status switch
        {
            "accepted_by_server" => InfoBarSeverity.Success,
            "outcome_unknown" or "rejected" or "submitting" => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Informational
        };
        ShowNotice(StatusLabel(status), message + (id.Length > 0 ? $" Request: {id}" : ""), severity);
        ShowCleanup(result);
    }

    private ContentDialog Dialog(string title, UIElement content, string primary) => new()
    {
        XamlRoot = Root.XamlRoot, Title = title, Content = content, PrimaryButtonText = primary,
        CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        PrimaryButtonStyle = DialogButtonStyle(primary),
        CloseButtonStyle = DialogButtonStyle("Cancel and close this dialog.")
    };

    private static Style DialogButtonStyle(string tooltip)
    {
        var style = new Style(typeof(Button))
        {
            BasedOn = (Style)Microsoft.UI.Xaml.Application.Current.Resources["DefaultButtonStyle"]
        };
        style.Setters.Add(new Setter(ToolTipService.ToolTipProperty, tooltip));
        return style;
    }

    private static Dictionary<string, object> Connection(TextBox host, TextBox port, ComboBox tls, TextBox username, string? protocol = null)
    {
        if (!int.TryParse(port.Text, out var number) || number is < 1 or > 65535)
            throw new EngineException("The server port must be a number from 1 to 65535.");
        var connection = new Dictionary<string, object>
        {
            ["host"] = host.Text.Trim(), ["port"] = number,
            ["tls"] = tls.SelectedIndex == 1 ? "starttls" : "tls", ["username"] = username.Text.Trim()
        };
        if (protocol is not null) connection["protocol"] = protocol;
        return connection;
    }

    private static string Value(JsonElement element, string property) => element.TryGetProperty(property, out var value)
        ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString() : "";

    private static string Addresses(JsonElement element, string property)
    {
        var value = string.Join(", ", element.GetProperty(property).EnumerateArray().Select(address => address.GetString()));
        return value.Length == 0 ? "(none)" : value;
    }

    private static string SafeResult(JsonElement result) => result.TryGetProperty("message", out var message)
        ? message.GetString() ?? "The operation completed." : "The operation completed.";

    private static string StatusLabel(string status) => status switch
    {
        "pending_approval" => "Waiting for your approval",
        "accepted_by_server" => "Accepted by the SMTP server",
        "outcome_unknown" => "Sending outcome unknown",
        "submitting" => "Submission in progress",
        "cancelled" => "Cancelled",
        "expired" => "Expired",
        "rejected" => "Rejected",
        _ => "Unknown request state"
    };

    private static string Timestamp(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds((long)value.GetDouble()).ToLocalTime().ToString("g")
            : value.ToString();
    }

    private static TextBlock Text(string value, double size = 14) => new()
    { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size, IsTextSelectionEnabled = true };

    private static TextBlock Hint(string value) => new()
    { Text = value, TextWrapping = TextWrapping.Wrap, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SubtleText"] };

    private static StackPanel Heading(string title, string subtitle) => Stack(
        new TextBlock { Text = title, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SectionTitle"] }, Hint(subtitle));

    private static Grid PageHeading(string title, string subtitle, FrameworkElement action)
    {
        var header = new Grid { ColumnSpacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = Heading(title, subtitle);
        header.Children.Add(heading);
        action.HorizontalAlignment = HorizontalAlignment.Right;
        action.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(action, 1);
        header.Children.Add(action);
        header.SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width < 620;
            Grid.SetColumnSpan(heading, compact ? 2 : 1);
            Grid.SetRow(action, compact ? 1 : 0);
            Grid.SetColumn(action, compact ? 0 : 1);
            Grid.SetColumnSpan(action, compact ? 2 : 1);
            action.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            action.Margin = compact ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        };
        return header;
    }

    private static SolidColorBrush IconBrush(uint rgb) => new(Windows.UI.Color.FromArgb(
        255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF)));

    private static FontIcon ColoredIcon(string glyph, uint color) => new()
    {
        Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 18,
        Foreground = IconBrush(color), Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center
    };

    private static Button IconAction(string name, string glyph, uint color, Action handler, string? tooltip = null)
    {
        var button = new Button
        {
            Content = ColoredIcon(glyph, color), Width = 36, Height = 36,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["QuietButton"],
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, tooltip ?? name);
        button.Click += (_, _) => handler();
        return button;
    }

    private static ComboBoxItem PresetItem(string name, string glyph, uint color)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        content.Children.Add(ColoredIcon(glyph, color));
        content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var item = new ComboBoxItem { Content = content };
        AutomationProperties.SetName(item, name);
        return item;
    }

    private static TabViewItem AccountTab(string name, string glyph, FrameworkElement content)
    {
        content.Margin = new Thickness(0, 20, 0, 0);
        return new TabViewItem
        {
            Header = name, IsClosable = false, Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            IconSource = new FontIconSource
            {
                Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons")
            }
        };
    }

    private static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static StackPanel Section(string title, params UIElement[] children)
    {
        var content = Stack();
        if (title.Length > 0) content.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var child in children) content.Children.Add(child);
        return content;
    }

    private static Grid Pair(FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        grid.SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width < 560;
            Grid.SetColumnSpan(left, compact ? 2 : 1);
            Grid.SetRow(right, compact ? 1 : 0);
            Grid.SetColumn(right, compact ? 0 : 1);
            Grid.SetColumnSpan(right, compact ? 2 : 1);
            grid.RowSpacing = compact ? 12 : 0;
        };
        return grid;
    }

    private static TextBox Field(string label, string placeholder) => new()
    { Header = label, PlaceholderText = placeholder, HorizontalAlignment = HorizontalAlignment.Stretch, MaxLength = 1024 };

    private static PasswordBox Password(string label) => new()
    { Header = label, PasswordRevealMode = PasswordRevealMode.Peek, MaxLength = 1024, HorizontalAlignment = HorizontalAlignment.Stretch };

    private static ComboBox TlsField()
    {
        var field = new ComboBox { Header = "Connection security", HorizontalAlignment = HorizontalAlignment.Stretch };
        field.Items.Add("TLS — encrypted from connection start");
        field.Items.Add("STARTTLS — require encryption before signing in");
        field.SelectedIndex = 0;
        return field;
    }

    private static Button Action(string caption, Action handler, bool accent = false, string? tooltip = null)
    {
        var button = new Button { Content = caption };
        if (accent) button.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        AutomationProperties.SetName(button, caption);
        ToolTipService.SetToolTip(button, tooltip ?? caption);
        button.Click += (_, _) => handler();
        return button;
    }

    private static Grid Actions(params Button[] buttons)
    {
        var panel = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var index = 0; index < buttons.Length; index++)
        {
            if (index % 2 == 0) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(buttons[index], index / 2);
            Grid.SetColumn(buttons[index], index % 2);
            panel.Children.Add(buttons[index]);
        }
        return panel;
    }
}
