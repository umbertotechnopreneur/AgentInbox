using MailMeUp.Desktop.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private string _codexReport = string.Empty;

    private async void RefreshCodexButton_Click(object sender, RoutedEventArgs e) =>
        await RunCodexSetupAsync(install: false);

    private async void InstallPluginButton_Click(object sender, RoutedEventArgs e) =>
        await RunCodexSetupAsync(install: true);

    private async Task RunCodexSetupAsync(bool install)
    {
        if (BlockDemoAction()) return;
        await RunAsync(install ? "Installing the local Codex plugin…" : "Checking local Codex setup…", async token =>
        {
            // Clear stale success and install eligibility before any new operation, including cancellation.
            _codexStatus = null;
            _codexReport = string.Empty;
            CopyCodexResultsButton.Visibility = CodexHelpButton.Visibility = InstallPluginButton.Visibility = Visibility.Collapsed;
            InstallPluginButton.IsEnabled = false;
            CodexStatusTitle.Text = install ? "Installing local plugin…" : "Checking local setup…";
            CodexStatusText.Text = install ? "Adding MailMeUp to this device's Codex setup." : "Looking for MailMeUp in this device's Codex setup.";
            CodexStatusCaption.Text = "Local configuration only";
            CodexStatusIcon.Glyph = "\uE895";
            CodexDiagnosticText.Text = "Waiting for local configuration results. No prompt or mailbox check is running.";
            CodexCheckedText.Text = "Check in progress…";
            RenderCodexChecks(CodexSetupCheck.Pending());
            UpdateProgress();
            try
            {
                if (install)
                {
                    var result = await _codex.InstallPluginAsync(token);
                    ApplyCodexStatus(result.Status);
                }
                else
                {
                    ApplyCodexStatus(await _codex.GetStatusAsync(token));
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Desktop Codex setup interrupted; installationRequested={InstallationRequested}; previous results cleared", install);
                if (!_lifetime.IsCancellationRequested)
                    ApplyCodexStatus(new("CheckInterrupted", install
                        ? "Installation was interrupted. Some local changes may have completed; refresh status before retrying."
                        : "The check was cancelled or timed out. Refresh status to try again; previous results have been cleared.", false, false, false));
            }
            catch (Exception exception)
            {
                _logger.LogWarning("Desktop Codex setup failed ({ErrorType}); no successful result assumed", exception.GetType().Name);
                if (!_lifetime.IsCancellationRequested)
                    ApplyCodexStatus(new("ConfigurationUnknown", "The check could not finish. Refresh status to retry; diagnostic details are in the local log.", false, false, false));
            }
        }, TimeSpan.FromMinutes(install ? 5 : 2));
    }

    private void ApplyCodexStatus(CodexSetupStatus status)
    {
        _codexStatus = status;
        var view = CodexSetupPresentation.FromStatus(status);
        var configured = status.Code == "PluginConfigured";
        var retry = status.Code is "CheckInterrupted" or "ConfigurationUnknown" or "PluginStatusUnknown"
            or "MarketplaceStatusUnknown" or "InstallationIncomplete";
        CodexStatusTitle.Text = view.Title;
        CodexStatusText.Text = CodexStatusSummary(status);
        CodexStatusCaption.Text = configured ? "Local plugin enabled · Live connection not tested"
            : status.Code == "ReadyToInstall" ? "Local setup checked · Ready to install"
            : retry ? "Local setup needs another check"
            : "Local setup needs your attention";
        CodexStatusIcon.Glyph = configured ? "\uE73E" : status.Code == "ReadyToInstall" ? "\uE943" : "\uE946";
        CodexDiagnosticText.Text = status.Message;
        InstallPluginButton.IsEnabled = status.CanInstall;
        InstallPluginButton.Visibility = ToVisibility(status.CanInstall);
        InstallPluginButton.Content = status.IsPluginConfigured ? "Update local plugin" : "Install local plugin";
        InstallPluginButton.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[
            status.CanInstall && !configured ? "AccentButtonStyle" : "QuietButton"];
        CodexHelpButton.Content = view.HelpLabel;
        CodexHelpButton.Visibility = Visibility.Visible;
        CodexHelpButton.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[
            configured || (!status.CanInstall && !retry) ? "AccentButtonStyle" : "QuietButton"];
        RefreshCodexButton.Content = "Refresh status";
        RefreshCodexButton.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[
            retry ? "AccentButtonStyle" : "QuietButton"];
        var checks = status.Checks.Count > 0 ? status.Checks : CodexSetupCheck.Pending();
        RenderCodexChecks(checks);
        CodexCheckedText.Text = $"Last attempt: {DateTimeOffset.Now:HH:mm:ss} · Local configuration only";
        _codexReport = string.Join(Environment.NewLine,
            new[] { "MailMeUp — Codex setup", CodexCheckedText.Text, $"Result: {status.Code}", status.Message }
                .Concat(checks.Select(check => $"{check.Name}: {check.Result}"))
                .Append("Runtime connection, Codex sign-in and mailbox access: not tested."));
        CopyCodexResultsButton.Content = "Copy setup results";
        CopyCodexResultsButton.Visibility = Visibility.Visible;
        UpdateProgress();
    }

    private static string CodexStatusSummary(CodexSetupStatus status) => status.Code switch
    {
        "ReadyToInstall" => "Add MailMeUp to Codex, then start a new task.",
        "PluginConfigured" => "Start a new Codex task to load MailMeUp's tools.",
        "DirectRegistrationExists" => status.IsPluginConfigured
            ? "Choose one MailMeUp connection in Codex to avoid duplicate tools."
            : "You can keep your existing connection or review how to switch to the plugin.",
        "PluginDisabled" => "Enable MailMeUp in Codex's plugin settings, then refresh here.",
        "OtherPluginExists" => "Review the existing MailMeUp plugin before adding this local copy.",
        "AliasUnavailable" => "Enable mailmeup.exe in Windows app execution aliases to continue.",
        "CodexUnavailable" => "Automatic setup needs the Codex CLI. Open setup help for your options.",
        "MarketplaceConflict" => "Review the existing local marketplace before installing MailMeUp.",
        "CheckInterrupted" => "The operation was interrupted. Refresh status before trying again.",
        "ConfigurationUnknown" => "Setup could not be confirmed. Refresh status to try again.",
        "PluginStatusUnknown" => "The plugin could not be checked. Refresh status or review the details.",
        "MarketplaceStatusUnknown" => "The plugin source could not be checked. Refresh status to try again.",
        "InstallationIncomplete" => "Installation was not confirmed. Refresh status before trying again.",
        _ => "Review the setup details and next steps to continue."
    };

    private async void CodexDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _dialogOpen) return;
        CodexDetailsParkingHost.Child = null;
        var dialog = DetailsDialog("Connection details", CodexDetailsContent);
        try
        {
            await ShowDialogAsync(dialog);
        }
        finally
        {
            if (dialog.Content is ScrollViewer scroll) scroll.Content = null;
            CodexDetailsParkingHost.Child = CodexDetailsContent;
        }
    }

    private void RenderCodexChecks(IReadOnlyList<CodexSetupCheck> checks)
    {
        CodexChecks.Children.Clear();
        foreach (var check in checks)
        {
            var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            row.Children.Add(Body(check.Name));
            var result = Body(check.Result);
            result.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            Grid.SetColumn(result, 1);
            row.Children.Add(result);
            CodexChecks.Children.Add(row);
        }
    }

    private async void CodexHelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _dialogOpen || _codexStatus is null) return;
        var view = CodexSetupPresentation.FromStatus(_codexStatus);
        var content = new StackPanel { Spacing = 16 };
        for (var index = 0; index < view.Steps.Count; index++)
            content.Children.Add(Body($"{index + 1}. {view.Steps[index]}"));
        await ShowDialogAsync(DetailsDialog(view.Title, content));
    }

    private void CopyCodexResultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrEmpty(_codexReport)) return;
        try
        {
            var data = new DataPackage();
            data.SetText(_codexReport);
            Clipboard.SetContent(data);
            CopyCodexResultsButton.Content = "Results copied";
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Copying Codex setup results failed ({ErrorType})", exception.GetType().Name);
            CopyCodexResultsButton.Content = "Copy failed — try again";
        }
    }
}
