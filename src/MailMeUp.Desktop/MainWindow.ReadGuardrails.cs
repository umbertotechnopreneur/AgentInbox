using System.Globalization;
using MailMeUp.Core;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private ReadGuardrailStatus? _readGuardrailStatus;
    private ReadGuardrailLimits? _readGuardrailExpectedLimits;
    private bool _loadingReadGuardrails;
    private bool _readGuardrailsDirty;
    private bool _readGuardrailsAvailable;
    private bool _readGuardrailsConflict;

    private Control[] ReadGuardrailBoxes =>
    [
        ReadGuardrailAccountAttemptsBox, ReadGuardrailProfileAttemptsBox,
        ReadGuardrailContentReadsBox, ReadGuardrailDetailReadsBox, ReadGuardrailWindowBox,
        ReadGuardrailResponseBytesBox, ReadGuardrailOutputBytesBox
    ];

    private void InitializeReadGuardrails()
    {
        SaveReadGuardrailsButton.Visibility = Visibility.Collapsed;
        foreach (var box in ReadGuardrailBoxes.OfType<NumberBox>())
            box.RegisterPropertyChangedCallback(NumberBox.TextProperty, (_, _) => UpdateReadGuardrailsDirty());
        ReadGuardrailResponseBytesBox.TextChanged += (_, _) => UpdateReadGuardrailsDirty();
        ReadGuardrailOutputBytesBox.TextChanged += (_, _) => UpdateReadGuardrailsDirty();
    }

    private async Task LoadReadGuardrailsAsync(CancellationToken cancellationToken)
    {
        ReadGuardrailLoadText.Text = "Loading local limits and usage…";
        try
        {
            var status = await _application.GetReadGuardrailStatusAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _readGuardrailsAvailable = status is not null;
            if (status is null)
            {
                ReadGuardrailLoadText.Text = "Read limits are unavailable in this session. Refresh to retry.";
                ReadGuardrailEditorExpander.IsEnabled = _readGuardrailsDirty;
                UpdateReadGuardrailsDirty();
                return;
            }
            _readGuardrailStatus = status;
            _readGuardrailsConflict = _readGuardrailsDirty && _readGuardrailExpectedLimits != status.SavedLimits;
            RenderReadGuardrailStatus(status);
            // Refreshing counters must never replace a draft or its optimistic-save baseline.
            if (!_readGuardrailsDirty) DisplayReadGuardrailDraft(status.SavedLimits);
            else UpdateReadGuardrailsDirty();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ReadGuardrailLoadText.Text = "Refresh stopped. Any displayed usage is the previous snapshot; your draft is kept.";
            UpdateReadGuardrailsDirty();
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Local read guardrails could not load ({ErrorType})", exception.GetType().Name);
            ReadGuardrailLoadText.Text = "Could not refresh local limits. Any displayed usage is the previous snapshot. Check local storage and retry; your draft is kept.";
            UpdateReadGuardrailsDirty();
        }
    }

    private void RenderReadGuardrailStatus(ReadGuardrailStatus status)
    {
        var limits = status.ActiveLimits;
        var usage = status.Usage;
        var window = limits.ReadWindowSeconds % 60 == 0
            ? $"{limits.ReadWindowSeconds / 60} min" : $"{limits.ReadWindowSeconds} sec";
        ReadGuardrailUsageRows.Children.Clear();
        AddReadGuardrailUsage("Provider attempts · last minute", usage.ProviderAttemptsInMinute,
            limits.ProviderAttemptsPerProfilePerMinute, usage.NextProviderCapacityAt);
        AddReadGuardrailUsage($"Content reads · last {window}", usage.ContentReadsInWindow,
            limits.ContentReadsPerWindow, usage.NextContentCapacityAt);
        AddReadGuardrailUsage($"Detail reads · last {window}", usage.DetailReadsInWindow,
            limits.DetailReadsPerWindow, usage.NextDetailCapacityAt);
        AddReadGuardrailUsage($"Assistant output · last {window}", usage.OutputBytesInWindow,
            limits.OutputBytesPerWindow, usage.NextOutputCapacityAt, outputBytes: true);
        ReadGuardrailActiveText.Text = $"This window allows {limits.ProviderAttemptsPerAccountServicePerMinute:N0} attempts per account/service each minute and {limits.ResponseBytes / 1024m:0.##########} KiB per response. Output measures bytes, not model tokens.";
        ReadGuardrailCooldownText.Text = usage.ActiveCooldowns == 0
            ? "No provider pauses in this snapshot."
            : $"{usage.ActiveCooldowns:N0} active provider {(usage.ActiveCooldowns == 1 ? "pause" : "pauses")}."
              + (usage.LatestCooldownEndsAt is { } ends ? $" Latest recorded end: {ReadGuardrailTime(ends)}." : string.Empty);
        ReadGuardrailUpdatedText.Text = $"Snapshot: {ReadGuardrailTime(usage.CapturedAt)} (local time). Refresh to update. Capacity returns gradually; the recorded times are not a full reset.";
        ReadGuardrailLoadText.Text = IsDemo
            ? "Preview only · sample usage and settings stay in this demo session."
            : "Usage across this local profile, against this window's limits. Other processes may use earlier limits.";
        ReadGuardrailDetailsExpander.Header = usage.ActiveCooldowns == 0
            ? "Usage details"
            : $"Usage details · {usage.ActiveCooldowns:N0} provider {(usage.ActiveCooldowns == 1 ? "pause" : "pauses")}";
        ReadGuardrailUsagePanel.Visibility = Visibility.Visible;
        ReadGuardrailEditorExpander.IsEnabled = true;
        ReadGuardrailRestartNotice.IsOpen = status.RequiresRestart;
        ReadGuardrailRestartNotice.Message = IsDemo
            ? "Preview only: saved limits differ from the simulated active limits. The real app requires restarting MailMeUp and reconnecting your assistant's MailMeUp connection. Closing the demo resets its sample choices."
            : "Saved limits differ from those active in this window. Restart MailMeUp and reconnect your assistant's MailMeUp connection to apply them to all processes. Saving does not clear usage or provider pauses.";
        UpdateSharingSettingsSummary();
    }

    private void AddReadGuardrailUsage(string label, long used, long limit, DateTimeOffset? nextCapacity, bool outputBytes = false)
    {
        var counts = outputBytes
            ? $"{used / 1024m:0.##} / {limit / 1024m:0.##} KiB"
            : $"{used:N0} / {limit:N0}";
        var row = new StackPanel { Spacing = 6 };
        row.Children.Add(new TextBlock
        {
            Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush")
        });
        row.Children.Add(new TextBlock
        {
            Text = counts, TextWrapping = TextWrapping.Wrap, FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        var progress = new ProgressBar { Minimum = 0, Maximum = limit, Value = Math.Min(used, limit), IsIndeterminate = false };
        AutomationProperties.SetName(progress, $"{label}, {counts}");
        row.Children.Add(progress);
        if (nextCapacity is { } next)
            row.Children.Add(new TextBlock
            {
                Text = $"Next recorded capacity release: {ReadGuardrailTime(next)}.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = ThemeBrush("TextFillColorSecondaryBrush")
            });
        var tile = row;
        var index = ReadGuardrailUsageRows.Children.Count;
        var narrow = ReadGuardrailUsageRightColumn.Width.Value == 0;
        Grid.SetColumn(tile, narrow ? 0 : index % 2);
        Grid.SetRow(tile, narrow ? index : index / 2);
        ReadGuardrailUsageRows.Children.Add(tile);
    }

    private static string ReadGuardrailTime(DateTimeOffset time) => time.ToLocalTime().ToString("G", CultureInfo.CurrentCulture);

    private void UpdateSharingSettingsSummary()
    {
        SharingSettingsSummary.Text = _mailSearchPreferencesDirty || _readGuardrailsDirty
            ? "Unsaved search or read settings"
            : _readGuardrailStatus?.RequiresRestart == true
                ? "Saved read limits · restart needed"
                : "Search period, read limits and usage";
    }

    private void DisplayReadGuardrailDraft(ReadGuardrailLimits limits)
    {
        _loadingReadGuardrails = true;
        try
        {
            _readGuardrailExpectedLimits = limits;
            _readGuardrailsConflict = false;
            SetReadGuardrailBox(ReadGuardrailAccountAttemptsBox, limits.ProviderAttemptsPerAccountServicePerMinute);
            SetReadGuardrailBox(ReadGuardrailProfileAttemptsBox, limits.ProviderAttemptsPerProfilePerMinute);
            SetReadGuardrailBox(ReadGuardrailContentReadsBox, limits.ContentReadsPerWindow);
            SetReadGuardrailBox(ReadGuardrailDetailReadsBox, limits.DetailReadsPerWindow);
            SetReadGuardrailBox(ReadGuardrailWindowBox, limits.ReadWindowSeconds);
            ReadGuardrailResponseBytesBox.Text = (limits.ResponseBytes / 1024m).ToString(CultureInfo.CurrentCulture);
            ReadGuardrailOutputBytesBox.Text = (limits.OutputBytesPerWindow / 1024m).ToString(CultureInfo.CurrentCulture);
        }
        finally { _loadingReadGuardrails = false; }
        UpdateReadGuardrailsDirty();
    }

    private static void SetReadGuardrailBox(NumberBox box, decimal value)
    {
        box.Value = (double)value;
        box.Text = value.ToString(CultureInfo.CurrentCulture);
    }

    private bool TryGetReadGuardrailDraft(out ReadGuardrailLimits limits, out string error)
    {
        limits = _readGuardrailExpectedLimits ?? new();
        error = "Enter whole numbers for attempts, reads and seconds. KiB values must describe an exact whole number of bytes.";
        if (!TryReadGuardrailInteger(ReadGuardrailAccountAttemptsBox, out var account)
            || !TryReadGuardrailInteger(ReadGuardrailProfileAttemptsBox, out var profile)
            || !TryReadGuardrailInteger(ReadGuardrailContentReadsBox, out var content)
            || !TryReadGuardrailInteger(ReadGuardrailDetailReadsBox, out var detail)
            || !TryReadGuardrailInteger(ReadGuardrailWindowBox, out var window)
            || !TryReadGuardrailBytes(ReadGuardrailResponseBytesBox, out var response)
            || !TryReadGuardrailBytes(ReadGuardrailOutputBytesBox, out var output)) return false;
        limits = limits with
        {
            ProviderAttemptsPerAccountServicePerMinute = account, ProviderAttemptsPerProfilePerMinute = profile,
            ContentReadsPerWindow = content, DetailReadsPerWindow = detail, ReadWindowSeconds = window,
            ResponseBytes = response, OutputBytesPerWindow = output
        };
        try { limits.Validate(); }
        catch (ArgumentOutOfRangeException)
        {
            error = "Use 1–10,000 attempts/reads, with details no greater than content reads; a 60–3,600 second window; 1–16,384 KiB output; and a 1–1,024 KiB response no larger than the output limit.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryReadGuardrailInteger(NumberBox box, out int value) =>
        int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);

    private static bool TryReadGuardrailBytes(TextBox box, out int bytes)
    {
        bytes = 0;
        if (!decimal.TryParse(box.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.CurrentCulture, out var kib) || kib < 1 || kib > 16_384) return false;
        var exactBytes = kib * 1024;
        if (decimal.Truncate(exactBytes) != exactBytes) return false;
        bytes = (int)exactBytes;
        return true;
    }

    private void UpdateReadGuardrailsDirty()
    {
        if (_loadingReadGuardrails) return;
        if (_readGuardrailExpectedLimits is null)
        {
            ReadGuardrailDraftText.Text = "Open Usage and choose Refresh to load read limits.";
            ReadGuardrailDraftText.Visibility = Visibility.Visible;
            return;
        }
        var valid = TryGetReadGuardrailDraft(out var draft, out var error);
        _readGuardrailsDirty = !valid || draft != _readGuardrailExpectedLimits;
        if (_readGuardrailsDirty) _sharingReviewed = false;
        SaveReadGuardrailsButton.IsEnabled = _readGuardrailsAvailable && valid && _readGuardrailsDirty && !_readGuardrailsConflict;
        SaveReadGuardrailsButton.Visibility = ToVisibility(_readGuardrailsDirty);
        DiscardReadGuardrailsButton.Visibility = ToVisibility(_readGuardrailsDirty || _readGuardrailsConflict);
        ReadGuardrailDraftText.Text = _readGuardrailsConflict
            ? "Limits changed in another process. Refresh usage, then discard your draft to load the latest saved limits before editing again."
            : !valid ? error : _readGuardrailsDirty ? "Unsaved limits · saving requires restarting MailMeUp processes."
            : IsDemo ? "Saved for this preview session." : "Saved on this device. Current usage appears in the Usage tab.";
        ReadGuardrailDraftText.Visibility = ToVisibility(_readGuardrailsDirty || _readGuardrailsConflict || IsDemo);
        ReadGuardrailEditorHeading.Text = "Read limits";
        UpdateSharingSettingsSummary();
        UpdateProgress();
    }

    private async void RefreshReadGuardrailsButton_Click(object sender, RoutedEventArgs e) =>
        await RunAsync("Refreshing local read usage…", LoadReadGuardrailsAsync);

    private async void SaveReadGuardrailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_readGuardrailsAvailable || !_readGuardrailsDirty || _readGuardrailsConflict
            || _readGuardrailExpectedLimits is not { } expected || !TryGetReadGuardrailDraft(out var draft, out _)) return;
        await RunAsync("Saving local read limits…", async token =>
        {
            try
            {
                var status = await _application.SaveReadGuardrailLimitsAsync(draft, expected, token);
                _readGuardrailStatus = status;
                RenderReadGuardrailStatus(status);
                DisplayReadGuardrailDraft(status.SavedLimits);
                SetNotice("Read limits saved", IsDemo ? "Saved for this preview session only. Closing the demo resets these choices."
                    : status.RequiresRestart ? "Restart MailMeUp and reconnect your assistant's MailMeUp connection to apply the saved limits to all processes. Existing usage and provider pauses are kept."
                    : "Saved limits match this window's active limits. Other running processes may need restarting. Existing usage and provider pauses are kept.", InfoBarSeverity.Success);
            }
            catch (InvalidOperationException)
            {
                _readGuardrailsConflict = true;
                UpdateReadGuardrailsDirty();
                SetNotice("Limits changed elsewhere", "Your draft is kept. Refresh usage and discard your draft to load the latest saved limits before editing again.", InfoBarSeverity.Warning);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning("Local read limits could not save ({ErrorType})", exception.GetType().Name);
                SetNotice("Could not save read limits", "Check local storage access and retry. Your draft is still available to save or discard.", InfoBarSeverity.Error);
            }
        });
    }

    private async void DiscardReadGuardrailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _readGuardrailExpectedLimits is null) return;
        // Explicit discard reloads the saved baseline; failure still preserves the draft.
        await RunAsync("Loading saved read limits…", async token =>
        {
            var status = await _application.GetReadGuardrailStatusAsync(token);
            token.ThrowIfCancellationRequested();
            if (status is null)
            {
                SetNotice("Read limits unavailable", "Your draft is kept. Refresh and retry when local limits are available.", InfoBarSeverity.Warning);
                return;
            }
            _readGuardrailsAvailable = true;
            _readGuardrailStatus = status;
            RenderReadGuardrailStatus(status);
            DisplayReadGuardrailDraft(status.SavedLimits);
        });
    }

    private void DefaultReadGuardrailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _readGuardrailExpectedLimits is not { } expected) return;
        // Defaults populate the visible draft only; unexposed spacing/wait settings remain unchanged.
        DisplayReadGuardrailDraft(new ReadGuardrailLimits());
        _readGuardrailExpectedLimits = expected;
        _readGuardrailsConflict = _readGuardrailStatus?.SavedLimits != expected;
        UpdateReadGuardrailsDirty();
    }

    private void UpdateReadGuardrailLayout(double availableWidth)
    {
        var usageWidth = _settingsTabs?.SelectedIndex == 1 && ReadGuardrailUsageRows.ActualWidth > 0
            ? ReadGuardrailUsageRows.ActualWidth : availableWidth;
        var narrowUsage = usageWidth < 420;
        ReadGuardrailUsageRightColumn.Width = narrowUsage ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        for (var index = 0; index < ReadGuardrailUsageRows.Children.Count; index++)
        {
            var tile = (FrameworkElement)ReadGuardrailUsageRows.Children[index];
            Grid.SetColumn(tile, narrowUsage ? 0 : index % 2);
            Grid.SetRow(tile, narrowUsage ? index : index / 2);
        }
        var editorWidth = _settingsTabs?.SelectedIndex == 2 && ReadGuardrailEditorExpander.ActualWidth > 0
            ? ReadGuardrailEditorExpander.ActualWidth : availableWidth;
        var narrow = editorWidth < 520;
        ReadGuardrailFieldRightColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        var boxes = ReadGuardrailBoxes;
        for (var index = 0; index < boxes.Length; index++)
        {
            Grid.SetColumn(boxes[index], narrow ? 0 : index % 2);
            Grid.SetRow(boxes[index], narrow ? index : index / 2);
        }
        ReadGuardrailSaveActions.Orientation = editorWidth < 420 ? Orientation.Vertical : Orientation.Horizontal;
    }
}
