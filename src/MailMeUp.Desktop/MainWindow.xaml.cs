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

/// <summary>Configures email accounts and assistant access on Windows.</summary>
public sealed partial class MainWindow : Window
{
    private readonly PythonEngine _engine;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<JsonElement> _accounts = [];
    private bool _loaded;
    private bool _busy;
    private bool _loadingForm;
    private string _accountId = "";
    private int? _revision;
    private ListView _accountPicker = new();
    private TabView _accountTabs = new();
    private StackPanel _accountToolbar = new();
    private Grid _accountWorkspace = new();
    private StackPanel _accountEditor = new();
    private TextBlock _accountSelectionHint = new();
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
    private ListView _folders = new();
    private Button _checkConnection = new();
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
    private TextBlock _saveStatus = new();
    private Button _testRead = new();
    private StackPanel _incomingChecks = new();
    private StackPanel _smtpChecks = new();
    private Button _testSmtp = new();

    internal MainWindow(PythonEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        SystemBackdrop = new DesktopAcrylicBackdrop();
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (dpi == 0) throw new InvalidOperationException("The window display scale could not be determined.");
        var scale = dpi / 96.0;
        // AppWindow uses physical pixels; size the form in logical pixels at the monitor's scale.
        var width = Math.Min((int)Math.Round(1280 * scale), area.Width);
        var height = Math.Min((int)Math.Round(960 * scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2, width, height));
        SaveBar.SizeChanged += (_, args) =>
            PageActions.Orientation = args.NewSize.Width < 480 ? Orientation.Vertical : Orientation.Horizontal;
        Closed += (_, _) => _lifetime.Cancel();
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr window);

    private void Root_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        Navigation.SelectedItem = Navigation.MenuItems[0];
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
            PageActions.Children.Clear();
            SaveBar.Visibility = Visibility.Collapsed;
            PageScroll.ChangeView(0, 0, null, disableAnimation: true);
            try
            {
                switch (page)
                {
                    case "accounts": BuildAccountsPage(); await LoadAccountsAsync(); break;
                    case "assistant": await BuildAssistantPageAsync(); break;
                    default: throw new InvalidOperationException("Unknown setup page.");
                }
            }
            catch
            {
                // Never leave a partially constructed form with actionable Save or Cancel buttons.
                Page.Children.Clear();
                PageActions.Children.Clear();
                SaveBar.Visibility = Visibility.Collapsed;
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
        Page.Children.Add(Heading("Email accounts", "Choose the mailboxes your assistant can use."));
        _saveStatus = Hint("");
        _saveStatus.VerticalAlignment = VerticalAlignment.Center;
        PageActions.Children.Add(_save);
        PageActions.Children.Add(_cancelAccount);
        PageActions.Children.Add(_saveStatus);
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
        _accountPicker = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single, MaxHeight = 480,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(_accountPicker, "Saved connections");
        _accountPicker.SelectionChanged += (_, _) =>
        {
            if (_loadingForm || _accountPicker.SelectedItem is not ListViewItem { Tag: string id }) return;
            var selected = _accounts.FirstOrDefault(account => Value(account, "id") == id);
            if (selected.ValueKind != JsonValueKind.Undefined) FillAccount(selected);
        };
        var accountActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom };
        accountActions.Children.Add(Action("Add connection", StartNewAccount,
            tooltip: "Open the setup form for another email account."));
        _removeAccount = IconAction("Remove account", "\uE74D", 0xD83B35, () => _ = RunAsync(RemoveAccountAsync),
            "Remove this account's local settings and credentials. Messages on the server stay in place.");
        accountActions.Children.Add(_removeAccount);
        accountActions.Children.Add(IconAction("Refresh accounts", "\uE72C", 0x2477C8, () => _ = RunAsync(LoadAccountsAsync),
            "Reload saved accounts and their settings."));
        _accountToolbar = Stack(Text("Connections", 22), accountActions, _accountPicker);
        _accountToolbar.SizeChanged += (_, args) =>
            accountActions.Orientation = args.NewSize.Width < 260 ? Orientation.Vertical : Orientation.Horizontal;
        _accountWorkspace = new Grid { ColumnSpacing = 24, RowSpacing = 20 };
        _accountWorkspace.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _accountWorkspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _accountWorkspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _accountWorkspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _accountWorkspace.Children.Add(_accountToolbar);
        _accountWorkspace.SizeChanged += (_, _) => UpdateAccountLayout();
        Page.Children.Add(_accountWorkspace);

