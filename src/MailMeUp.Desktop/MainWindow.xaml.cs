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
    private bool _stackAccountActions;
    private bool _highContrastSubscribed;
    private bool IsDemo => _application is DemoMailMeUpApplication;

    /// <summary>Creates the setup window without starting sign-in or reading mailbox content.</summary>
    public MainWindow(IMailMeUpApplication application, CodexSetupService codex, ILogger<MainWindow> logger)
    {
        _application = application;
        _codex = codex;
        _logger = logger;
        InitializeComponent();
        InitializePageScrolling();
        InitializeReadGuardrails();
        SettingsContent.SizeChanged += (_, _) => UpdateSettingsLayout();
        DemoBanner.IsOpen = IsDemo;
        if (IsDemo) Title = "MailMeUp — UI preview";
        MailSearchLookbackDaysBox.Minimum = MailSearchPreferences.MinimumDays;
        MailSearchLookbackDaysBox.Maximum = MailSearchPreferences.MaximumDays;
        MailSearchLookbackDaysBox.RegisterPropertyChangedCallback(NumberBox.TextProperty,
            (_, _) => UpdateMailSearchPreferencesDirty());
        RenderCodexChecks(CodexSetupCheck.Pending());
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
            StopObservingDisplayRoot();
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
        var scale = Root.XamlRoot?.RasterizationScale
            ?? GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        AppTitleBar.Padding = new Thickness(20 + AppWindow.TitleBar.LeftInset / scale, 0,
            Math.Max(16, AppWindow.TitleBar.RightInset / scale + 12), 0);
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
        ObserveDisplayRoot();
        if (_loaded) return;
        _loaded = true;
        Steps.SelectedIndex = 0;
        ShowStep(0);
        UpdateLayout();
        await RunAsync("Loading local setup…", RefreshAccountsAsync);
        ApplyPendingStep();
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_loaded && !_displayRefreshQueued) UpdateLayout();
    }

    private void UpdateLayout()
    {
        var compact = Root.ActualWidth < 900;
        RailColumn.Width = new GridLength(compact ? 68 : 184);
        RailLayout.Padding = new Thickness(compact ? 6 : 12, 28, compact ? 6 : 12, 20);
        foreach (var label in new[] { RailCaption, WelcomeLabel, AccountsLabel, SharingLabel, CodexLabel, HelpLabel, ReadOnlyLabel })
            label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ContentLayout.Padding = new Thickness(compact ? 24 : 40, 28, compact ? 24 : 40, 24);
        var availableWidth = Math.Max(0, Math.Min(1080, Root.ActualWidth - RailColumn.Width.Value)
            - ContentLayout.Padding.Left - ContentLayout.Padding.Right);
        var showHero = availableWidth >= 690 && Root.ActualHeight >= 650 && !_accessibility.HighContrast;
        WelcomeArtwork.Visibility = ToVisibility(showHero);
        HeroColumn.Width = new GridLength(showHero ? 280 : 0);
        WelcomeHero.MinHeight = showHero ? 250 : 220;
        PageTitle.FontSize = _step == 0 ? (availableWidth < 500 ? 30 : 36) : (availableWidth < 500 ? 26 : 30);
        UpdateSettingsLayout();
        UpdateAddAccountLayout();
        CodexActions.Orientation = availableWidth < 620 ? Orientation.Vertical : Orientation.Horizontal;
        CodexSecondaryActions.Orientation = availableWidth < 440 ? Orientation.Vertical : Orientation.Horizontal;
        var stackActions = availableWidth < 520;
        AccountsActionsColumn.Width = stackActions ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(AccountsToolbarActions, stackActions ? 0 : 1);
        Grid.SetRow(AccountsToolbarActions, stackActions ? 1 : 0);
        AccountsToolbarActions.Orientation = availableWidth < 320 ? Orientation.Vertical : Orientation.Horizontal;
        CodexSharingActionColumn.Width = stackActions ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(ReviewSharingButton, stackActions ? 0 : 1);
        Grid.SetRow(ReviewSharingButton, stackActions ? 1 : 0);
        ReviewSharingButton.HorizontalAlignment = stackActions ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        PreviewLabel.Visibility = ToVisibility(availableWidth >= 600);
        if (_stackAccountActions != stackActions)
        {
            _stackAccountActions = stackActions;
            RenderConnectedAccounts();
        }
        UpdateSharingLayout();
        UpdateActiveDialogLayout();
        RefreshPageScrollChrome();
    }

    private void ShowStep(int step)
    {
        _step = step;
        PageTitle.Text = step switch
        {
            0 => "All your inboxes.\nOne conversation.",
            1 => "Your accounts",
            2 => "What you share",
            _ => "Connect to Codex"
        };
        PageSubtitle.Text = step switch
        {
            0 => "Bring your mail and calendars into Codex.",
            1 => "Connect personal, work and client accounts.",
            2 => "Choose what Codex can read from each account.",
            _ => "Make your shared accounts available in your conversations."
        };
        WelcomePage.Visibility = ToVisibility(step == 0);
        AccountsPage.Visibility = ToVisibility(step == 1);
        SharingPage.Visibility = ToVisibility(step == 2);
        CodexPage.Visibility = ToVisibility(step == 3);
        BackButton.Visibility = ToVisibility(step > 0);
        BackButton.IsEnabled = step > 0 && !_busy;
        PreviewLabel.Text = IsDemo ? "Sample data · changes stay in this preview" : step switch
        {
            0 => "Your choices stay on this device",
            1 => "Next, choose what to share",
            2 => "Save your choices before continuing",
            _ => "You can return to setup at any time"
        };
        NextButton.Content = step switch
        {
            0 => "Get started", 1 => "Choose sharing", 2 => "Connect to Codex", _ => "Done"
        };
        NextButton.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[
            step == 3 ? "DefaultButtonStyle" : "AccentButtonStyle"];
        PageScroll.ChangeView(null, 0, null, true);
        UpdateLayout();
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
        if (_sharingDirty)
        {
            if (_sharingDialog is null) _ = ShowSharingEditorAsync();
            return false;
        }
        if (_mailSearchPreferencesDirty)
        {
            _ = ShowSharingSettingsAsync();
            return false;
        }
        if (_readGuardrailsDirty)
        {
            _ = ShowSharingSettingsAsync();
            return false;
        }
        return true;
    }

    private void UpdateProgress()
    {
        UpdateSharingSettingsSummary();
        if (_step == 2 && !IsDemo)
            PreviewLabel.Text = _sharingDirty || _mailSearchPreferencesDirty || _readGuardrailsDirty
                ? "You have unsaved changes"
                : "You control what Codex can read";
        var icons = new[] { WelcomeIcon, AccountsIcon, SharingIcon, CodexIcon };
        string[] glyphs = ["\uE80F", "\uE77B", "\uE716", "\uE943"];
        string[] labels = ["Welcome", "Accounts", "Sharing", "Connect to Codex"];
        bool[] completed = [_welcomeReviewed, _accounts.Count > 0, _sharingReviewed && !_sharingDirty && !_mailSearchPreferencesDirty && !_readGuardrailsDirty, _codexStatus is { Code: "PluginConfigured", IsPluginConfigured: true }];
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
        if (_allowClose || (!_sharingDirty && !_mailSearchPreferencesDirty && !_readGuardrailsDirty)) return;
        args.Cancel = true;
        if (_busy || _dialogOpen) return;
        var result = await ShowDialogAsync(new ContentDialog
        {
            Title = "Discard unsaved choices?",
            Content = Body("Your saved sharing choices, default mail search period and read limits will stay unchanged."),
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
        await LoadMailSearchPreferencesAsync(cancellationToken);
        await LoadReadGuardrailsAsync(cancellationToken);
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
        CheckConnectionsButton.IsEnabled = _accounts.Count > 0 && !IsDemo;
        CheckConnectionsButton.Visibility = ToVisibility(_accounts.Count > 0);
        AddAccountButton.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[
            _accounts.Count == 0 ? "AccentButtonStyle" : "DefaultButtonStyle"];
        ConnectedAccounts.Children.Clear();
        foreach (var account in _accounts)
        {
            var row = AccountRow(account, includeStatus: false);
            var connection = _connectionChecks.GetValueOrDefault(account.Id);
            if (NeedsReadAttention(connection))
            {
                var reconnect = AccountActionButton(
                    "Try to reconnect", $"Try to reconnect {account.EmailAddress}",
                    "Try to reconnect", account, ReconnectAccountButton_Click);
                reconnect.Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
                PlaceAccountNotice(reconnect);
                row.Children.Add(reconnect);
            }
            else if (GetReadFailures(connection).Contains(ReadFailureKind.RateLimited))
            {
                var limited = AccountActionButton(
                    "Read limit reached", $"Read limit reached for {account.EmailAddress}; show guidance",
                    "Wait before trying again. A shorter search period makes fewer requests.",
                    account, ReadRateLimitButton_Click);
                PlaceAccountNotice(limited);
                row.Children.Add(limited);
            }
            else if (GetReadFailures(connection).Contains(ReadFailureKind.BudgetExceeded))
            {
                var limited = AccountActionButton(
                    "Read budget reached", $"Read budget reached for {account.EmailAddress}; show guidance",
                    "The local request budget has been reached. Wait for its window to reset.",
                    account, ReadBudgetButton_Click);
                PlaceAccountNotice(limited);
                row.Children.Add(limited);
            }
            var menu = new MenuFlyout();
            var reconnectItem = new MenuFlyoutItem { Text = "Reconnect", Tag = account };
            reconnectItem.Click += ReconnectAccountButton_Click;
            menu.Items.Add(reconnectItem);
            var removeItem = new MenuFlyoutItem { Text = "Remove from device", Tag = account };
            removeItem.Click += RemoveAccountButton_Click;
            menu.Items.Add(removeItem);
            var actions = new Button
            {
                Content = new FontIcon { Glyph = "\uE712", FontSize = 16 },
                Flyout = menu,
                Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["QuietButton"],
                Padding = new Thickness(8),
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(actions, $"Account actions for {account.EmailAddress}");
            ToolTipService.SetToolTip(actions, "Account actions");
            Grid.SetColumn(actions, 3);
            row.Children.Add(actions);
            ConnectedAccounts.Children.Add(new Border
            {
                Child = row, BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = ThemeBrush("DividerStrokeColorDefaultBrush"), Padding = new Thickness(0, 12, 0, 12)
            });
        }
        if (_accounts.Count == 0)
        {
            var empty = new StackPanel { Spacing = 12, Margin = new Thickness(0, 28, 0, 28) };
            empty.Children.Add(new FontIcon { Glyph = "\uE715", FontSize = 36, HorizontalAlignment = HorizontalAlignment.Left, Foreground = ThemeBrush("TextFillColorSecondaryBrush") });
            empty.Children.Add(new TextBlock { Text = "Start with one account", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            empty.Children.Add(Body("Choose Add account to connect Google or Microsoft."));
            ConnectedAccounts.Children.Add(empty);
        }
    }

    private void PlaceAccountNotice(FrameworkElement notice)
    {
        Grid.SetColumn(notice, _stackAccountActions ? 1 : 2);
        Grid.SetRow(notice, _stackAccountActions ? 1 : 0);
        Grid.SetColumnSpan(notice, _stackAccountActions ? 2 : 1);
        notice.HorizontalAlignment = HorizontalAlignment.Left;
    }

    private Grid AccountRow(Account account, bool includeStatus)
    {
        var row = new Grid { ColumnSpacing = 14, RowSpacing = 6, MinHeight = 36 };
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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

    private static bool NeedsReadAttention(AccountConnectionCheck? check) =>
        GetReadFailures(check).Any(kind => kind is not (ReadFailureKind.ResultLimit or ReadFailureKind.RateLimited or ReadFailureKind.BudgetExceeded));

    private static IReadOnlyList<ReadFailureKind> GetReadFailures(AccountConnectionCheck? check)
    {
        if (check is null || !check.HasFailures)
            return [];
        // Missing samples, bounded checks and rate limits are not broken connections.
        var failures = new List<ReadFailureKind>();
        if (check.MailReachable == false || check.MailFailureKind is not null)
            failures.Add(check.MailFailureKind ?? check.FailureKind ?? ReadFailureKind.Unknown);
        if (check.CalendarReachable == false || check.CalendarFailureKind is not null)
            failures.Add(check.CalendarFailureKind ?? check.FailureKind ?? ReadFailureKind.Unknown);
        if (failures.Count == 0)
            failures.Add(check.FailureKind ?? ReadFailureKind.Unknown);
        return failures;
    }

    private void ReadRateLimitButton_Click(object sender, RoutedEventArgs e) =>
        SetNotice("Read limit reached",
            "Your provider is temporarily limiting requests. Wait before trying again, use a shorter mail search period and check read access later. This error does not require reconnecting the account.",
            InfoBarSeverity.Warning);

    private void ReadBudgetButton_Click(object sender, RoutedEventArgs e) =>
        SetNotice("Read budget reached", ReadFailureGuidance.Describe(ReadFailureKind.BudgetExceeded).Action, InfoBarSeverity.Warning);

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
        "Checking read access…",
        async cancellationToken =>
        {
            if (BlockDemoAction()) return;
            // A failed or cancelled retry must never retain a previous successful result.
            _connectionChecks.Clear();
            RenderConnectedAccounts();
            try
            {
                var result = await _application.CheckConnectionsAsync(cancellationToken);
                _connectionChecks = result.Accounts.ToDictionary(check => check.AccountId, StringComparer.Ordinal);
                foreach (var account in _accounts)
                {
                    if (!_connectionChecks.ContainsKey(account.Id))
                        _connectionChecks[account.Id] = UnavailableCheck(account);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Desktop read check cancelled; previous results cleared");
            }
            catch (Exception exception)
            {
                _logger.LogWarning("Desktop read check failed ({ErrorType}); no successful result assumed", exception.GetType().Name);
                _connectionChecks = _accounts.ToDictionary(account => account.Id, UnavailableCheck, StringComparer.Ordinal);
            }
            for (var index = 0; index < _accounts.Count; index++)
            {
                var account = _accounts[index];
                var check = _connectionChecks.GetValueOrDefault(account.Id);
                _logger.LogInformation(
                    "Desktop read check row={AccountRow}; account={AccountKey}; resultPresent={ResultPresent}; failures={HasFailures}; mail={MailReachable}/{MailFailureCategory}/{MailEvidence}; calendar={CalendarReachable}/{CalendarFailureCategory}/{CalendarEvidence}",
                    index + 1, MailMeUp.Diagnostics.ReadDiagnostics.AccountKey(account.Id), check is not null,
                    check?.HasFailures, check?.MailReachable, check?.MailFailureKind, check?.MailEvidence,
                    check?.CalendarReachable, check?.CalendarFailureKind, check?.CalendarEvidence);
            }
            RenderConnectedAccounts();
            Notice.IsOpen = false;
        },
        TimeSpan.FromMinutes(2.5));

    private static AccountConnectionCheck UnavailableCheck(Account account) => new(account.Id, false,
        ReadFailureKind.Unknown, account.MailReadEnabled ? false : null,
        account.MailReadEnabled ? ReadFailureKind.Unknown : null, account.CalendarReadEnabled ? false : null,
        account.CalendarReadEnabled ? ReadFailureKind.Unknown : null);


    private async void ReconnectAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _dialogOpen || sender is not FrameworkElement { Tag: Account account }) return;
        if (!CanLeaveSharing()) return;
        await ConnectAsync(account.Provider, account);
    }

    private async void RemoveAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (BlockDemoAction()) return;
        if (_busy || _dialogOpen || sender is not FrameworkElement { Tag: Account account }) return;
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
        if (BlockDemoAction()) return;
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


    private void CancelButton_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private async Task RunAsync(string activity, Func<CancellationToken, Task> action, TimeSpan? timeout = null)
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        _busy = true;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        operation.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        PageHost.IsEnabled = Steps.IsEnabled = NextButton.IsEnabled = BackButton.IsEnabled = AboutButton.IsEnabled = PrivacyButton.IsEnabled = HelpButton.IsEnabled = false;
        ActivityText.Text = activity;
        Activity.Visibility = Visibility.Visible;
        Notice.IsOpen = false;
        UpdateSettingsActivity(activity);
        UpdateSharingActivity(activity);
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
                PageHost.IsEnabled = Steps.IsEnabled = NextButton.IsEnabled = AboutButton.IsEnabled = PrivacyButton.IsEnabled = HelpButton.IsEnabled = true;
                BackButton.IsEnabled = _step > 0;
                Activity.Visibility = Visibility.Collapsed;
                UpdateSettingsActivity(string.Empty);
                UpdateSharingActivity(string.Empty);
                ApplyPendingStep();
            }
        }
    }

    private bool BlockDemoAction()
    {
        if (!IsDemo) return false;
        SetNotice("UI preview", "This action is disabled for sample accounts. Open MailMeUp without --demo to configure real accounts or Codex.", InfoBarSeverity.Informational);
        return true;
    }

    private void SetNotice(string title, string message, InfoBarSeverity severity)
    {
        var notice = _settingsDialog is not null ? _settingsNotice
            : _sharingDialog is not null ? _sharingNotice
            : _addAccountDialog is not null ? _addAccountNotice : Notice;
        notice.Title = title;
        notice.Message = message;
        notice.Severity = severity;
        notice.IsOpen = true;
    }

    private static Visibility ToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private static Brush ThemeBrush(string key) => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
    private static string ProviderName(string provider) => provider == "google" ? "Google" : provider == "microsoft" ? "Microsoft" : provider;
    private static TextBlock Body(string text) => new() { Text = text, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["BodyText"] };
}
