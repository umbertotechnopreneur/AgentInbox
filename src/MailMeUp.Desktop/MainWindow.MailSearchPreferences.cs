using System.Globalization;
using MailMeUp.Core;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private MailSearchPreferences _mailSearchPreferences = new();
    private bool _mailSearchPreferencesLoaded;
    private bool _loadingMailSearchPreferences;
    private bool _mailSearchPreferencesDirty;

    private async Task LoadMailSearchPreferencesAsync(CancellationToken cancellationToken)
    {
        // Account refreshes must not overwrite an edited global preference.
        if (_mailSearchPreferencesDirty) return;
        _mailSearchPreferencesLoaded = false;
        MailSearchLookbackDaysBox.IsEnabled = false;
        SaveMailSearchPreferencesButton.IsEnabled = false;
        SaveMailSearchPreferencesButton.Visibility = Visibility.Collapsed;
        ReloadMailSearchPreferencesButton.Visibility = Visibility.Collapsed;
        MailSearchPreferencesSavedText.Visibility = Visibility.Visible;
        MailSearchPreferencesSavedText.Text = "Loading the saved search period…";
        try
        {
            var preferences = await _application.GetMailSearchPreferencesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            DisplayMailSearchPreferences(preferences);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MailSearchPreferencesHeading.Text = "Search period not loaded";
            MailSearchPreferencesSavedText.Text = "Loading stopped. Retry to read the saved search period.";
            ReloadMailSearchPreferencesButton.Visibility = Visibility.Visible;
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Default mail search period could not load ({ErrorType})", exception.GetType().Name);
            MailSearchPreferencesHeading.Text = "Search period unavailable";
            MailSearchPreferencesSavedText.Text = "Could not load the saved search period. Check local storage access and retry.";
            ReloadMailSearchPreferencesButton.Visibility = Visibility.Visible;
        }
    }

    private void DisplayMailSearchPreferences(MailSearchPreferences preferences)
    {
        _loadingMailSearchPreferences = true;
        try
        {
            _mailSearchPreferences = preferences;
            MailSearchLookbackDaysBox.Value = preferences.DefaultLookbackDays;
            MailSearchLookbackDaysBox.Text = preferences.DefaultLookbackDays.ToString(CultureInfo.CurrentCulture);
            _mailSearchPreferencesLoaded = true;
            MailSearchLookbackDaysBox.IsEnabled = true;
            ReloadMailSearchPreferencesButton.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _loadingMailSearchPreferences = false;
        }
        UpdateMailSearchPreferencesDirty();
    }

    private bool TryGetMailSearchLookbackDays(out int days) =>
        int.TryParse(MailSearchLookbackDaysBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out days)
        && days >= MailSearchPreferences.MinimumDays && days <= MailSearchPreferences.MaximumDays;

    private void UpdateMailSearchPreferencesDirty()
    {
        if (_loadingMailSearchPreferences || !_mailSearchPreferencesLoaded) return;
        var valid = TryGetMailSearchLookbackDays(out var days);
        _mailSearchPreferencesDirty = !valid || days != _mailSearchPreferences.DefaultLookbackDays;
        if (_mailSearchPreferencesDirty) _sharingReviewed = false;
        SaveMailSearchPreferencesButton.IsEnabled = valid && _mailSearchPreferencesDirty;
        SaveMailSearchPreferencesButton.Visibility = ToVisibility(_mailSearchPreferencesDirty);
        DiscardMailSearchPreferencesButton.Visibility = ToVisibility(_mailSearchPreferencesDirty);
        MailSearchPreferencesSavedText.Text = !valid
            ? $"Enter a whole number from {MailSearchPreferences.MinimumDays} to {MailSearchPreferences.MaximumDays} days."
            : _mailSearchPreferencesDirty ? "Unsaved search period"
            : IsDemo ? "Saved for this preview session." : "Saved on this device.";
        MailSearchPreferencesSavedText.Visibility = ToVisibility(_mailSearchPreferencesDirty || IsDemo);
        MailSearchPreferencesHeading.Text = "Default search period";
        UpdateSharingSettingsSummary();
        UpdateProgress();
    }

    private async void SaveMailSearchPreferencesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_mailSearchPreferencesLoaded || !_mailSearchPreferencesDirty) return;
        if (!TryGetMailSearchLookbackDays(out var days))
        {
            UpdateMailSearchPreferencesDirty();
            MailSearchLookbackDaysBox.Focus(FocusState.Programmatic);
            return;
        }
        var requested = new MailSearchPreferences(days);
        await RunAsync("Saving the default mail search period…", async token =>
        {
            try
            {
                var result = await _application.SaveMailSearchPreferencesAsync(requested, token);
                DisplayMailSearchPreferences(result);
                SetNotice("Search period saved", IsDemo ? "Saved for this preview session. Closing the demo resets this period." : "Applies to new mail searches without explicit dates on every account.", InfoBarSeverity.Success);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning("Default mail search period could not save ({ErrorType})", exception.GetType().Name);
                SetNotice("Could not save the search period", "Check local storage access and retry. Your changes are still available to save or discard.", InfoBarSeverity.Error);
            }
        });
    }

    private void DiscardMailSearchPreferencesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_mailSearchPreferencesLoaded) return;
        DisplayMailSearchPreferences(_mailSearchPreferences);
        _settingsNotice.IsOpen = false;
    }

    private async void ReloadMailSearchPreferencesButton_Click(object sender, RoutedEventArgs e) =>
        await RunAsync("Loading the default mail search period…", LoadMailSearchPreferencesAsync);
}
