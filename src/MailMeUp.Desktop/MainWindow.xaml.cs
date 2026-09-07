using System.Runtime.InteropServices;
using MailMeUp.Application;
using MailMeUp.Core;
using MailMeUp.Desktop.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;

namespace MailMeUp.Desktop;

/// <summary>Provides local account, sharing and Codex onboarding through the shared application boundary.</summary>
public sealed partial class MainWindow : Window
{
    private readonly IMailMeUpApplication _application;
    private readonly CodexSetupService _codex;
    private readonly ILogger<MainWindow> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AccessibilitySettings _accessibility = new();
    private CancellationTokenSource? _operation;
    private IReadOnlyList<Account> _accounts = [];
    private IReadOnlyList<ProviderSetupStatus> _providers = [];
    private Dictionary<string, AccountSharingSettings> _sharing = new(StringComparer.Ordinal);
    private Dictionary<string, AccountConnectionCheck> _connectionChecks = new(StringComparer.Ordinal);
    private CodexSetupStatus? _codexStatus;
    private bool _loaded;
    private bool _busy;
    private bool _dialogOpen;
    private bool _allowClose;
    private bool _welcomeReviewed;
    private bool _sharingReviewed;
    private bool _sharingDirty;
    private int _step;
    private bool _narrowSharing;
    private bool _showSharingList;
    private bool _highContrastSubscribed;

    /// <summary>Creates the setup window without starting sign-in or reading mailbox content.</summary>
    public MainWindow(IMailMeUpApplication application, CodexSetupService codex, ILogger<MainWindow> logger)
    {
        _application = application;
        _codex = codex;
        _logger = logger;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        UpdateTitleBar();
        CenterWindow();
        Root.Loaded += Root_Loaded;
        PageScroll.SizeChanged += (_, _) => { if (_loaded) UpdateLayout(); };
        Root.ActualThemeChanged += (_, _) =>
        {
            UpdateTitleBar();
            if (_loaded)
            {
                UpdateProgress();
                RenderSharingAccounts();
            }
        };
        try
        {
            _accessibility.HighContrastChanged += Accessibility_HighContrastChanged;
            _highContrastSubscribed = true;
        }
        catch (COMException exception) when ((uint)exception.HResult == 0x80070490)
        {
            // Some desktop configurations do not expose this optional WinRT event.
            // Current high-contrast state is still read when the window is initialized.
            _logger.LogDebug("High-contrast change notification is unavailable.");
        }
        AppWindow.Closing += AppWindow_Closing;
        Closed += (_, _) =>
        {
            if (_highContrastSubscribed)
                _accessibility.HighContrastChanged -= Accessibility_HighContrastChanged;
            _lifetime.Cancel();
            _operation?.Cancel();
        };
    }

