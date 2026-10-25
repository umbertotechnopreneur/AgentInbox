// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using MailMeUp.Desktop.Services;
using MailMeUp.Desktop.ViewModels;
using MailMeUp.Desktop.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace MailMeUp.Desktop;

/// <summary>Configures email accounts and assistant access on Windows.</summary>
public sealed partial class MainWindow : Window
{
    private readonly ConfigurationModel _configuration;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loaded;
    private ListView _accountPicker = new();
    private TabView _accountTabs = new();
    private StackPanel _accountToolbar = new();
    private Grid _accountWorkspace = new();
    private StackPanel _accountEditor = new();
    private Grid _accountSelectionState = new();
    private Grid _accountEmptyState = new();
    private Button _createFirstAccount = new();
    private Button _removeAccount = new();
    private Button _cancelAccount = new();
    private TextBlock _newAccountHeading = new();
    private ComboBox _preset = new();
    private TextBox _label = new();
    private TextBox _email = new();
    private ComboBox _authentication = new();
    private TextBlock _authenticationHint = new();
    private HyperlinkButton _providerHelpLink = new();
    private ComboBox _tenant = new();
    private TextBox _tenantId = new();
    private StackPanel _microsoftFields = new();
    private Button _microsoftSignIn = new();
    private TextBox _microsoftLog = new();
    private ComboBox _incomingProtocol = new();
    private TextBox _incomingHost = new();
    private TextBox _incomingPort = new();
    private ComboBox _incomingTls = new();
    private TextBox _incomingUser = new();
    private PasswordBox _incomingPassword = new();
    private TextBlock _incomingSignInHint = new();
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
    private TextBlock _smtpSignInHint = new();
    private StackPanel _smtpFields = new();
    private Button _save = new();
    private TextBlock _saveStatus = new();
    private Button _testRead = new();
    private StackPanel _incomingChecks = new();
    private StackPanel _smtpChecks = new();
    private Button _testSmtp = new();

