/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Account workspace, identity and mail tabs, and responsive layout.
 * Created with AI: OpenAI Codex; extracted account-page composition, 2026-10-27.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using MailMeUp.Desktop.ViewModels;
using MailMeUp.Desktop.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MailMeUp.Desktop;

/// <summary>Configures email accounts and assistant access on Windows.</summary>
public sealed partial class MainWindow
{
    /// <summary>Compose the account workspace before attaching editor bindings.</summary>
    private void BuildAccountsPage()
    {
        // Build page-level actions and placeholders before the account workspace.
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

        // The tab host and account list must exist before model bindings are attached.
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


        // Saved-account actions remain separate from the unsaved editor draft.
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

        // Create every field before bindings can observe the editor draft.
        var accountContent = BuildIdentityTab();
        var incomingContent = BuildIncomingTab();
        var outgoingContent = BuildOutgoingTab();

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

    /// <summary>Create provider, identity, sign-in and access-permission controls.</summary>
    private StackPanel BuildIdentityTab()
    {
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
        return Section("", providerChoice, _email, _authentication, accountAccess, _microsoftFields);
    }

    /// <summary>Create incoming server, folder selection and explicit read-check controls.</summary>
    private StackPanel BuildIncomingTab()
    {
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
        return Stack(incomingSettings, _incomingChecks);
    }

    /// <summary>Create optional SMTP settings and connection-check controls.</summary>
    private StackPanel BuildOutgoingTab()
    {
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

        return Stack(_configureSmtp,
            Hint("Configure the outgoing server here. Choose Enable send under Account to allow your assistant to send email."), _smtpFields);
    }

    /// <summary>Start an explicit new draft and focus its connection name.</summary>
    private void StartNewAccount()
    {
        _configuration.StartNewAccount();
        _label.Focus(FocusState.Programmatic);
    }

    /// <summary>Discard the new-account draft and restore selection focus.</summary>
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

    /// <summary>Reload saved-account snapshots within the configuration window lifetime.</summary>
    private Task LoadAccountsAsync() => _configuration.LoadAccountsAsync(_lifetime.Token);

    /// <summary>Confirm removal of the selected local account before revoking its settings.</summary>
    private async Task RemoveAccountAsync()
    {
        _configuration.RequireSavedAccount();
        var dialog = Dialog("Remove this account?", Text("This removes its settings and saved sign-in details from AgentInbox. Your emails stay with your mail provider."), "Remove account");
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await _configuration.RemoveAccountAsync(_lifetime.Token);
    }

    /// <summary>Request one bounded read check using the saved incoming settings.</summary>
    private Task TestReadAsync() => _configuration.TestReadingAsync(_lifetime.Token);
}
