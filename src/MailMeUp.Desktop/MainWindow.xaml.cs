// SPDX-License-Identifier: MIT

using System.Text.Json;
using MailMeUp.Desktop.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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
    private ComboBox _preset = new();
    private TextBox _label = new();
    private TextBox _email = new();
    private ComboBox _authentication = new();
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

    internal MainWindow(PythonEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(1120, area.Width);
        var height = Math.Min(900, area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2, width, height));
        Closed += (_, _) => _lifetime.Cancel();
    }

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
        await RunAsync(async () =>
        {
            Page.Children.Clear();
            switch (item.Tag as string)
            {
                case "accounts": BuildAccountsPage(); await LoadAccountsAsync(); break;
                case "outgoing": await BuildOutgoingPageAsync(); break;
                case "assistant": BuildAssistantPage(); break;
            }
        });
    }

    private void BuildAccountsPage()
    {
        Page.Children.Add(Heading("Email accounts", "Connect with your mail server's settings and an account or app password."));
        _accountPicker = new ComboBox { Header = "Account", MinWidth = 300, HorizontalAlignment = HorizontalAlignment.Stretch };
        _accountPicker.SelectionChanged += (_, _) =>
        {
            if (_loadingForm || _accountPicker.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            var selected = _accounts.FirstOrDefault(account => Value(account, "id") == id);
            if (selected.ValueKind != JsonValueKind.Undefined) FillAccount(selected);
        };
        Page.Children.Add(_accountPicker);
        Page.Children.Add(Actions(Action("Add account", () => { _accountPicker.SelectedIndex = -1; FillAccount(null); }),
            Action("Remove account", () => _ = RunAsync(RemoveAccountAsync)),
            Action("Refresh", () => _ = RunAsync(LoadAccountsAsync))));

        _preset = new ComboBox { Header = "Server preset", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var name in new[] { "Custom mail server", "Gmail", "iCloud", "Outlook.com", "Microsoft 365" })
            _preset.Items.Add(name);
        _preset.SelectionChanged += (_, _) => ApplyPreset();
        _label = Field("Account name", "Personal, work, or a name you recognize");
        _email = Field("Email address", "you@example.com");
        _email.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_incomingUser.Text)) _incomingUser.Text = _email.Text.Trim();
            if (string.IsNullOrWhiteSpace(_smtpUser.Text)) _smtpUser.Text = _email.Text.Trim();
        };
        _authentication = new ComboBox { Header = "Authentication", HorizontalAlignment = HorizontalAlignment.Stretch };
        _authentication.Items.Add("Password — account or app password");
        _authentication.Items.Add("Microsoft sign-in (OAuth2)");
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
        Page.Children.Add(Section("Account", _preset, Pair(_label, _email), _authentication, _microsoftFields,
            Hint("A preset fills server details. Password is the default. Use an app password when required, or explicitly choose Microsoft sign-in for a Microsoft account.")));

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
        Page.Children.Add(Section("Incoming mail", _incomingProtocol, Pair(_incomingHost, _incomingPort), _incomingTls,
            Pair(_incomingUser, _incomingPassword), _folders, _folderHint,
            Hint("Passwords and Microsoft sign-in tokens stay in Windows Credential Manager. Assistant configuration contains neither."), _readEnabled));

        _configureSmtp = new ToggleSwitch { Header = "Configure outgoing SMTP", OnContent = "Configured", OffContent = "Off" };
        _smtpHost = Field("SMTP server", "smtp.example.com");
        _smtpPort = Field("Port", "465");
        _smtpTls = TlsField();
        _smtpUser = Field("Username", "Usually your full email address");
        _smtpPassword = Password("SMTP password");
        _sendEnabled = new ToggleSwitch { Header = "Enable sending from this account", OnContent = "Enabled", OffContent = "Disabled" };
        _smtpFields = Stack(Pair(_smtpHost, _smtpPort), _smtpTls, Pair(_smtpUser, _smtpPassword), _sendEnabled,
            Hint("Every email still needs your approval in a separate review window. Enabling SMTP never approves a message."));
        _configureSmtp.Toggled += (_, _) =>
        {
            _smtpFields.Visibility = _configureSmtp.IsOn ? Visibility.Visible : Visibility.Collapsed;
            if (!_configureSmtp.IsOn) _sendEnabled.IsOn = false;
        };
        Page.Children.Add(Section("Outgoing mail · SMTP", _configureSmtp, _smtpFields));
        _save = Action("Save account", () => _ = RunAsync(SaveAccountAsync), accent: true);
        _testRead = Action("Test IMAP reading", () => _ = RunAsync(TestReadAsync));
        Page.Children.Add(Actions(_save, _testRead,
            Action("Test SMTP connection", () => _ = RunAsync(TestSmtpAsync)),
            Action("Send a test email…", () => _ = RunAsync(PrepareTestAsync))));
        Page.Children.Add(Hint("Save changes before testing. Connection tests use the saved settings. A test email opens the same approval step as any other send."));
        FillAccount(null);
    }

    private async Task LoadAccountsAsync()
    {
        var selected = _accountId;
        var result = await _engine.ManageAsync("list_accounts", new { }, _lifetime.Token);
        _accounts.Clear();
        _accounts.AddRange(result.EnumerateArray().Select(account => account.Clone()));
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
        _accountId = account is { } value ? Value(value, "id") : "";
        _revision = account is { } current ? current.GetProperty("revision").GetInt32() : null;
        _preset.SelectedIndex = 0;
        _label.Text = account is { } name ? Value(name, "label") : "";
        _email.Text = account is { } email ? Value(email, "email") : "";
        var authentication = account?.GetProperty("authentication");
        var microsoft = authentication is { } method && Value(method, "method") == "microsoft_oauth";
        _authentication.SelectedIndex = microsoft ? 1 : 0;
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
        _save.IsEnabled = true;
        _loadingForm = false;
        UpdateIncomingPresentation();
        UpdateAuthenticationPresentation(clearPasswords: false);
    }

    private void ApplyPreset()
    {
        if (_loadingForm) return;
        if (_preset.SelectedIndex == 0) return;
        if (_preset.SelectedIndex == 2 && _incomingProtocol.SelectedIndex == 1)
        {
            _loadingForm = true;
            _incomingProtocol.SelectedIndex = 0;
            _loadingForm = false;
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
        UpdateIncomingPresentation();
        if (_preset.SelectedIndex is 3 or 4)
            ShowNotice("Microsoft cloud accounts require modern authentication", "Outlook.com requires modern authentication. Exchange Online removed Basic authentication for IMAP and POP3. Choose Microsoft sign-in and supply your app registration for these services; tenant policies can still restrict access. AgentInbox never switches authentication automatically.", InfoBarSeverity.Warning);
    }

    private void IncomingProtocolChanged()
    {
        if (_loadingForm) return;
        _incomingPort.Text = _incomingProtocol.SelectedIndex == 1 ? "995" : "993";
        _incomingTls.SelectedIndex = 0;
        if (_preset.SelectedIndex > 0) ApplyPreset();
        else UpdateIncomingPresentation();
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
        _accountId = Value(saved, "id");
        _incomingPassword.Password = "";
        _smtpPassword.Password = "";
        passwords.Clear();
        await LoadAccountsAsync();
        ShowNotice("Account saved", "Your assistant can use only the access you enabled.", InfoBarSeverity.Success);
    }

    private async Task RemoveAccountAsync()
    {
        RequireSavedAccount();
        var dialog = Dialog("Remove this account?", Text("This removes local connection settings and saved credentials. Mail on the server stays in place."), "Remove account");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await _engine.ManageAsync("remove_account", new { account_id = _accountId }, _lifetime.Token);
        _accountId = "";
        await LoadAccountsAsync();
        ShowNotice("Account removed", "The local connection is no longer available to your assistant.", InfoBarSeverity.Success);
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
            DefaultButton = ContentDialogButton.Close,
            Content = Stack(
                Hint("You chose Microsoft sign-in for this account. The browser opens only when you press the button below."),
                userCode, Text(verificationUri.AbsoluteUri), openBrowser, status,
                Hint($"This code expires at {deadline.ToLocalTime():t}. Sign-in does not test mail reading or sending."))
        };
        var connected = false;
        string? failure = null;

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
                try { await _engine.ManageAsync("cancel_microsoft_signin", new { flow_id = flowId }, _lifetime.Token); }
                catch (EngineException) { }
            }
        }
        if (_lifetime.IsCancellationRequested) return;
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
        Page.Children.Add(Heading("Outgoing mail", "Review the exact recipients, message and attachments before any email can leave."));
        Page.Children.Add(Action("Refresh requests", () => _ = RunAsync(BuildOutgoingPageAsync)));
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

    private void BuildAssistantPage()
    {
        Page.Children.Add(Heading("Connect your assistant", "Use the local Python engine in an MCP client that supports standard input and output."));
        Page.Children.Add(Hint("Python 3.11 or later must already be available. To choose a runtime, set AGENTINBOX_PYTHON to its absolute executable path and reopen AgentInbox."));
        var configuration = _engine.CreateClientConfiguration();
        var box = new TextBox
        {
            Text = configuration, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"), MinHeight = 260, MaxHeight = 480,
            Header = "Local MCP configuration (.mcp.json)", HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Page.Children.Add(box);
        Page.Children.Add(Action("Copy configuration", () =>
        {
            var package = new DataPackage();
            package.SetText(configuration);
            Clipboard.SetContent(package);
            ShowNotice("Configuration copied", "Add the server entry to your client's MCP settings and reconnect it.", InfoBarSeverity.Success);
        }, accent: true));
        Page.Children.Add(Hint("This configuration contains paths, not passwords. Keep AgentInbox at these paths. Only accounts you share are available for reading, and every outgoing message still opens a Windows approval dialog."));
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
        Progress.Visibility = Visibility.Visible;
        Notice.IsOpen = false;
        try { await operation(); }
        catch (EngineException exception) { ShowNotice("Could not complete this action", exception.Message + ReviewFailureHint(), InfoBarSeverity.Error); }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            ShowNotice("The operation timed out", "Check the saved settings and server connection. If this happened during a send, check its status before preparing another email." + ReviewFailureHint(), InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            ShowNotice("Could not complete this action", "The local engine could not complete the request. Check your Python runtime and saved account settings. No automatic retry was made." + ReviewFailureHint(), InfoBarSeverity.Error);
        }
        finally
        {
            _busy = false;
            Navigation.IsEnabled = true;
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
    }

    private ContentDialog Dialog(string title, UIElement content, string primary) => new()
    {
        XamlRoot = Root.XamlRoot, Title = title, Content = content, PrimaryButtonText = primary,
        CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
    };

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

    private static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static Border Section(string title, params UIElement[] children)
    {
        var content = Stack();
        if (title.Length > 0) content.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var child in children) content.Children.Add(child);
        return new Border { Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SetupCard"], Child = content };
    }

    private static Grid Pair(UIElement left, UIElement right)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
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

    private static Button Action(string caption, Action handler, bool accent = false)
    {
        var button = new Button { Content = caption };
        if (accent) button.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
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
