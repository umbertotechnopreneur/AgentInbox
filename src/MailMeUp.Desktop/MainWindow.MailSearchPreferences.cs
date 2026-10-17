// SPDX-License-Identifier: MIT

/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.Globalization;
using MailMeUp.Core;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MailMeUp.Desktop;

/// <summary>Implements local mail-search preference loading, editing, and saving for the window.</summary>
public sealed partial class MainWindow
{
    private MailSearchPreferences _mailSearchPreferences = new();
    private bool _mailSearchPreferencesLoaded;
    private bool _loadingMailSearchPreferences;
    private bool _mailSearchPreferencesDirty;

    /// <summary>Loads persisted mail-search defaults and reflects them in the settings controls.</summary>
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

    /// <summary>Displays a validated mail-search preference value without marking it as an unsaved edit.</summary>
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

    /// <summary>Validates the editable lookback-day control and returns its integral value.</summary>
    private bool TryGetMailSearchLookbackDays(out int days) =>
        int.TryParse(MailSearchLookbackDaysBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out days)
        && days >= MailSearchPreferences.MinimumDays && days <= MailSearchPreferences.MaximumDays;

    /// <summary>Updates save-state controls after a mail-search preference edit.</summary>
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
        MailSearchPreferencesHeading.Text = "Mail · default search period";
        UpdateSharingSettingsSummary();
        UpdateProgress();
    }

    /// <summary>Saves a validated mail-search preference draft after explicit user confirmation.</summary>
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

    /// <summary>Restores the last loaded mail-search preference value after an explicit discard request.</summary>
    private void DiscardMailSearchPreferencesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_mailSearchPreferencesLoaded) return;
        DisplayMailSearchPreferences(_mailSearchPreferences);
        _settingsNotice.IsOpen = false;
    }

    /// <summary>Reloads persisted mail-search preferences after an explicit user request.</summary>
    private async void ReloadMailSearchPreferencesButton_Click(object sender, RoutedEventArgs e) =>
        await RunAsync("Loading the default mail search period…", LoadMailSearchPreferencesAsync);
}