    private void CenterWindow()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var scale = Math.Max(1.0, GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0);
        var width = Math.Min((int)(1180 * scale), area.Width);
        var height = Math.Min((int)(820 * scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2, width, height));
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr window);

    private void UpdateTitleBar()
    {
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = _accessibility.HighContrast
            ? new UISettings().GetColorValue(UIColorType.Foreground)
            : Root.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
    }

    private void Accessibility_HighContrastChanged(AccessibilitySettings sender, object args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_lifetime.IsCancellationRequested)
            {
                UpdateTitleBar();
                UpdateLayout();
            }
        });

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        Steps.SelectedIndex = 0;
        ShowStep(0);
        UpdateLayout();
        await RunAsync("Loading local setup…", RefreshAccountsAsync);
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_loaded) UpdateLayout();
    }

    private void UpdateLayout()
    {
        var compact = Root.ActualWidth < 1000;
        RailColumn.Width = new GridLength(compact ? 76 : 220);
        RailLayout.Padding = new Thickness(compact ? 6 : 14, 16, compact ? 6 : 14, 16);
        foreach (var label in new[] { RailTagline, WelcomeLabel, AccountsLabel, SharingLabel, CodexLabel, PrivacyLabel, AboutLabel, ReadOnlyLabel })
            label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        RailArtwork.Visibility = compact || Root.ActualHeight < 700 || _accessibility.HighContrast
            ? Visibility.Collapsed : Visibility.Visible;
        ContentLayout.Padding = new Thickness(compact ? 20 : 32, 16, compact ? 20 : 32, 20);
        var availableWidth = Math.Max(0, Root.ActualWidth - RailColumn.Width.Value - ContentLayout.Padding.Left - ContentLayout.Padding.Right);
        var showHero = availableWidth >= 730 && Root.ActualHeight >= 690 && !_accessibility.HighContrast;
        WelcomeArtwork.Visibility = showHero ? Visibility.Visible : Visibility.Collapsed;
        HeroColumn.Width = showHero ? new GridLength(0.9, GridUnitType.Star) : new GridLength(0);
        WelcomeHero.MinHeight = showHero ? 300 : 250;
        WelcomeHeading.FontSize = availableWidth < 500 ? 32 : 40;
        RequestAccessLabel.Visibility = availableWidth < 440 ? Visibility.Collapsed : Visibility.Visible;
        _narrowSharing = availableWidth < 750;
        UpdateSharingLayout();

    }

    private void ShowStep(int step)
    {
        _step = step;
        WelcomePage.Visibility = ToVisibility(step == 0);
        AccountsPage.Visibility = ToVisibility(step == 1);
        SharingPage.Visibility = ToVisibility(step == 2);
        CodexPage.Visibility = ToVisibility(step == 3);
        BackButton.Visibility = ToVisibility(step > 0);
        BackButton.IsEnabled = step > 0 && !_busy;
        PreviewLabel.Visibility = ToVisibility(step == 0);
        NextButton.Content = step switch
        {
            0 => "Get started →", 1 => "Choose sharing →", 2 => "Connect to Codex →", _ => "Close setup"
        };
        NextButton.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[
            step == 3 ? "DefaultButtonStyle" : "AccentButtonStyle"];
        PageScroll.ChangeView(null, 0, null, true);
        UpdateProgress();
    }

    private void Steps_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || Steps.SelectedIndex < 0 || Steps.SelectedIndex == _step) return;
        if (_busy || !CanLeaveSharing())
        {
            Steps.SelectedIndex = _step;
            return;
        }
        if (_step == 0) _welcomeReviewed = true;
        if (_step == 2 && _accounts.Count > 0) _sharingReviewed = true;
        ShowStep(Steps.SelectedIndex);
    }

    private bool CanLeaveSharing()
    {
        if (!_sharingDirty) return true;
        SetNotice("Unsaved sharing choices", "Save or discard this account's changes before continuing.", InfoBarSeverity.Warning);
        return false;
    }

    private void UpdateProgress()
    {
        var icons = new[] { WelcomeIcon, AccountsIcon, SharingIcon, CodexIcon };
        string[] glyphs = ["\uE80F", "\uE77B", "\uE716", "\uE943"];
        string[] labels = ["Welcome", "Accounts", "Sharing", "Connect to Codex"];
        bool[] completed = [_welcomeReviewed, _accounts.Count > 0, _sharingReviewed && !_sharingDirty, _codexStatus?.IsPluginConfigured == true];
        var contiguous = 0;
        while (contiguous < 3 && completed[contiguous]) contiguous++;
        ProgressLine.Height = contiguous * 52;
        for (var index = 0; index < icons.Length; index++)
        {
            var active = index == _step;
            icons[index].Glyph = completed[index] && !active ? "\uE73E" : glyphs[index];
            icons[index].Foreground = ThemeBrush(active || completed[index] ? "WizardAccentBrush" : "TextFillColorSecondaryBrush");
            var item = (ListBoxItem)Steps.Items[index];
            AutomationProperties.SetName(item, labels[index]);
            AutomationProperties.SetItemStatus(item, active ? "Current stage" : completed[index] ? "Completed" : "Upcoming");
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Steps.SelectedIndex = Math.Max(0, _step - 1);
    private void ReviewSharingButton_Click(object sender, RoutedEventArgs e) => Steps.SelectedIndex = 2;

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 3) Close();
        else Steps.SelectedIndex = _step + 1;
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !_sharingDirty) return;
        args.Cancel = true;
        if (_busy || _dialogOpen) return;
        var result = await ShowDialogAsync(new ContentDialog
        {
            Title = "Discard unsaved choices?",
            Content = Body("Your saved sharing settings will stay unchanged."),
            PrimaryButtonText = "Discard and close",
            CloseButtonText = "Keep editing",
            DefaultButton = ContentDialogButton.Close
        });
        if (result == ContentDialogResult.Primary)
        {
            _allowClose = true;
            Close();
        }
    }

    private async Task RefreshAccountsAsync(CancellationToken cancellationToken)
    {
        var accounts = await _application.ListAccountsAsync(cancellationToken);
        var providers = await _application.ListProviderSetupAsync(cancellationToken);
        var settings = await _application.ListAccountSharingAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _accounts = accounts;
        _providers = providers;
        _sharing = settings.ToDictionary(item => item.AccountId, StringComparer.Ordinal);
        var connectedIds = accounts.Select(account => account.Id).ToHashSet(StringComparer.Ordinal);
        _connectionChecks = _connectionChecks
            .Where(pair => connectedIds.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        ProviderStatusText.Text = string.Join(" · ", new[] { "google", "microsoft" }.Select(id =>
            $"{ProviderName(id)}: {(providers.Any(provider => provider.ProviderId == id && provider.Configured) ? "configured" : "setup needed")}"));
        _sharingDirty = false;
        _sharingReviewed = false;
        SelectSharingAccount(_accounts.FirstOrDefault(account => account.Id == _selectedAccount?.Id) ?? _accounts.FirstOrDefault());
        UpdateLayout();
        RenderConnectedAccounts();
        RenderSharingAccounts();
        UpdateSharingSummary();
        UpdateProgress();
    }

    private void RenderConnectedAccounts()
    {
        AccountsCountText.Text = $"{_accounts.Count} {(_accounts.Count == 1 ? "account" : "accounts")}";
        CheckConnectionsButton.IsEnabled = _accounts.Count > 0;
        ConnectedAccounts.Children.Clear();
        foreach (var account in _accounts)
        {
            var row = AccountRow(account, includeStatus: false);
            var sharingStatus = IsShared(account) ? "Shared" : "Sharing off";
            var connection = _connectionChecks.GetValueOrDefault(account.Id);
            var connectionStatus = ConnectionStatusText(connection);
            var status = new TextBlock
            {
                Text = connectionStatus is null ? sharingStatus : $"{connectionStatus} · {sharingStatus}",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ConnectionStatusBrush(connection)
            };
            if (connection is { Reachable: false })
            {
                ToolTipService.SetToolTip(status, ConnectionFailureTooltip(connection));
            }
            Grid.SetColumn(status, 2);
            row.Children.Add(status);
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            actions.Children.Add(AccountActionButton(
                "Reconnect",
                $"Reconnect {account.EmailAddress}",
                "Reconnect this account with the same provider account",
                account,
                ReconnectAccountButton_Click));
            actions.Children.Add(AccountActionButton(
                "Remove",
                $"Remove {account.EmailAddress} from this device",
                "Remove this account and its local credentials from this device",
                account,
                RemoveAccountButton_Click));
            Grid.SetColumn(actions, 3);
            row.Children.Add(actions);
            ConnectedAccounts.Children.Add(new Border
            {
                Child = row, BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush"), Padding = new Thickness(8, 8, 8, 8)
            });
        }
        if (_accounts.Count == 0)
            ConnectedAccounts.Children.Add(Body("Add your first account above."));
    }

    private Grid AccountRow(Account account, bool includeStatus)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = new Image { Source = ProviderLogo(account.Provider), Width = 24, Height = 24 };
        AutomationProperties.SetName(logo, ProviderName(account.Provider));
        row.Children.Add(logo);
        var details = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var address = new TextBlock { Text = account.EmailAddress, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTipService.SetToolTip(address, account.EmailAddress);
        AutomationProperties.SetName(address, account.EmailAddress);
        details.Children.Add(address);
        if (includeStatus)
            details.Children.Add(new TextBlock { Text = IsShared(account) ? "Shared" : "Sharing off", FontSize = 12, Foreground = ThemeBrush("TextFillColorSecondaryBrush") });
        Grid.SetColumn(details, 1);
        row.Children.Add(details);
        return row;
    }

    private Button AccountActionButton(
        string text,
        string automationName,
        string tooltip,
        Account account,
        RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = text,
            Tag = account,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["QuietButton"],
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(button, automationName);
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += handler;
        return button;
    }

    private static string? ConnectionStatusText(AccountConnectionCheck? connection)
    {
        if (connection is null)
            return null;

        var services = new List<string>(2);
        if (connection.MailReachable is bool mailReachable)
            services.Add($"Mail: {(mailReachable ? "OK" : "needs attention")}");
        if (connection.CalendarReachable is bool calendarReachable)
            services.Add($"Calendar: {(calendarReachable ? "OK" : "needs attention")}");
        if (services.Count > 0)
            return string.Join(" · ", services);

        return connection switch
        {
            { Reachable: true } => "Connected",
            { FailureKind: ReadFailureKind.SignInRequired or ReadFailureKind.AccessDenied or ReadFailureKind.LocalCredentialsUnavailable } => "Reconnect needed",
            _ => "Needs attention"
        };
    }

    private static string ConnectionFailureTooltip(AccountConnectionCheck connection)
    {
        var actions = new List<string>(2);
        if (connection.MailReachable == false)
            actions.Add($"Mail: {ReadFailureGuidance.Describe(connection.MailFailureKind ?? connection.FailureKind ?? ReadFailureKind.Unknown).Action}");
        if (connection.CalendarReachable == false)
            actions.Add($"Calendar: {ReadFailureGuidance.Describe(connection.CalendarFailureKind ?? connection.FailureKind ?? ReadFailureKind.Unknown).Action}");
        if (actions.Count > 0)
            return string.Join(" ", actions);

        return ReadFailureGuidance.Describe(connection.FailureKind ?? ReadFailureKind.Unknown).Action;
    }

    private static Brush ConnectionStatusBrush(AccountConnectionCheck? connection) => connection switch
    {
        { Reachable: true } => ThemeBrush("WizardAccentBrush"),
        { Reachable: false } => new SolidColorBrush(Microsoft.UI.Colors.IndianRed),
        _ => ThemeBrush("TextFillColorSecondaryBrush")
    };

    private static ImageSource ProviderLogo(string provider) =>
        new SvgImageSource(new Uri($"ms-appx:///Assets/{(provider == "google" ? "Google" : "Microsoft")}.svg"));

    private bool IsShared(Account account)
    {
        var setting = _sharing.GetValueOrDefault(account.Id) ?? new AccountSharingSettings(account.Id);
        return setting.Enabled && (setting.ShareMail && account.MailReadEnabled
            || setting.ShareCalendars && account.CalendarReadEnabled && setting.CalendarIds is not { Count: 0 });
    }

    private void UpdateSharingSummary()
    {
        var shared = _accounts.Count(IsShared);
        CodexSharingText.Text = shared == 0 ? "No accounts shared yet." : $"Sharing: {shared} {(shared == 1 ? "account" : "accounts")}";
    }

    private async void CheckConnectionsButton_Click(object sender, RoutedEventArgs e) => await RunAsync(
        "Checking Mail and Calendar read access…",
        async cancellationToken =>
        {
            var result = await _application.CheckConnectionsAsync(cancellationToken);
            _connectionChecks = result.Accounts.ToDictionary(check => check.AccountId, StringComparer.Ordinal);
            RenderConnectedAccounts();
            var unavailable = result.Accounts.Count(check => !check.Reachable);
            var mailFailures = result.Accounts.Count(check => check.MailReachable == false);
            var calendarFailures = result.Accounts.Count(check => check.CalendarReachable == false);
            if (result.Accounts.Count == 0)
            {
                SetNotice("No accounts to check", "Connect an account first, then check its connection here.", InfoBarSeverity.Informational);
            }
            else if (unavailable == 0)
            {
                SetNotice("Read access checked", "Mail and calendar read access succeeded for every enabled account. Expired access tokens were refreshed silently when possible.", InfoBarSeverity.Success);
            }
            else
            {
                var failures = new List<string>(2);
                if (mailFailures > 0)
                    failures.Add($"{mailFailures} mail access {(mailFailures == 1 ? "check" : "checks")} failed");
                if (calendarFailures > 0)
                    failures.Add($"{calendarFailures} calendar access {(calendarFailures == 1 ? "check" : "checks")} failed");
                var detail = failures.Count == 0
                    ? "Some account read checks need attention."
                    : string.Join("; ", failures) + ".";
                SetNotice("Some read capabilities need attention", $"{detail} MailMeUp checked provider read access for each enabled capability (Microsoft Graph: Mail.Read and Calendars.Read). Review the affected account and sign in again if needed.", InfoBarSeverity.Warning);
            }
        },
        TimeSpan.FromMinutes(2.5));
    private async void GoogleButton_Click(object sender, RoutedEventArgs e) => await ConnectAsync("google");
    private async void MicrosoftButton_Click(object sender, RoutedEventArgs e) => await ConnectAsync("microsoft");

    private async void ReconnectAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _dialogOpen || sender is not Button { Tag: Account account }) return;
        if (!CanLeaveSharing()) return;
        await ConnectAsync(account.Provider, account);
    }

    private async void RemoveAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _dialogOpen || sender is not Button { Tag: Account account }) return;
        if (!CanLeaveSharing()) return;

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(Body($"Remove {account.EmailAddress} from MailMeUp on this device?"));
        content.Children.Add(Body("This removes the local account record and protected sign-in cache. It does not delete or change email, calendars or provider consent. You can connect the account again later."));
        var confirmation = await ShowDialogAsync(new ContentDialog
        {
            Title = "Remove account?",
            Content = content,
            PrimaryButtonText = "Remove from device",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        });
        if (confirmation != ContentDialogResult.Primary) return;

        await RunAsync($"Removing {account.EmailAddress} from this device…", async cancellationToken =>
        {
            var removal = await _application.RemoveAccountAsync(account.Id, cancellationToken);
            if (!removal.Removed)
            {
                SetNotice("Account not found", "The local account was already removed. Refresh the account list and connect it again if needed.", InfoBarSeverity.Informational);
                await RefreshAccountsAsync(cancellationToken);
                return;
            }

            if (_selectedAccount?.Id == account.Id)
            {
                _selectedAccount = null;
                _selectedCalendarIds.Clear();
            }
            _connectionChecks.Remove(account.Id);
            await RefreshAccountsAsync(cancellationToken);
            SetNotice("Account removed", "The local account and protected credentials were removed. Provider consent and account data are unchanged; connect it again above when needed.", InfoBarSeverity.Success);
        }, TimeSpan.FromMinutes(2.5));
    }

    private async Task ConnectAsync(string provider, Account? reconnectAccount = null)
    {
        var includeMail = reconnectAccount?.MailReadEnabled ?? RequestMail.IsChecked == true;
        var includeCalendar = reconnectAccount?.CalendarReadEnabled ?? RequestCalendars.IsChecked == true;
        if (reconnectAccount is not null && !includeMail && !includeCalendar)
        {
            includeMail = true;
            includeCalendar = true;
        }
        if (reconnectAccount is null && !includeMail && !includeCalendar)
        {
            SetNotice("Choose read access", "Select mail, calendars, or both before signing in.", InfoBarSeverity.Warning);
            return;
        }
        var activity = reconnectAccount is null
            ? $"Connecting {ProviderName(provider)} — finish sign-in in your browser…"
            : $"Reconnecting {reconnectAccount.EmailAddress} — choose the same account in your browser…";
        await RunAsync(activity, async cancellationToken =>
        {
            var setup = await _application.ListProviderSetupAsync(cancellationToken);
            if (!setup.Any(item => item.ProviderId == provider && item.Configured))
            {
                var configured = provider == "google"
                    ? await ConfigureGoogleAsync(cancellationToken)
                    : await ConfigureMicrosoftAsync(cancellationToken, signIn: true);
                if (!configured) return;
            }
            var connection = await _application.ConnectAccountAsync(provider,
                new AccountConnectionOptions(includeMail, includeCalendar, ShareWithAssistant: false), cancellationToken);
            _connectionChecks.Remove(connection.Account.Id);
            await RefreshAccountsAsync(cancellationToken);
            if (reconnectAccount is not null && !string.Equals(connection.Account.Id, reconnectAccount.Id, StringComparison.Ordinal))
            {
                SetNotice("Different account connected", $"{connection.Account.EmailAddress} was connected. The original account remains in the list.", InfoBarSeverity.Warning);
            }
            else
            {
                SetNotice(
                    reconnectAccount is null ? "Account connected" : "Account reconnected",
                    reconnectAccount is null
                        ? "New accounts start with sharing off. Reconnected accounts keep their saved choices."
                        : "The local sign-in was renewed and the saved sharing choices were kept.",
                    InfoBarSeverity.Success);
            }
        }, TimeSpan.FromMinutes(5));
    }

    private async Task<bool> ConfigureGoogleAsync(CancellationToken cancellationToken)
    {
        ActivityText.Text = "Select your Google Desktop OAuth client JSON…";
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads, ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (file is null) return false;
        await _application.ConfigureProviderAsync("google", file.Path, cancellationToken);
        SetNotice("Google app configured", "The original JSON stays in its folder. Keep it private.", InfoBarSeverity.Informational);
        ActivityText.Text = "Google app configured.";
        return true;
    }

    private async Task<bool> ConfigureMicrosoftAsync(CancellationToken cancellationToken, bool signIn)
    {
        var input = new TextBox { Header = "Application (client) ID", PlaceholderText = "00000000-0000-0000-0000-000000000000" };
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(Body("Enter the public client ID of your Microsoft desktop app. No client secret is needed."));
        content.Children.Add(input);
        var dialog = new ContentDialog
        {
            Title = "Set up Microsoft sign-in", Content = content,
            PrimaryButtonText = signIn ? "Save and sign in" : "Save",
            CloseButtonText = "Cancel", IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Primary
        };
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = Guid.TryParseExact(input.Text.Trim(), "D", out _);
        using var registration = cancellationToken.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return false;
        cancellationToken.ThrowIfCancellationRequested();
        await _application.ConfigureProviderAsync("microsoft", input.Text.Trim(), cancellationToken);
        return true;
    }

    private async void RefreshCodexButton_Click(object sender, RoutedEventArgs e) =>
        await RunAsync("Reading local Codex configuration…", async token => ApplyCodexStatus(await _codex.GetStatusAsync(token)));

    private void ApplyCodexStatus(CodexSetupStatus status)
    {
        _codexStatus = status;
        CodexStatusTitle.Text = status.Code switch
        {
            "ReadyToInstall" => "Plugin not installed",
            "PluginConfigured" => "Plugin installed and enabled",
            "PluginDisabled" => "Plugin is disabled",
            "DirectRegistrationExists" => "Existing connection found",
            "AliasUnavailable" => "Windows alias unavailable",
            "CodexUnavailable" => "Manual setup needed",
            _ => "Setup needs attention"
        };
        CodexStatusText.Text = status.Code switch
        {
            "ReadyToInstall" => "Ready for local installation.",
            "PluginConfigured" => "Start a new Codex task to load the tools. Runtime connection is not confirmed here.",
            "PluginDisabled" => "Enable MailMeUp in Codex, then refresh status.",
            "DirectRegistrationExists" => "Review the direct MCP connection in Codex before adding this plugin.",
            "CodexUnavailable" => "Open manual setup for the available installation route.",
            _ => "Open installation details for the next steps."
        };
        InstallPluginButton.IsEnabled = status.CanInstall;
        InstallPluginButton.Content = status.IsPluginConfigured ? "Update local plugin" : "Install local plugin";
        UpdateProgress();
    }

    private async void InstallPluginButton_Click(object sender, RoutedEventArgs e) =>
        await RunAsync("Installing the local Codex plugin…", async token =>
        {
            var result = await _codex.InstallPluginAsync(token);
            ApplyCodexStatus(result.Status);
            SetNotice(result.Success ? "Plugin configured" : "Setup needs attention", result.Message,
                result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }, TimeSpan.FromMinutes(2));

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private async Task RunAsync(string activity, Func<CancellationToken, Task> action, TimeSpan? timeout = null)
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        _busy = true;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        operation.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        PageHost.IsEnabled = Steps.IsEnabled = NextButton.IsEnabled = BackButton.IsEnabled = AboutButton.IsEnabled = PrivacyButton.IsEnabled = false;
        ActivityText.Text = activity;
        Activity.Visibility = Visibility.Visible;
        Notice.IsOpen = false;
        try
        {
            await action(operation.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested)
                SetNotice("Operation stopped", "The operation was cancelled or timed out. Completed changes remain saved.", InfoBarSeverity.Warning);
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Desktop setup operation failed ({ErrorType})", exception.GetType().Name);
            if (!_lifetime.IsCancellationRequested)
                SetNotice("Could not complete setup", "Check provider setup, network and local storage access, then retry. Saved choices are kept.", InfoBarSeverity.Error);
        }
        finally
        {
            _operation = null;
            _busy = false;
            if (!_lifetime.IsCancellationRequested)
            {
                PageHost.IsEnabled = Steps.IsEnabled = NextButton.IsEnabled = AboutButton.IsEnabled = PrivacyButton.IsEnabled = true;
                BackButton.IsEnabled = _step > 0;
                Activity.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void SetNotice(string title, string message, InfoBarSeverity severity)
    {
        Notice.Title = title;
        Notice.Message = message;
        Notice.Severity = severity;
        Notice.IsOpen = true;
    }

    private static Visibility ToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private static Brush ThemeBrush(string key) => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
    private static string ProviderName(string provider) => provider == "google" ? "Google" : provider == "microsoft" ? "Microsoft" : provider;
    private static TextBlock Body(string text) => new() { Text = text, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["BodyText"] };
}