        _preset = new ComboBox { Header = "Server preset", HorizontalAlignment = HorizontalAlignment.Stretch };
        _preset.Items.Add(PresetItem("Custom mail server", "\uE713", 0x667789));
        _preset.Items.Add(PresetItem("Gmail", "\uE715", 0xD94235));
        _preset.Items.Add(PresetItem("iCloud", "\uE753", 0x3282CF));
        _preset.Items.Add(PresetItem("Outlook.com", "\uE715", 0x1274C4));
        _preset.Items.Add(PresetItem("Microsoft 365", "\uE71D", 0x8661C5));
        _preset.SelectionChanged += (_, _) => ApplyPreset();
        _label = Field("Connection name", "Personal, work, or a name you recognize");
        AutomationProperties.SetName(_label, "Connection name");
        ToolTipService.SetToolTip(_label, "Rename this connection, then choose Save changes.");
        _email = Field("Email address", "you@example.com");
        _email.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_incomingUser.Text)) _incomingUser.Text = _email.Text.Trim();
            if (string.IsNullOrWhiteSpace(_smtpUser.Text)) _smtpUser.Text = _email.Text.Trim();
        };
        _authentication = new ComboBox { Header = "How to connect", PlaceholderText = "Choose how to connect", HorizontalAlignment = HorizontalAlignment.Stretch };
        _authentication.Items.Add("Password — account or app password");
        _authentication.SelectionChanged += (_, _) =>
        {
            if (!_loadingForm) UpdateAuthenticationPresentation(clearPasswords: true);
        };
        _clientId = Field("Microsoft app ID", "Application (client) ID from Microsoft");
        _tenant = new ComboBox { Header = "Microsoft account type", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var name in new[] { "Personal and work accounts (common)", "Work or school accounts (organizations)", "Personal accounts (consumers)", "Specific tenant ID" })
            _tenant.Items.Add(name);
        _tenantId = Field("Tenant ID", "Directory (tenant) GUID");
        _tenant.SelectionChanged += (_, _) => _tenantId.Visibility = _tenant.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        _microsoftSignIn = Action("Start web authorization…", () => _ = RunAsync(SignInMicrosoftAsync));
        _microsoftFields = Stack(_clientId, _tenant, _tenantId,
            Hint("You need a Microsoft app ID that allows sign-in on this device and your account type. The ID is not a password. AgentInbox cannot create or check it for you."),
            _microsoftSignIn,
            Hint("Save this choice before starting web authorization. It applies to incoming mail and, if enabled, sending. A wrong password never switches to web authorization automatically."));
        _authenticationHint = Hint("");
        var accountContent = Section("", _preset, _email, _authentication, _microsoftFields, _authenticationHint);

        _incomingProtocol = new ComboBox { Header = "Incoming protocol", HorizontalAlignment = HorizontalAlignment.Stretch };
        _incomingProtocol.Items.Add("IMAP — mail and server folders");
        _incomingProtocol.Items.Add("POP3 — mailbox messages");
        _incomingProtocol.SelectionChanged += (_, _) => IncomingProtocolChanged();
        _incomingHost = Field("Incoming server", "imap.example.com");
        _incomingPort = Field("Port", "993");
        _incomingTls = TlsField();
        _incomingUser = Field("Username", "Usually your full email address");
        _incomingPassword = Password("Incoming password");
        _folders = new ListView { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 220, MinHeight = 48 };
        AutomationProperties.SetName(_folders, "Folders available to your assistant");
        _checkConnection = Action("Check connection and get folders", () => _ = RunAsync(() => CheckAccountAsync(saveAccount: false)),
            tooltip: "Check the settings and password currently in this form, then choose folders from the server. Nothing is saved or sent.");
        _folderHint = Hint("");
        _readEnabled = new ToggleSwitch { Header = "Share this account with my assistant", OnContent = "Enabled", OffContent = "Disabled" };
        var incomingSettings = Section("", _incomingProtocol, Pair(_incomingHost, _incomingPort), _incomingTls,
            Pair(_incomingUser, _incomingPassword), _checkConnection, Text("Folders available to your assistant"), _folders, _folderHint,
            Hint("Passwords and Microsoft web authorization credentials stay in Windows Credential Manager. Your assistant's settings contain neither."), _readEnabled);
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
            tooltip: "Check the SMTP settings and password currently in this form without saving or sending an email.");
        _smtpChecks = Stack(_testSmtp, Hint("Check the connection and sign-in without saving settings or sending an email."));
        _smtpFields = Stack(Pair(_smtpHost, _smtpPort), _smtpTls, Pair(_smtpUser, _smtpPassword), _sendEnabled,
            Hint("When enabled, Codex can send email from this account when you ask it to."), _smtpChecks);
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
            AccountTab("Account", "\uE77B", 0xB58ADA, accountContent),
            AccountTab("Incoming", "\uE715", 0x409BE0, incomingContent),
            AccountTab("Outgoing", "\uE724", 0x2BAF90, outgoingContent) })
            _accountTabs.TabItems.Add(tab);
        _accountTabs.SelectionChanged += (_, _) => UpdateAccountTabHeaders();
        _accountTabs.SelectedIndex = 0;
        UpdateAccountTabHeaders();
        _newAccountHeading = Text("New account", 22);
        _accountEditor = Stack(_newAccountHeading, _label, _accountTabs);
        _accountSelectionHint = Hint("Select a connection to edit its name and settings, or add a new connection.");
        _accountWorkspace.Children.Add(_accountEditor);
        _accountWorkspace.Children.Add(_accountSelectionHint);
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
        _accountPicker.SelectedIndex = index;
        if (index < 0) FillAccount(null);
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
        _accountSelectionHint.Visibility = _accountsLoaded && _accounts.Count > 0 && !editing ? Visibility.Visible : Visibility.Collapsed;
        SaveBar.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _cancelAccount.Visibility = _creatingAccount ? Visibility.Visible : Visibility.Collapsed;
        _newAccountHeading.Visibility = _creatingAccount ? Visibility.Visible : Visibility.Collapsed;
        _removeAccount.IsEnabled = _accountId.Length > 0;
        _save.Content = _creatingAccount ? "Create account" : "Save changes";
        AutomationProperties.SetName(_save, (string)_save.Content);
        ToolTipService.SetToolTip(_save, _creatingAccount
            ? "Check this new account, choose folders and store its credentials securely. Microsoft web authorization requires saving first."
            : "Save changes to the selected account and its credentials.");
        UpdateAccountLayout();
        UpdateAccountChecks();
    }

    /// <summary>Keeps the saved connection list beside the editor, stacking it on smaller windows.</summary>
    private void UpdateAccountLayout()
    {
        var hasList = _accountToolbar.Visibility == Visibility.Visible;
        var sideBySide = hasList && _accountWorkspace.ActualWidth >= 960;
        _accountWorkspace.ColumnDefinitions[0].Width = sideBySide ? new GridLength(280) : new GridLength(0);
        _accountWorkspace.ColumnSpacing = sideBySide ? 24 : 0;
        Grid.SetColumnSpan(_accountToolbar, sideBySide ? 1 : 2);
        _accountPicker.MaxHeight = sideBySide ? 480 : 180;
        foreach (var content in new FrameworkElement[] { _accountEditor, _accountSelectionHint })
        {
            Grid.SetColumn(content, sideBySide ? 1 : 0);
            Grid.SetColumnSpan(content, sideBySide ? 1 : 2);
            Grid.SetRow(content, hasList && !sideBySide ? 1 : 0);
        }
    }

    /// <summary>Emphasizes the selected tab without changing the native TabView appearance.</summary>
    private void UpdateAccountTabHeaders()
    {
        foreach (var tab in _accountTabs.TabItems.OfType<TabViewItem>())
            if (tab.Header is StackPanel header)
                foreach (var label in header.Children.OfType<TextBlock>())
                    label.FontWeight = ReferenceEquals(tab, _accountTabs.SelectedItem)
                        ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
    }

    private void TrackAccountChanges()
    {
        foreach (var field in new[] { _label, _email, _clientId, _tenantId, _incomingHost, _incomingPort, _incomingUser, _smtpHost, _smtpPort, _smtpUser })
            field.TextChanged += (_, _) => MarkAccountChanged();
        foreach (var field in new[] { _incomingPassword, _smtpPassword })
            field.PasswordChanged += (_, _) => MarkAccountChanged();
        foreach (var field in new[] { _preset, _authentication, _tenant, _incomingProtocol, _incomingTls, _smtpTls })
            field.SelectionChanged += (_, _) => MarkAccountChanged();
        foreach (var field in new[] { _readEnabled, _configureSmtp, _sendEnabled })
            field.Toggled += (_, _) => MarkAccountChanged();
        _folders.SelectionChanged += (_, _) => MarkAccountChanged();
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
        _save.IsEnabled = _creatingAccount || _hasUnsavedChanges;
        _saveStatus.Text = _creatingAccount ? "New connection" : _hasUnsavedChanges ? "Unsaved changes" : "All changes saved";
        _incomingChecks.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
        _smtpChecks.Visibility = _configureSmtp.IsOn ? Visibility.Visible : Visibility.Collapsed;
        _testRead.IsEnabled = ready;
        _testSmtp.IsEnabled = _configureSmtp.IsOn;
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
        {
            var name = Value(account, "label");
            var address = Value(account, "email");
            var title = new TextBlock { Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis };
            var subtitle = Hint(address);
            subtitle.TextWrapping = TextWrapping.NoWrap;
            subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            var content = Stack(title, subtitle);
            content.Spacing = 4;
            var item = new ListViewItem { Content = content, Tag = Value(account, "id"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 10, 12, 10) };
            AutomationProperties.SetName(item, $"{name}, {address}");
            ToolTipService.SetToolTip(item, $"{name}\n{address}");
            _accountPicker.Items.Add(item);
        }
        _loadingForm = false;
        var index = _accounts.FindIndex(account => Value(account, "id") == selected);
        _accountPicker.SelectedIndex = index;
        if (index < 0) FillAccount(null);
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
        string[] selectedFolders = account is { } folders
            ? folders.GetProperty("folders").EnumerateArray().Select(folder => folder.GetString()!).ToArray() : ["INBOX"];
        SetFolderChoices(selectedFolders, selectedFolders);
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
            ShowNotice("Use web authorization with Microsoft", "Outlook.com and Microsoft 365 may reject a regular password for mail access. Choose web authorization and enter your Microsoft app ID. Your work or school may still block this connection. AgentInbox never changes your choice automatically.", InfoBarSeverity.Warning);
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
            _authentication.Items.Add("Web authorization with Microsoft");
        else if (!allowMicrosoft && _authentication.Items.Count == 2)
        {
            _authentication.Items.RemoveAt(1);
            if (selectedMicrosoft)
            {
                // Changing servers invalidates the choice; do not silently switch authentication.
                _authentication.SelectedIndex = -1;
                if (!_loadingForm)
                    ShowNotice("Choose how to connect to this server", "Web authorization with Microsoft is only available for supported Microsoft mail servers. Choose a password or app password before saving.", InfoBarSeverity.Informational);
            }
        }
        _authenticationHint.Text = _preset.SelectedIndex switch
        {
            1 => "Use a Gmail app password. Availability depends on your Google account settings.",
            2 => "Use an app-specific password for iCloud.",
            _ when allowMicrosoft => "For Outlook.com or Microsoft 365, you can choose web authorization with Microsoft. You will need your Microsoft app ID.",
            _ => "Use the password or app password required by your mail provider. A preset fills in the server settings."
        };
        if (!_loadingForm) UpdateAuthenticationPresentation(clearPasswords: false);
    }

    private void UpdateIncomingPresentation()
    {
        var pop = _incomingProtocol.SelectedIndex == 1;
        _folders.IsEnabled = !pop;
        if (pop) SetFolderChoices(["INBOX"], ["INBOX"]);
        _folderHint.Text = pop
            ? "POP3 exposes one mailbox as INBOX. It has no server folders or unread flags. AgentInbox leaves messages on the server."
            : "Check the connection to load server folders, then choose between one and twenty. Only selected folders are shared when you save. Current support requires ASCII server names.";
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
            _incomingPassword.PlaceholderText = "Uses Microsoft web authorization";
            _smtpPassword.PlaceholderText = "Uses Microsoft web authorization";
        }
        else
        {
            _incomingPassword.PlaceholderText = _accountId.Length == 0 ? "Required for a new account" : "Enter a password or keep the saved password";
            _smtpPassword.PlaceholderText = "Enter a password or keep the saved password";
        }
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
            throw new EngineException("Save your web authorization choice before starting it.");
        var selectedTenant = _tenant.SelectedIndex switch { 0 => "common", 1 => "organizations", 2 => "consumers", _ => _tenantId.Text.Trim() };
        if (_authentication.SelectedIndex != 1 ||
            !string.Equals(Value(savedAuthentication, "client_id"), _clientId.Text.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Value(savedAuthentication, "tenant"), selectedTenant, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Value(saved, "email"), _email.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new EngineException("Save your account and Microsoft app ID changes before starting web authorization.");
        var start = await _engine.ManageAsync("start_microsoft_signin", new { account_id = _accountId }, _lifetime.Token);
        var flowId = Value(start, "flow_id");
        var verificationUri = MicrosoftVerificationUri(Value(start, "verification_uri"));
        var expiresIn = start.GetProperty("expires_in").GetInt32();
        var interval = start.GetProperty("interval").GetInt32();
        if (flowId.Length == 0 || expiresIn is < 1 or > 3600 || interval is < 1 or > 300)
            throw new EngineException("Microsoft could not start web authorization. Try starting it again.");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        using var flowCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var status = Text("Waiting for you to finish web authorization with Microsoft.");
        var userCode = new TextBox
        {
            Header = "Enter this code on Microsoft's page", Text = Value(start, "user_code"),
            IsReadOnly = true, FontSize = 24, FontFamily = new FontFamily("Consolas")
        };
        var openBrowser = Action("Open Microsoft authorization page", async () =>
        {
            try
            {
                var opened = await Windows.System.Launcher.LaunchUriAsync(verificationUri);
                status.Text = opened
                    ? "Finish web authorization in your browser. This window will wait for Microsoft's response."
                    : "Windows could not open the browser. Open the displayed Microsoft address manually.";
            }
            catch (Exception)
            {
                status.Text = "Windows could not open the browser. Open the displayed Microsoft address manually.";
            }
        }, accent: true);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "Web authorization with Microsoft", CloseButtonText = "Cancel",
            CloseButtonStyle = DialogButtonStyle("Cancel Microsoft web authorization."),
            DefaultButton = ContentDialogButton.Close,
            Content = Stack(
                Hint("You chose web authorization for this Microsoft account. The browser opens only when you press the button below."),
                userCode, Text(verificationUri.AbsoluteUri), openBrowser, status,
                Hint($"This code expires at {deadline.ToLocalTime():t}. Web authorization does not test mail reading or sending."))
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
                    if (remaining <= TimeSpan.Zero) throw new EngineException("The Microsoft code expired. Start web authorization again.");
                    await Task.Delay(remaining < TimeSpan.FromSeconds(interval) ? remaining : TimeSpan.FromSeconds(interval), flowCancellation.Token);
                    if (DateTimeOffset.UtcNow >= deadline) throw new EngineException("The Microsoft code expired. Start web authorization again.");
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
                            throw new EngineException("Microsoft returned an unexpected authorization status.");
                    }
                }
            }
            catch (OperationCanceledException) when (flowCancellation.IsCancellationRequested) { }
            catch (EngineException exception) { failure = exception.Message; dialog.Hide(); }
            catch (Exception)
            {
                failure = "Microsoft web authorization could not finish. Check the account and app ID before trying again.";
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
                    CleanupNotice.Message = "Local web authorization cleanup could not be confirmed. " + exception.Message;
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
            ShowNotice("Microsoft web authorization completed", "Test incoming mail reading and the optional SMTP connection separately.", InfoBarSeverity.Success);
        }
        else ShowNotice("Microsoft web authorization closed", "The connection was not completed. Your choice was not changed to a password.", InfoBarSeverity.Informational);
    }

    private static Uri MicrosoftVerificationUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            value.Contains('@') || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (uri.Host != "microsoft.com" && uri.Host != "www.microsoft.com") ||
            (uri.AbsolutePath != "/devicelogin" && uri.AbsolutePath != "/devicelogin/"))
            throw new EngineException("Microsoft returned an unexpected web address. Web authorization was stopped.");
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

    private async Task BuildAssistantPageAsync()
    {
        Page.Children.Clear();
        Page.Children.Add(Heading("Your assistant", "Find the email you need, right in your conversation."));
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
        Page.Children.Add(PageHeading("Your assistant", "Find the email you need, right in your conversation.",
            IconAction("Check Codex again", "\uE72C", 0x2477C8, () => _ = RunAsync(BuildAssistantPageAsync),
                "Look for Codex and check its saved AgentInbox connection.")));
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(ColoredIcon("\uE71B", 0x8B62CF));
        title.Children.Add(Text(client.DisplayName, 20));
        var state = client.RequiresReplacement ? "Update your connection"
            : client.IsConfigured ? "Added to Codex"
            : !client.IsDetected ? "Codex not found"
            : client.CanConnect ? "Ready to add AgentInbox" : "A setup issue needs your attention";
        var status = Text(state);
        status.Foreground = IconBrush(client.IsConfigured ? 0x16856AU : client.CanConnect ? 0x2477C8U : 0xC77616U);
        var connect = Action(client.RequiresReplacement ? "Update connection" : "Connect to Codex",
            () => _ = RunAsync(() => ConnectAssistantAsync(service, configuration, client)), accent: true,
            tooltip: "Add this AgentInbox installation to Codex's local connections.");
        connect.IsEnabled = client.CanConnect;
        connect.Visibility = client.IsConfigured ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(connect, client.RequiresReplacement ? "Update AgentInbox connection in Codex" : "Connect AgentInbox to Codex");
        Page.Children.Add(Section("", title, status, Text(client.Message), connect));
        if (client.IsConfigured)
        {
            const string prompt = "Which email accounts can you access through AgentInbox?";
            Page.Children.Add(Section("Try it in a new chat",
                Text("Open a new chat in Codex and ask:"), Text(prompt, 18),
                Action("Copy this prompt", () => _ = RunAsync(() =>
                {
                    CopyText(prompt);
                    ShowNotice("Prompt copied", "Paste it into a new Codex chat to check the connection.", InfoBarSeverity.Informational);
                    return Task.CompletedTask;
                }), tooltip: "Copy the first request to try in a new Codex chat."),
                Hint("If AgentInbox is missing, restart Codex and try a new chat. The saved setup has not yet been tested with your assistant.")));
        }
        else Page.Children.Add(Hint("After connecting, open a new chat in Codex to start using AgentInbox."));
        Page.Children.Add(Hint("You choose which accounts Codex can read and whether it can send from them."));

        var path = new TextBox
        {
            Header = "Codex settings file", Text = client.ConfigurationPath.Length > 0 ? client.ConfigurationPath : "Not available",
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var box = new TextBox
        {
            Text = configuration, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"), FontSize = 12, MaxHeight = 160,
            Header = "Connection settings (JSON)", HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        var copy = Action("Copy connection settings", () => _ = RunAsync(() =>
        {
            CopyText(configuration);
            ShowNotice("Connection settings copied", "Paste them into your assistant's MCP settings, then open a new chat.", InfoBarSeverity.Informational);
            return Task.CompletedTask;
        }), tooltip: "Copy the local connection settings for an assistant that supports MCP.");
        Page.Children.Add(new Expander
        {
            Header = "Manual setup and troubleshooting", IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Stack(Hint("For another desktop assistant, add AgentInbox as a local MCP server using these settings. They contain local paths, but no passwords."),
                copy, box, path)
        });
    }

    /// <summary>Copies an explicitly requested prompt or secret-free connection description.</summary>
    private static void CopyText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception) { throw new EngineException("Windows could not copy the text. Try again."); }
    }

    private async Task ConnectAssistantAsync(AssistantSetupService service, string configuration, AssistantClientStatus client)
    {
        var replaceExisting = false;
        if (client.RequiresReplacement)
        {
            var confirmation = Dialog("Update the Codex connection?", Stack(
                Text("Use this app's settings for AgentInbox in Codex?"),
                Hint("This replaces the existing AgentInbox entry. Your other connections stay as they are.")),
                "Update connection");
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            replaceExisting = true;
        }
        AssistantSetupResult result;
        try { result = await service.ConnectAsync(client, _lifetime.Token, replaceExisting); }
        catch (InvalidOperationException exception) { throw new EngineException(exception.Message); }
        RenderAssistantPage(service, configuration, result.Status);
        ShowNotice(result.ConfigurationWritten ? "Added to Codex" : "Already added to Codex",
            result.Message, result.CleanupPending ? InfoBarSeverity.Warning : InfoBarSeverity.Informational);
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
        catch (EngineException exception) { ShowNotice("Could not complete this action", exception.Message, InfoBarSeverity.Error); }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            ShowNotice("The operation timed out", "Check the connection settings. If you were saving changes, reload the accounts to check whether they were saved.", InfoBarSeverity.Warning);
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
            ShowNotice("Could not complete this action", $"AgentInbox encountered an unexpected error ({exception.GetType().Name}, 0x{exception.HResult:X8}). No automatic retry was made.", InfoBarSeverity.Error);
        }
        finally
        {
            _busy = false;
            Navigation.IsEnabled = true;
            CleanupRetryButton.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private void RequireSavedAccount()
    {
        if (_accountId.Length == 0) throw new EngineException("Save an account before using this action.");
    }

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

    /// <summary>Keeps legal links on the left and the VibeWare signature on the right at smaller widths.</summary>
    private void BrandFooter_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Width < 560;
        Grid.SetColumnSpan(FooterLinks, compact ? 2 : 1);
        FooterLinks.Orientation = args.NewSize.Width < 360 ? Orientation.Vertical : Orientation.Horizontal;
        Grid.SetRow(VibeWareLink, compact ? 1 : 0);
        Grid.SetColumn(VibeWareLink, compact ? 0 : 1);
        Grid.SetColumnSpan(VibeWareLink, compact ? 2 : 1);
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

    private static string SafeResult(JsonElement result) => result.TryGetProperty("message", out var message)
        ? message.GetString() ?? "The operation completed." : "The operation completed.";

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

    /// <summary>Centers each tab icon and label without changing the stock TabViewItem template.</summary>
    private static TabViewItem AccountTab(string name, string glyph, uint color, FrameworkElement content)
    {
        content.Margin = new Thickness(0, 20, 0, 0);
        content.VerticalAlignment = VerticalAlignment.Top;
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(new FontIcon
        {
            Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16, Foreground = IconBrush(color), VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = name, TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        var tab = new TabViewItem
        {
            Header = header, IsClosable = false, Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top
        };
        AutomationProperties.SetName(tab, name);
        return tab;
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
}