    internal MainWindow(PythonEngine engine)
    {
        _configuration = new ConfigurationModel(engine, DesktopSettings.Load().MicrosoftClientId);
        InitializeComponent();
        Root.DataContext = _configuration;
        BindApplicationState();
        _configuration.Editor.PropertyChanged += (_, _) => { RenderAccountErrors(); RenderFolderSelection(); };
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
        PageScroll.SizeChanged += (_, _) => UpdateAccountLayout();
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

    private void ProviderLogo_Failed(object sender, ExceptionRoutedEventArgs args)
    {
        if (sender is Image image && image.Parent is Grid grid)
        {
            image.Opacity = 0;
            grid.Children[0].Opacity = 0.55;
        }
    }

    private void AccountStatus_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is FontIcon icon && args.NewValue is AccountListItemModel model)
            ModelBinding.Bind(icon, IconElement.ForegroundProperty, model, nameof(AccountListItemModel.CheckTone), converter: ModelBinding.ToneConverter.Instance);
    }

    private void ProviderLogo_Opened(object sender, RoutedEventArgs args)
    {
        if (sender is Image image && image.Parent is Grid grid)
        {
            image.Opacity = 1;
            grid.Children[0].Opacity = 0;
        }
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
            _configuration.Page = page;
            PageScroll.ChangeView(0, 0, null, disableAnimation: true);
            try
            {
                switch (page)
                {
                    case "accounts":
                        BuildAccountsPage();
                        if (!_configuration.AccountsLoaded) await LoadAccountsAsync();
                        break;
                    case "assistant": await BuildAssistantPageAsync(); break;
                    default: throw new InvalidOperationException("Unknown setup page.");
                }
            }
            catch
            {
                // Never leave a partially constructed form with actionable Save or Cancel buttons.
                Page.Children.Clear();
                PageActions.Children.Clear();
                _configuration.Page = "";
                Page.Children.Add(Text("This page could not be opened", 22));
                Page.Children.Add(Hint("You can try again or choose another page."));
                Page.Children.Add(Action("Try again", () => _ = OpenPageAsync(page),
                    tooltip: "Reload this page without repeating a mail operation."));
                throw;
            }
        });
    }

    private void BuildAccountsPage()
    {

        _save = Action("Save changes", () => _ = RunAsync(SaveAccountAsync), accent: true);
        _cancelAccount = Action("Cancel", CancelNewAccount, tooltip: "Discard this new account and return to your saved accounts.");
        Page.Children.Add(Heading("Email accounts", "Connect as many compatible mailboxes as you need: Gmail, Microsoft, iCloud, GoDaddy and other IMAP or POP3 providers. Share accounts and selected IMAP folders with your AI agent through one MCP connection."));
        _saveStatus = Hint("");
        _saveStatus.VerticalAlignment = VerticalAlignment.Center;
        PageActions.Children.Add(_save);
        PageActions.Children.Add(_cancelAccount);
        PageActions.Children.Add(_saveStatus);
        _createFirstAccount = Action("Create your first account", StartNewAccount, accent: true,
            tooltip: "Open the setup form for your first email account.");
        _accountEmptyState = AccountPlaceholder("Add your first mailbox",
            "Start with Gmail, Microsoft, iCloud, GoDaddy or another compatible IMAP/POP3 provider. Add more mailboxes and choose the accounts and folders your AI agent can use.",
            _createFirstAccount);
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

        var accountActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom };
        accountActions.Children.Add(IconAction("Add connection", "\uE710", 0x2BAF90, StartNewAccount,
            "Add another compatible IMAP or POP3 mailbox. Share multiple accounts and their selected folders through one MCP connection.", caption: "Add"));
        _removeAccount = IconAction("Remove account", "\uE74D", 0xD83B35, () => _ = RunAsync(RemoveAccountAsync),
            "Remove this account from AgentInbox. Your emails stay with your mail provider.");
        accountActions.Children.Add(_removeAccount);
        accountActions.Children.Add(IconAction("Refresh accounts", "\uE72C", 0x2477C8, () => _ = RunAsync(LoadAccountsAsync),
            "Reload saved accounts and their settings."));
        var testAll = IconAction("Test All", "\uE73E", 0x16856A, () => _ = RunAsync(TestAllAccountsAsync),
            "Check connection and sign-in for every saved account, including configured sending. Stop at the first error. No email is read or sent.", caption: "Test All");
        testAll.Margin = new Thickness(12, 0, 0, 0);
        ModelBinding.Bind(testAll, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.CanTestAll));
        accountActions.Children.Add(testAll);
        _accountToolbar = Stack(Text("Connections", 22), accountActions, _accountPicker);
        _accountToolbar.SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width < 260;
            accountActions.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
            testAll.Margin = compact ? new Thickness(0, 12, 0, 0) : new Thickness(12, 0, 0, 0);
        };
        _accountWorkspace = new Grid { ColumnSpacing = 24, RowSpacing = 20 };
        _accountWorkspace.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _accountWorkspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _accountWorkspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _accountWorkspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _accountWorkspace.Children.Add(_accountToolbar);
        _accountWorkspace.SizeChanged += (_, _) => UpdateAccountLayout();
        Page.Children.Add(_accountWorkspace);

        _preset = new ComboBox { Header = "Email provider", HorizontalAlignment = HorizontalAlignment.Stretch };
        _preset.Items.Add(PresetItem("Other mail provider", "\uE713", 0x667789));
        _preset.Items.Add(PresetItem("Gmail", "\uE715", 0xD94235));
        _preset.Items.Add(PresetItem("iCloud", "\uE753", 0x3282CF));
        _preset.Items.Add(PresetItem("Outlook.com", "\uE715", 0x1274C4));
        _preset.Items.Add(PresetItem("Microsoft 365", "\uE71D", 0x8661C5));

        _label = Field("Connection name", "Personal, work, or a name you recognize");
        AutomationProperties.SetName(_label, "Connection name");
        ToolTipService.SetToolTip(_label, "Rename this connection, then choose Save changes.");
        _email = Field("Email address", "you@example.com");
        _email.LostFocus += (_, _) => _configuration.Editor.FillSignInEmails();
        _authentication = new ComboBox { Header = "How to connect", PlaceholderText = "Choose how to connect", HorizontalAlignment = HorizontalAlignment.Stretch };

        _tenant = new ComboBox { Header = "Microsoft account type", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var name in new[] { "Personal or work", "Work or school", "Personal", "A specific organization" })
            _tenant.Items.Add(name);
        _tenantId = Field("Organization ID", "Ask your IT team for this ID");

        _microsoftSignIn = Action("Sign in with Microsoft", () => _ = RunAsync(SignInMicrosoftAsync),
            tooltip: "Save these account settings and get a code to sign in on Microsoft's page.");
        _microsoftLog = SignInLogBox();
        _microsoftFields = Stack(_tenant, _tenantId,
            _microsoftSignIn,
            Hint("Press Sign in with Microsoft. I'll save your account settings and give you a code to enter on Microsoft's page. The same sign-in is used to read and send mail."),
            SignInDetails(_microsoftLog, Action("Copy sign-in details", () => _ = RunAsync(() =>
            {
                CopyText(_configuration.SignIn.Log);
                return Task.CompletedTask;
            }), tooltip: "Copy these details without passwords or sign-in codes.")));
        _authenticationHint = Hint("");
        _providerHelpLink = new HyperlinkButton
        {
            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0),
            Visibility = Visibility.Collapsed
        };
        var providerChoice = Stack(_preset, _authenticationHint, _providerHelpLink);
        _readEnabled = new ToggleSwitch { Header = "Enable receive", OnContent = "Enabled", OffContent = "Disabled" };
        AutomationProperties.SetName(_readEnabled, "Enable receive for this account");
        ToolTipService.SetToolTip(_readEnabled, "Allow your assistant to search and read the selected folders after you save changes.");
        _sendEnabled = new ToggleSwitch { Header = "Enable send", OnContent = "Enabled", OffContent = "Disabled" };
        AutomationProperties.SetName(_sendEnabled, "Enable send for this account");
        ToolTipService.SetToolTip(_sendEnabled, "Allow your assistant to send email from this account when you ask, after you save changes.");
        var accountAccess = Stack(_readEnabled, _sendEnabled,
            Hint("Save changes to apply these permissions. Configure servers and folders under Read mail and Send mail."));
        accountAccess.Margin = new Thickness(0, 8, 0, 0);
        var accountContent = Section("", providerChoice, _email, _authentication, accountAccess, _microsoftFields);

        _incomingProtocol = new ComboBox { Header = "How to read mail", HorizontalAlignment = HorizontalAlignment.Stretch };
        _incomingProtocol.Items.Add("IMAP — mail and server folders");
        _incomingProtocol.Items.Add("POP3 — mailbox messages");

        _incomingHost = Field("Incoming server", "imap.example.com");
        _incomingPort = Field("Port", "993");
        _incomingTls = TlsField();
        _incomingUser = Field("Email address for reading", "you@example.com");
        _incomingPassword = Password("Incoming password");
        _incomingSignInHint = Hint("Uses your Microsoft sign-in. No password is needed here.");
        _folders = new ListView { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 220, MinHeight = 48 };
        AutomationProperties.SetName(_folders, "Folders available to your assistant");
        _checkConnection = Action("Check connection and get folders", () => _ = RunAsync(() => CheckAccountAsync(saveAccount: false)),
            tooltip: "Check this account, then choose the IMAP folders your AI agent can read. Add more accounts to work across more folders. Nothing is saved or sent.");
        _folderHint = Hint("");
        var incomingSettings = Section("", _incomingProtocol, Pair(_incomingHost, _incomingPort), _incomingTls,
            Pair(_incomingUser, Stack(_incomingPassword, _incomingSignInHint)), _checkConnection, Text("Folders available to your assistant"), _folders, _folderHint,
            Hint("Windows stores your sign-in details on this computer. They are not copied to Codex's settings."));
        _testRead = Action("Check reading", () => _ = RunAsync(TestReadAsync));
        _incomingChecks = Stack(_testRead, Hint("Save changes before testing. This check uses the saved incoming settings."));
        var incomingContent = Stack(incomingSettings, _incomingChecks);

        _configureSmtp = new ToggleSwitch { Header = "Set up sending", OnContent = "On", OffContent = "Off" };
        _smtpHost = Field("Sending server (SMTP)", "smtp.example.com");
        _smtpPort = Field("Port", "465");
        _smtpTls = TlsField();
        _smtpUser = Field("Email address for sending", "you@example.com");
        _smtpPassword = Password("Password for sending");
        _smtpSignInHint = Hint("Uses the same Microsoft sign-in as incoming mail. No extra password is needed.");
        _testSmtp = Action("Check sending connection", () => _ = RunAsync(TestSmtpAsync),
            tooltip: "Check these sending settings without saving changes or sending an email.");
        _smtpChecks = Stack(_testSmtp, Hint("Check the connection and sign-in without saving settings or sending an email."));
        _smtpFields = Stack(Pair(_smtpHost, _smtpPort), _smtpTls, Pair(_smtpUser, Stack(_smtpPassword, _smtpSignInHint)), _smtpChecks);

        var outgoingContent = Stack(_configureSmtp,
            Hint("Configure the outgoing server here. Choose Enable send under Account to allow your assistant to send email."), _smtpFields);
        foreach (var tab in new[] {
            AccountTab("Account", "\uE77B", 0xB58ADA, accountContent),
            AccountTab("Read mail", "\uE715", 0x409BE0, incomingContent),
            AccountTab("Send mail", "\uE724", 0x2BAF90, outgoingContent) })
            _accountTabs.TabItems.Add(tab);
        _accountTabs.SelectionChanged += (_, _) => UpdateAccountTabHeaders();

        UpdateAccountTabHeaders();
        _newAccountHeading = Text("New account", 22);
        _accountEditor = Stack(_newAccountHeading, _label, _accountTabs);
        _accountSelectionState = AccountPlaceholder("Select or add a mailbox",
            "Select a mailbox from the list to edit its name and settings.\nUse the + button to add another mailbox.");
        _accountWorkspace.Children.Add(_accountEditor);
        _accountWorkspace.Children.Add(_accountSelectionState);
        BindAccountControls();

    }

    private void StartNewAccount()
    {
        _configuration.StartNewAccount();
        _label.Focus(FocusState.Programmatic);
    }

    private void CancelNewAccount()
    {
        _configuration.CancelNewAccount();
        if (_configuration.Accounts.Count == 0) _createFirstAccount.Focus(FocusState.Programmatic);
        else _accountPicker.Focus(FocusState.Programmatic);
    }

    /// <summary>Keeps the saved connection list beside the editor, stacking it on smaller windows.</summary>
    private void UpdateAccountLayout()
    {
        if (!Page.Children.Contains(_accountWorkspace)) return;
        var hasList = _accountToolbar.Visibility == Visibility.Visible;
        var sideBySide = hasList && _accountWorkspace.ActualWidth >= 960;
        _accountWorkspace.ColumnDefinitions[0].Width = sideBySide ? new GridLength(280) : new GridLength(0);
        _accountWorkspace.ColumnSpacing = sideBySide ? 24 : 0;
        _accountWorkspace.RowSpacing = hasList && !sideBySide ? 20 : 0;
        Grid.SetColumnSpan(_accountToolbar, sideBySide ? 1 : 2);
        _accountPicker.MaxHeight = sideBySide ? 480 : 180;
        foreach (var content in new FrameworkElement[] { _accountEditor, _accountSelectionState })
        {
            Grid.SetColumn(content, sideBySide ? 1 : 0);
            Grid.SetColumnSpan(content, sideBySide ? 1 : 2);
            Grid.SetRow(content, hasList && !sideBySide ? 1 : 0);
        }
        // Use the remaining viewport for the prompt, while keeping the editor top-aligned.
        var headingHeight = ((FrameworkElement)Page.Children[0]).ActualHeight;
        var pagePadding = Page.Padding.Top + Page.Padding.Bottom;
        var availableHeight = PageScroll.ViewportHeight - headingHeight - Page.Spacing - pagePadding;
        _accountEmptyState.MinHeight = Math.Max(320, availableHeight);
        _accountSelectionState.MinHeight = sideBySide ? Math.Max(320, availableHeight) : 320;
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

    private Task LoadAccountsAsync() => _configuration.LoadAccountsAsync(_lifetime.Token);

    private async Task RemoveAccountAsync()
    {
        _configuration.RequireSavedAccount();
        var dialog = Dialog("Remove this account?", Text("This removes its settings and saved sign-in details from AgentInbox. Your emails stay with your mail provider."), "Remove account");
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await _configuration.RemoveAccountAsync(_lifetime.Token);
    }

    private Task TestReadAsync() => _configuration.TestReadingAsync(_lifetime.Token);

    private async Task BuildAssistantPageAsync()
    {
        Page.Children.Clear();
        Page.Children.Add(Heading("Connect Codex", ""));
        Page.Children.Add(Hint("Checking Codex on this computer…"));
        await _configuration.Assistant.DetectAsync(_lifetime.Token);
        RenderAssistantPage();
    }

    private void RenderAssistantPage()
    {
        var model = _configuration.Assistant;
        Page.Children.Clear();
        Page.Children.Add(PageHeading("Connect Codex", "One connection for all enabled mailboxes and their selected folders. Use your AI agent across accounts and providers.",
            IconAction("Check Codex again", "\uE72C", 0x2477C8, () => _ = RunAsync(BuildAssistantPageAsync),
                "Look for Codex and check its saved AgentInbox connection.")));
        TextBlock Label(string property, bool hint = false, double size = 0)
        {
            var label = hint ? Hint("") : Text("", size > 0 ? size : 14);
            ModelBinding.Bind(label, TextBlock.TextProperty, model, property);
            return label;
        }
        TextBox FieldFor(string header, string property, bool multiline = false)
        {
            var box = new TextBox { Header = header, IsReadOnly = true, AcceptsReturn = multiline,
                TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch };
            ModelBinding.Bind(box, TextBox.TextProperty, model, property);
            return box;
        }
        var status = Label(nameof(AssistantConnectionModel.StatusText));
        status.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        ModelBinding.Visible(status, model, nameof(AssistantConnectionModel.ShowStatus));
        ModelBinding.Bind(status, TextBlock.ForegroundProperty, model, nameof(AssistantConnectionModel.StatusTone), converter: ModelBinding.ToneConverter.Instance);
        var connect = Action("Connect to Codex", () => _ = RunAsync(ConnectAssistantAsync), accent: true,
            tooltip: "Add this AgentInbox installation to Codex's local connections.");
        ModelBinding.Bind(connect, ContentControl.ContentProperty, model, nameof(AssistantConnectionModel.ConnectText));
        ModelBinding.Bind(connect, AutomationProperties.NameProperty, model, nameof(AssistantConnectionModel.ConnectName));
        ModelBinding.Bind(connect, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.CanConnectAssistant));
        ModelBinding.Visible(connect, model, nameof(AssistantConnectionModel.ShowConnect));
        var setupMessage = Label(nameof(AssistantConnectionModel.Message));
        ModelBinding.Visible(setupMessage, model, nameof(AssistantConnectionModel.IsConfigured), invert: true);
        Page.Children.Add(Section("", status, setupMessage, connect));
        var path = FieldFor("Codex settings file", nameof(AssistantConnectionModel.ConfigurationPath));
        var checkStatus = Label(nameof(AssistantConnectionModel.CheckStatus), hint: true);
        ModelBinding.Bind(checkStatus, TextBlock.ForegroundProperty, model, nameof(AssistantConnectionModel.CheckTone), converter: ModelBinding.ToneConverter.Instance);
        var test = Action("Test connection", () => _ = RunAsync(() => model.TestAsync(_lifetime.Token)),
            tooltip: "Check the saved Codex settings, start AgentInbox and ask for its tool list. No email is read or sent.");
        ModelBinding.Bind(test, Control.IsEnabledProperty, _configuration, nameof(ConfigurationModel.CanTestAssistant));
        Page.Children.Add(Section("1. Check AgentInbox on this PC",
            Hint("This checks that AgentInbox can start and reply. It does not read or send email."),
            test, checkStatus));
        var prompt = FieldFor("", nameof(AssistantConnectionModel.TryPrompt), multiline: true);
        AutomationProperties.SetName(prompt, "Request to paste into Codex");
        var tryChat = Section("2. Try it in Codex", Hint("Open a new Codex chat and paste this request:"), prompt,
            Action("Copy this prompt", () => _ = RunAsync(() =>
            {
                CopyText(model.TryPrompt);
                ShowNotice("Prompt copied", "Paste it into a new Codex chat to check the connection.", InfoBarSeverity.Informational);
                return Task.CompletedTask;
            }), tooltip: "Copy the first request to try in a new Codex chat."),
            Hint("If Codex cannot connect, restart Codex and open a new chat. A successful local check does not verify the connection inside your chat."));
        ModelBinding.Visible(tryChat, model, nameof(AssistantConnectionModel.IsConfigured));
        Page.Children.Add(tryChat);
        var afterConnecting = Hint("After connecting, open a new chat in Codex to start using AgentInbox.");
        ModelBinding.Visible(afterConnecting, model, nameof(AssistantConnectionModel.IsConfigured), invert: true);
        Page.Children.Add(afterConnecting);
        var app = FieldFor("", nameof(AssistantConnectionModel.Command));
        ModelBinding.Bind(app, TextBox.HeaderProperty, model, nameof(AssistantConnectionModel.AppLabel));
        var arguments = FieldFor("Startup options", nameof(AssistantConnectionModel.Arguments), multiline: true);
        arguments.MinHeight = 80;
        arguments.MaxHeight = 140;
        var profile = FieldFor("AgentInbox settings folder", nameof(AssistantConnectionModel.DataDirectory));
        var json = FieldFor("Full local settings (JSON)", nameof(AssistantConnectionModel.Configuration), multiline: true);
        json.Height = 220;
        json.FontFamily = new FontFamily("Consolas");
        json.FontSize = 12;
        ScrollViewer.SetVerticalScrollBarVisibility(json, ScrollBarVisibility.Auto);
        var copy = Action("Copy full settings", () => _ = RunAsync(() =>
        {
            CopyText(model.Configuration);
            ShowNotice("Settings copied", "These JSON settings are for manual setup in another app that supports MCP. Codex saves its own entry in the settings file shown above.", InfoBarSeverity.Informational);
            return Task.CompletedTask;
        }), tooltip: "Copy the local connection settings for an assistant that supports MCP.");
        var details = new Expander
        {
            Header = "Connection details", IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Stack(Hint("Codex starts AgentInbox locally through MCP. Test mailbox access separately under Accounts."),
                path, Label(nameof(AssistantConnectionModel.DetailsHint), hint: true), app, arguments, profile,
                Hint("Use the JSON below for manual setup in another MCP app."), json, copy)
        };
        ModelBinding.Bind(details, Expander.IsExpandedProperty, model, nameof(AssistantConnectionModel.DetailsExpanded), BindingMode.TwoWay);
        Page.Children.Add(details);
    }
    /// <summary>Copies only text the user explicitly chooses to put on the clipboard.</summary>
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

    private async Task ConnectAssistantAsync()
    {
        var replaceExisting = false;
        if (_configuration.Assistant.RequiresConfirmation)
        {
            var confirmation = Dialog("Update the Codex connection?", Stack(Text("Use this app's settings for AgentInbox in Codex?"),
                Hint("This replaces the existing AgentInbox entry. Your other connections stay as they are.")), "Update connection");
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            replaceExisting = true;
        }
        await _configuration.Assistant.ConnectAsync(replaceExisting, _lifetime.Token);
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (_configuration.IsBusy || _lifetime.IsCancellationRequested) return;
        await _configuration.RunAsync(operation, _lifetime.Token);
        if (_lifetime.IsCancellationRequested) return;
        if (_configuration.Notice.IsOpen)
        {
            var notice = _configuration.Notice;
            notice.IsOpen = false;
            using var window = new NotificationWindow(this, notice.Title, notice.Message, notice.Severity);
            window.Activate();
            await window.ClosedTask;
        }
        if (_configuration.Cleanup.IsOpen && !_lifetime.IsCancellationRequested)
        {
            var cleanup = _configuration.Cleanup;
            cleanup.IsOpen = false;
            using var window = new NotificationWindow(this, cleanup.Title, cleanup.Message, cleanup.Severity, allowRetry: true);
            window.Activate();
            await window.ClosedTask;
            if (window.RetryRequested) await RunAsync(() => _configuration.RetryCleanupAsync(_lifetime.Token));
        }
    }

    private void ShowNotice(string title, string message, InfoBarSeverity severity) =>
        _configuration.Notice.Show(title, message, (int)severity);

    /// <summary>Keeps legal links on the left and the VibeWare signature on the right at smaller widths.</summary>
    private void BrandFooter_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Width < 760;
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

    private static TextBlock Text(string value, double size = 14) => new()
    { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size, IsTextSelectionEnabled = true };

    private static TextBlock Hint(string value) => new()
    { Text = value, TextWrapping = TextWrapping.Wrap, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SubtleText"] };

    private static StackPanel Heading(string title, string subtitle)
    {
        var heading = Stack(new TextBlock { Text = title, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SectionTitle"] });
        if (subtitle.Length > 0) heading.Children.Add(Hint(subtitle));
        return heading;
    }

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

    private static Button IconAction(string name, string glyph, uint color, Action handler, string? tooltip = null, string? caption = null)
    {
        var button = new Button
        {
            Content = ColoredIcon(glyph, color), MinWidth = 36, Height = 36,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["QuietButton"],
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (caption is null) button.Width = 36;
        else
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(ColoredIcon(glyph, color));
            content.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center });
            button.Content = content;
            button.Padding = new Thickness(6, 0, 6, 0);
        }
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, tooltip ?? name);
        button.Click += (_, _) => handler();
        return button;
    }

    /// <summary>Centers a compact mailbox prompt in the available space without adding a card.</summary>
    private static Grid AccountPlaceholder(string title, string description, Button? action = null)
    {
        var icon = new FontIcon
        {
            Glyph = "\uE715", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 48,
            Foreground = IconBrush(0xB58ADA), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        var heading = Text(title, 24);
        heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        heading.TextAlignment = TextAlignment.Center;
        var guidance = Hint(description);
        guidance.FontSize = 14;
        guidance.LineHeight = 22;
        guidance.TextAlignment = TextAlignment.Center;
        var content = Stack(icon, heading, guidance);
        content.MaxWidth = 320;
        content.Margin = new Thickness(20);
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        if (action is not null)
        {
            action.HorizontalAlignment = HorizontalAlignment.Center;
            action.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(action);
        }
        var placeholder = new Grid { MinHeight = 320 };
        placeholder.Children.Add(content);
        return placeholder;
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
