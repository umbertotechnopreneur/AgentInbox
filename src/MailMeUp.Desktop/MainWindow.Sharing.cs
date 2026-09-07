using MailMeUp.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private Account? _selectedAccount;
    private HashSet<string> _selectedCalendarIds = new(StringComparer.Ordinal);
    private bool _loadingSharing;

    private void RenderSharingAccounts()
    {
        SharingAccounts.Children.Clear();
        foreach (var account in _accounts)
        {
            var selected = account.Id == _selectedAccount?.Id;
            var row = AccountRow(account, includeStatus: true);
            var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 12 };
            Grid.SetColumn(chevron, 2);
            row.Children.Add(chevron);
            var button = new Button
            {
                Content = row,
                Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["QuietButton"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 8, 10, 8),
                BorderThickness = new Thickness(1),
                BorderBrush = selected ? ThemeBrush("WizardAccentBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Background = selected ? ThemeBrush("WizardSelectionBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            };
            AutomationProperties.SetName(button, $"{account.EmailAddress}, {ProviderName(account.Provider)}, {(IsShared(account) ? "shared" : "sharing off")}");
            AutomationProperties.SetItemStatus(button, selected ? "Selected account" : string.Empty);
            button.Click += (_, _) =>
            {
                if (_busy || (account.Id != _selectedAccount?.Id && !CanLeaveSharing())) return;
                if (account.Id != _selectedAccount?.Id) SelectSharingAccount(account);
                _showSharingList = false;
                UpdateSharingLayout();
                ShareAccountSwitch.Focus(FocusState.Programmatic);
            };
            SharingAccounts.Children.Add(button);
        }
        if (_accounts.Count == 0)
            SharingAccounts.Children.Add(Body("Connect an account to get started."));
    }

    private void UpdateSharingLayout()
    {
        SharingListColumn.Width = new GridLength(_narrowSharing ? 1 : 0.85, GridUnitType.Star);
        SharingDetailColumn.Width = _narrowSharing ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(SharingDetailRegion, _narrowSharing ? 0 : 1);
        var showList = !_narrowSharing || _showSharingList || _selectedAccount is null;
        SharingListRegion.Visibility = ToVisibility(showList);
        SharingDetailRegion.Visibility = ToVisibility(!_narrowSharing || !showList);
        ChooseAccountButton.Visibility = ToVisibility(_narrowSharing && !showList);
    }

    private void SelectSharingAccount(Account? account)
    {
        _loadingSharing = true;
        try
        {
            _selectedAccount = account;
            _sharingDirty = false;
            SharingEmptyText.Visibility = ToVisibility(account is null);
            SharingEditor.Visibility = ToVisibility(account is not null);
            SaveSharingButton.IsEnabled = false;
            DiscardSharingButton.Visibility = Visibility.Collapsed;
            if (account is null) return;
            var saved = _sharing.GetValueOrDefault(account.Id) ?? new AccountSharingSettings(account.Id);
            SelectedAccountText.Text = account.EmailAddress;
            SelectedProviderLogo.Source = ProviderLogo(account.Provider);
            ShareAccountSwitch.IsOn = saved.Enabled;
            ShareMailSwitch.IsOn = saved.ShareMail && account.MailReadEnabled;
            ShareCalendarsSwitch.IsOn = saved.ShareCalendars && account.CalendarReadEnabled;
            _selectedCalendarIds = saved.CalendarIds?.ToHashSet(StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
            CalendarScope.SelectedIndex = saved.CalendarIds is null ? 0 : 1;
            SharingSavedText.Text = "Choices saved on this device.";
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
        SharingSavedText.Text = _sharingDirty ? "Unsaved changes" : "Choices saved on this device.";
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
        if (_selectedAccount is not { } account || _busy || _dialogOpen) return;
        IReadOnlyList<ProviderCalendar>? available = null;
        await RunAsync("Loading calendar names…", async token =>
            available = await _application.ListAvailableCalendarsAsync(account.Id, token));
        if (available is null || _lifetime.IsCancellationRequested || _selectedAccount?.Id != account.Id || _step != 2) return;
        var dialog = new CalendarSelectionDialog(available, _selectedCalendarIds, CalendarScope.SelectedIndex == 0);
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        // Discovery alone never changes consent or widens the saved selection.
        _selectedCalendarIds = dialog.SelectedCalendarIds.ToHashSet(StringComparer.Ordinal);
        _loadingSharing = true;
        CalendarScope.SelectedIndex = 1;
        _loadingSharing = false;
        UpdateSharingControls();
        UpdateSharingDirty();
    }

    private async void SaveSharingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAccount is not { } account || !_sharingDirty) return;
        var requested = new AccountSharingSettings(account.Id,
            Enabled: ShareAccountSwitch.IsOn,
            ShareMail: ShareMailSwitch.IsOn && account.MailReadEnabled,
            ShareCalendars: ShareCalendarsSwitch.IsOn && account.CalendarReadEnabled,
            CalendarIds: CalendarScope.SelectedIndex == 0 ? null : _selectedCalendarIds.ToArray());
        await RunAsync("Saving sharing choices…", async token =>
        {
            var result = await _application.SaveAccountSharingAsync(requested, token);
            _sharing[account.Id] = result;
            SelectSharingAccount(account);
            RenderConnectedAccounts();
            UpdateSharingSummary();
            UpdateProgress();
            SetNotice("Sharing choices saved", "Applies to future reads. Existing conversations keep information already returned.", InfoBarSeverity.Success);
        });
    }

    private void DiscardSharingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SelectSharingAccount(_selectedAccount);
        Notice.IsOpen = false;
        UpdateProgress();
    }

    private void ChooseAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanLeaveSharing()) return;
        _showSharingList = true;
        UpdateSharingLayout();
    }

}
