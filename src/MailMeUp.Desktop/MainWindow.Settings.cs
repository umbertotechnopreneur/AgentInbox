using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private ContentDialog? _settingsDialog;
    private Pivot? _settingsTabs;
    private ScrollViewer? _settingsScroll;
    private readonly List<ScrollViewer> _settingsScrolls = [];
    private readonly InfoBar _settingsNotice = new() { IsClosable = true };
    private readonly TextBlock _settingsActivityText = new() { TextWrapping = TextWrapping.Wrap };
    private Grid? _settingsActivity;
    private int _settingsTabIndex;

    private async void SharingSettingsButton_Click(object sender, RoutedEventArgs e) =>
        await ShowSharingSettingsAsync();

    private async Task ShowSharingSettingsAsync()
    {
        if (_busy || _dialogOpen || _lifetime.IsCancellationRequested) return;

        FrameworkElement[] sections = [SearchSettingsSection, UsageSettingsSection, LimitsSettingsSection];
        var surface = new Grid { RowSpacing = 12 };
        var opened = false;
        var failed = false;
        try
        {
            surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _settingsTabs = new Pivot { Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
            SettingsContent.Children.Clear();
            string[] titles = ["Search", "Usage", "Limits"];
            for (var index = 0; index < sections.Length; index++)
            {
                var scroll = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(0, 12, 12, 8)
                };
                _settingsScrolls.Add(scroll);
                scroll.SizeChanged += SettingsViewport_SizeChanged;
                scroll.Content = sections[index];
                _settingsTabs.Items.Add(new PivotItem
                {
                    Header = titles[index], Margin = new Thickness(0), Content = scroll
                });
            }
            _settingsTabs.SelectionChanged += SettingsTabs_SelectionChanged;
            _settingsTabs.SelectedIndex = _mailSearchPreferencesDirty ? 0 : _readGuardrailsDirty ? 2 : _settingsTabIndex;
            UpdateSelectedSettingsScroll();
            surface.Children.Add(_settingsTabs);

            _settingsActivity = new Grid { ColumnSpacing = 12, Visibility = Visibility.Collapsed };
            _settingsActivity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _settingsActivity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _settingsActivity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _settingsActivity.Children.Add(new ProgressRing { Width = 20, Height = 20, IsActive = true });
            Grid.SetColumn(_settingsActivityText, 1);
            _settingsActivity.Children.Add(_settingsActivityText);
            var cancel = new Button { Content = "Cancel" };
            cancel.Click += CancelButton_Click;
            Grid.SetColumn(cancel, 2);
            _settingsActivity.Children.Add(cancel);
            Grid.SetRow(_settingsNotice, 1);
            surface.Children.Add(_settingsNotice);
            Grid.SetRow(_settingsActivity, 2);
            surface.Children.Add(_settingsActivity);
            _settingsNotice.IsOpen = false;

            _settingsDialog = new ContentDialog
            {
                Title = "Search & read settings",
                CloseButtonText = "Done",
                Content = surface
            };
            _settingsDialog.Resources["ContentDialogMaxWidth"] = 704.0;
            _settingsDialog.Opened += (_, _) =>
            {
                opened = true;
                UpdateSettingsLayout();
                FocusUnsavedSettings();
            };
            _settingsDialog.Closing += (_, closing) =>
            {
                if (_lifetime.IsCancellationRequested) return;
                if (_busy)
                {
                    closing.Cancel = true;
                    SetNotice("Operation in progress", "Wait for it to finish or choose Cancel.", InfoBarSeverity.Informational);
                }
                else if (_mailSearchPreferencesDirty || _readGuardrailsDirty)
                {
                    closing.Cancel = true;
                    SetNotice("Unsaved settings", "Save or discard your changes before closing.", InfoBarSeverity.Warning);
                    FocusUnsavedSettings();
                }
            };

            UpdateSettingsLayout();
            await ShowDialogAsync(_settingsDialog);
            failed = !opened;
        }
        catch (Exception exception)
        {
            failed = true;
            _logger.LogWarning("Search and read settings could not open ({ErrorType})", exception.GetType().Name);
        }
        finally
        {
            if (_settingsScroll is { } activeScroll) UntrackSettingsScrollViewer(activeScroll);
            foreach (var scroll in _settingsScrolls)
            {
                scroll.SizeChanged -= SettingsViewport_SizeChanged;
                scroll.Content = null;
            }
            _settingsScrolls.Clear();
            if (_settingsTabs is { } tabs)
            {
                tabs.SelectionChanged -= SettingsTabs_SelectionChanged;
                tabs.Items.Clear();
            }
            surface.Children.Clear();
            _settingsActivity?.Children.Clear();
            // Put the original controls back even when dialog preparation fails partway.
            SettingsContent.Children.Clear();
            foreach (var section in sections) SettingsContent.Children.Add(section);
            _settingsDialog = null;
            _settingsTabs = null;
            _settingsScroll = null;
            _settingsActivity = null;
            UpdateSharingSettingsSummary();
        }

        // ShowDialogAsync reports failures through the active notice. Once the popup
        // is gone, repeat this on the wizard so the recovery action stays visible.
        if (failed && !_lifetime.IsCancellationRequested)
            SetNotice("Settings unavailable", "Try opening settings again. Your unsaved changes are kept.", InfoBarSeverity.Warning);
    }

    private void SettingsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _settingsTabs)) return;
        UpdateSelectedSettingsScroll();
        UpdateSettingsLayout();
    }

    private void UpdateSelectedSettingsScroll()
    {
        if (_settingsTabs?.SelectedItem is not PivotItem { Content: ScrollViewer scroll }) return;
        if (ReferenceEquals(_settingsScroll, scroll)) return;
        _settingsTabIndex = _settingsTabs.SelectedIndex;
        _settingsScroll = scroll;
        TrackSettingsScrollViewer(scroll);
        SetSettingsScrollPointerSurface((UIElement?)_settingsDialog ?? scroll);
        RefreshPageScrollChrome();
    }

    private void SettingsViewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSettingsLayout();

    private void FocusUnsavedSettings()
    {
        if (_settingsTabs is null) return;
        var tabIndex = _mailSearchPreferencesDirty ? 0 : _readGuardrailsDirty ? 2 : -1;
        if (tabIndex < 0) return;
        _settingsTabs.SelectedIndex = tabIndex;
        _settingsScroll?.ChangeView(null, 0, null, disableAnimation: true);
        var dialog = _settingsDialog;
        // Pivot realizes a newly selected page after selection; defer field focus.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!ReferenceEquals(dialog, _settingsDialog) || _settingsTabs?.SelectedIndex != tabIndex) return;
            if (tabIndex == 0) MailSearchLookbackDaysBox.Focus(FocusState.Programmatic);
            else ReadGuardrailAccountAttemptsBox.Focus(FocusState.Programmatic);
        });
    }

    private void UpdateSettingsLayout()
    {
        if (_settingsTabs is null || _settingsScroll is null) return;
        _settingsTabs.Width = Math.Min(624, Math.Max(0, Root.ActualWidth - 96));
        foreach (var scroll in _settingsScrolls)
            scroll.Height = Math.Min(420, Math.Max(120, Root.ActualHeight - 360));
        var activeSection = _settingsScroll.Content as FrameworkElement;
        var width = activeSection is { ActualWidth: > 0 } ? activeSection.ActualWidth : _settingsTabs.Width - 36;
        MailSearchPreferencesActions.Orientation = width < 360 ? Orientation.Vertical : Orientation.Horizontal;
        UpdateReadGuardrailLayout(width);
    }

    private void UpdateSettingsActivity(string message)
    {
        if (_settingsTabs is null || _settingsActivity is null) return;
        _settingsTabs.IsEnabled = !_busy;
        _settingsActivityText.Text = message;
        _settingsActivity.Visibility = ToVisibility(_busy);
        if (_busy) _settingsNotice.IsOpen = false;
    }
}
