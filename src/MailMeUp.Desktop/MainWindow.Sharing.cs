using MailMeUp.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private Account? _selectedAccount;
    private HashSet<string> _selectedCalendarIds = new(StringComparer.Ordinal);
    private bool _loadingSharing;
    private ContentDialog? _sharingDialog;
    private ScrollViewer? _sharingScroll;
    private CalendarSelectionDialog? _pendingSharingCalendarDialog;
    private readonly InfoBar _sharingNotice = new() { IsClosable = true };
    private readonly TextBlock _sharingActivityText = new() { TextWrapping = TextWrapping.Wrap };
    private Grid? _sharingActivity;

    private void RenderSharingAccounts()
    {
        SharingAccounts.Children.Clear();
        var shared = _accounts.Count(IsShared);
        SharingSummaryText.Text = _accounts.Count == 0
            ? "Connect an account to choose what to share."
            : $"{shared} of {_accounts.Count} {(_accounts.Count == 1 ? "account" : "accounts")} shared";
        foreach (var account in _accounts)
        {
            if (SharingAccounts.Children.Count > 0)
                SharingAccounts.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(52, 0, 12, 0),
                    Background = ThemeBrush("DividerStrokeColorDefaultBrush")
                });

            var summary = SharingAccountDescription(account);
            var row = new Grid { ColumnSpacing = 14 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var logo = new Image { Source = ProviderLogo(account.Provider), Width = 24, Height = 24 };
            AutomationProperties.SetName(logo, ProviderName(account.Provider));
            row.Children.Add(logo);
            var details = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            var address = new TextBlock { Text = account.EmailAddress, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTipService.SetToolTip(address, account.EmailAddress);
            details.Children.Add(address);
            details.Children.Add(new TextBlock
            {
                Text = summary, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush")
            });
            Grid.SetColumn(details, 1);
            row.Children.Add(details);
            var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 12, Foreground = ThemeBrush("TextFillColorSecondaryBrush") };
            Grid.SetColumn(chevron, 2);
            row.Children.Add(chevron);
            var button = new Button
            {
                Content = row,
                Tag = account.Id,
                Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["QuietButton"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(14, 14, 14, 14)
            };
            AutomationProperties.SetName(button, $"{account.EmailAddress}, {ProviderName(account.Provider)}, {summary}. Edit sharing.");
            button.Click += async (_, _) =>
            {
                if (_busy || _dialogOpen || (account.Id != _selectedAccount?.Id && !CanLeaveSharing())) return;
                if (account.Id != _selectedAccount?.Id || !_sharingDirty) SelectSharingAccount(account);
                await ShowSharingEditorAsync();
            };
            SharingAccounts.Children.Add(button);
        }
    }

    private string SharingAccountDescription(Account account)
    {
        var saved = _sharing.GetValueOrDefault(account.Id) ?? new AccountSharingSettings(account.Id);
        if (!saved.Enabled) return "Not shared";
        var categories = new List<string>();
        if (saved.ShareMail && account.MailReadEnabled) categories.Add("Mail");
        if (saved.ShareCalendars && account.CalendarReadEnabled)
            categories.Add(saved.CalendarIds is null ? "All calendars"
                : saved.CalendarIds.Count == 0 ? "No calendars selected"
                : $"{saved.CalendarIds.Count} {(saved.CalendarIds.Count == 1 ? "calendar" : "calendars")}");
        return categories.Count == 0 ? "Nothing shared" : string.Join(" · ", categories);
    }

    private async Task ShowSharingEditorAsync()
    {
        if (_selectedAccount is null || _busy || _dialogOpen || _sharingDialog is not null || _lifetime.IsCancellationRequested) return;

        do
        {
            var surface = new Grid { RowSpacing = 12 };
            surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _sharingScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 14, 0)
            };
            TrackSettingsScrollViewer(_sharingScroll);
            SharingEditorParkingHost.Child = null;
            _sharingScroll.Content = SharingEditorCard;
            surface.Children.Add(_sharingScroll);
            Grid.SetRow(_sharingNotice, 1);
            surface.Children.Add(_sharingNotice);
            _sharingNotice.IsOpen = false;

            _sharingActivity = new Grid { ColumnSpacing = 12, Visibility = Visibility.Collapsed };
            _sharingActivity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _sharingActivity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _sharingActivity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _sharingActivity.Children.Add(new ProgressRing { Width = 20, Height = 20, IsActive = true });
            Grid.SetColumn(_sharingActivityText, 1);
            _sharingActivity.Children.Add(_sharingActivityText);
            var cancel = new Button { Content = "Cancel" };
            cancel.Click += CancelButton_Click;
            Grid.SetColumn(cancel, 2);
            _sharingActivity.Children.Add(cancel);
            Grid.SetRow(_sharingActivity, 2);
            surface.Children.Add(_sharingActivity);

            var opened = false;
            _sharingDialog = new ContentDialog
            {
                Title = "Account sharing",
                CloseButtonText = "Done",
                Content = surface
            };
            _sharingDialog.Resources["ContentDialogMaxWidth"] = 580.0;
            _sharingDialog.Opened += (_, _) =>
            {
                opened = true;
                UpdateSharingLayout();
                ShareAccountSwitch.Focus(FocusState.Programmatic);
            };
            _sharingDialog.Closing += (_, closing) =>
            {
                if (_lifetime.IsCancellationRequested || _pendingSharingCalendarDialog is not null) return;
                if (_busy)
                {
                    closing.Cancel = true;
                    SetNotice("Operation in progress", "Wait for it to finish or choose Cancel.", InfoBarSeverity.Informational);
                }
                else if (_sharingDirty)
                {
                    closing.Cancel = true;
                    SetNotice("Unsaved choices", "Save or discard your changes before closing.", InfoBarSeverity.Warning);
                    SaveSharingButton.Focus(FocusState.Programmatic);
                }
            };

            try
            {
                UpdateSharingLayout();
                await ShowDialogAsync(_sharingDialog);
            }
            finally
            {
                UntrackSettingsScrollViewer(_sharingScroll);
                _sharingScroll.Content = null;
                surface.Children.Clear();
                _sharingActivity.Children.Clear();
                SharingEditorParkingHost.Child = SharingEditorCard;
                _sharingDialog = null;
                _sharingScroll = null;
                _sharingActivity = null;
            }

            if (!opened && _sharingNotice.IsOpen && !_lifetime.IsCancellationRequested)
                SetNotice(_sharingNotice.Title, _sharingNotice.Message, _sharingNotice.Severity);
            if (_pendingSharingCalendarDialog is not { } picker || _lifetime.IsCancellationRequested) break;
            _pendingSharingCalendarDialog = null;
            // The editor has fully closed before the picker opens; its controls keep the draft.
            if (await ShowDialogAsync(picker) == ContentDialogResult.Primary)
            {
                _selectedCalendarIds = picker.SelectedCalendarIds.ToHashSet(StringComparer.Ordinal);
                _loadingSharing = true;
                CalendarScope.SelectedIndex = 1;
                _loadingSharing = false;
                UpdateSharingControls();
                UpdateSharingDirty();
            }
        }
        while (!_lifetime.IsCancellationRequested && _step == 2);

        _pendingSharingCalendarDialog = null;
        if (!_lifetime.IsCancellationRequested && _step == 2)
            SharingAccounts.Children.OfType<Button>()
                .FirstOrDefault(button => button.Tag as string == _selectedAccount?.Id)?.Focus(FocusState.Programmatic);
    }

    private void UpdateSharingActivity(string message)
    {
        if (_sharingScroll is null || _sharingActivity is null) return;
        _sharingScroll.IsEnabled = !_busy;
        _sharingActivityText.Text = message;
        _sharingActivity.Visibility = ToVisibility(_busy);
        if (_busy) _sharingNotice.IsOpen = false;
    }

    private void UpdateSharingLayout()
    {
        if (_sharingScroll is null) return;
        _sharingScroll.Width = Math.Min(500, Math.Max(0, Root.ActualWidth - 96));
        _sharingScroll.MaxHeight = Math.Max(120, Root.ActualHeight - 280);
        SharingSaveActions.Orientation = _sharingScroll.Width < 340 ? Orientation.Vertical : Orientation.Horizontal;
    }

    private void SelectSharingAccount(Account? account)
    {
        _loadingSharing = true;
        try
        {
            _selectedAccount = account;
            _sharingDirty = false;
            SharingEditor.Visibility = ToVisibility(account is not null);
            SharingEditorCard.Visibility = ToVisibility(account is not null);
            SaveSharingButton.IsEnabled = false;
            DiscardSharingButton.Visibility = Visibility.Collapsed;
            SharingSaveActions.Visibility = Visibility.Collapsed;
            SharingSavedText.Visibility = Visibility.Collapsed;
            if (account is null) return;
            var saved = _sharing.GetValueOrDefault(account.Id) ?? new AccountSharingSettings(account.Id);
            SelectedAccountText.Text = account.EmailAddress;
            SelectedProviderLogo.Source = ProviderLogo(account.Provider);
            ShareAccountSwitch.IsOn = saved.Enabled;
            ShareMailSwitch.IsOn = saved.ShareMail && account.MailReadEnabled;
            ShareCalendarsSwitch.IsOn = saved.ShareCalendars && account.CalendarReadEnabled;
            _selectedCalendarIds = saved.CalendarIds?.ToHashSet(StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
            CalendarScope.SelectedIndex = saved.CalendarIds is null ? 0 : 1;
            SharingSavedText.Text = IsDemo ? "Choices saved for this preview session." : "Choices saved on this device.";
        }
        finally
        {
            _loadingSharing = false;
            UpdateSharingControls();
            UpdateSharingLayout();
            RenderSharingAccounts();
        }
    }

    private void UpdateSharingControls()
    {
        var account = _selectedAccount;
        if (account is null) return;
        var enabled = ShareAccountSwitch.IsOn;
        SharingOffText.Visibility = ToVisibility(!enabled);
        SharingCategories.Visibility = ToVisibility(enabled);
        ShareMailSwitch.IsEnabled = enabled && account.MailReadEnabled;
        ShareCalendarsSwitch.IsEnabled = enabled && account.CalendarReadEnabled;
        CalendarChoices.Visibility = ToVisibility(enabled && ShareCalendarsSwitch.IsOn && account.CalendarReadEnabled);
        MissingConsentText.Visibility = ToVisibility(!account.MailReadEnabled || !account.CalendarReadEnabled);
        MissingConsentText.Text = !account.MailReadEnabled && !account.CalendarReadEnabled
            ? "Reconnect this account with mail or calendar access to enable these choices."
            : !account.MailReadEnabled ? "Mail access was not granted. Reconnect to add it."
            : "Calendar access was not granted. Reconnect to add it.";
        CalendarSelectionText.Text = CalendarScope.SelectedIndex == 0
            ? "Includes calendars added later."
            : $"{_selectedCalendarIds.Count} {(_selectedCalendarIds.Count == 1 ? "calendar" : "calendars")} selected.";
        CalendarSelectionText.Visibility = ToVisibility(CalendarScope.SelectedIndex != 0);
        ChooseCalendarsButton.Visibility = ToVisibility(CalendarScope.SelectedIndex != 0);
    }

    private void UpdateSharingDirty()
    {
        if (_loadingSharing || _selectedAccount is not { } account) return;
        var saved = _sharing.GetValueOrDefault(account.Id) ?? new AccountSharingSettings(account.Id);
        _sharingDirty = ShareAccountSwitch.IsOn != saved.Enabled
            || ShareMailSwitch.IsOn != (saved.ShareMail && account.MailReadEnabled)
            || ShareCalendarsSwitch.IsOn != (saved.ShareCalendars && account.CalendarReadEnabled)
            || (CalendarScope.SelectedIndex == 0) != (saved.CalendarIds is null)
            || (CalendarScope.SelectedIndex != 0 && !_selectedCalendarIds.SetEquals(saved.CalendarIds ?? []));
        if (_sharingDirty) _sharingReviewed = false;
        SaveSharingButton.IsEnabled = _sharingDirty;
        DiscardSharingButton.Visibility = ToVisibility(_sharingDirty);
        SharingSaveActions.Visibility = ToVisibility(_sharingDirty);
        SharingSavedText.Visibility = ToVisibility(_sharingDirty);
        SharingSavedText.Text = _sharingDirty ? "Unsaved changes" : IsDemo ? "Choices saved for this preview session." : "Choices saved on this device.";
        UpdateProgress();
    }

    private void SharingSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSharing || !_loaded) return;
        UpdateSharingControls();
        UpdateSharingDirty();
    }

    private void CalendarScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSharing || !_loaded) return;
        UpdateSharingControls();
        UpdateSharingDirty();
    }

    private async void ChooseCalendarsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAccount is not { } account || _busy || _sharingDialog is null || _pendingSharingCalendarDialog is not null) return;
        IReadOnlyList<ProviderCalendar>? available = null;
        await RunAsync("Loading calendar names…", async token =>
            available = await _application.ListAvailableCalendarsAsync(account.Id, token));
        if (available is null || _lifetime.IsCancellationRequested || _selectedAccount?.Id != account.Id || _step != 2 || _sharingDialog is null) return;
        // Discovery alone never changes consent or widens the saved selection.
        _pendingSharingCalendarDialog = new CalendarSelectionDialog(available, _selectedCalendarIds, CalendarScope.SelectedIndex == 0);
        _sharingDialog.Hide();
    }

    private async void SaveSharingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAccount is not { } account || !_sharingDirty || _busy) return;
        var requested = new AccountSharingSettings(account.Id,
            Enabled: ShareAccountSwitch.IsOn,
            ShareMail: ShareMailSwitch.IsOn && account.MailReadEnabled,
            ShareCalendars: ShareCalendarsSwitch.IsOn && account.CalendarReadEnabled,
            CalendarIds: CalendarScope.SelectedIndex == 0 ? null : _selectedCalendarIds.ToArray());
        var saved = false;
        await RunAsync("Saving sharing choices…", async token =>
        {
            var result = await _application.SaveAccountSharingAsync(requested, token);
            _sharing[account.Id] = result;
            SelectSharingAccount(account);
            RenderConnectedAccounts();
            UpdateSharingSummary();
            UpdateProgress();
            saved = true;
        });
        if (saved && !_lifetime.IsCancellationRequested) _sharingDialog?.Hide();
    }

    private void DiscardSharingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SelectSharingAccount(_selectedAccount);
        _sharingNotice.IsOpen = false;
        UpdateProgress();
        _sharingDialog?.Hide();
    }

}
