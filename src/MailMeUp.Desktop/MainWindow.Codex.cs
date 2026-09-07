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
        await RunAsync(install ? "Installing the local Codex plugin…" : "Checking local Codex setup…", async token =>
        {
            // Clear stale success and install eligibility before any new operation, including cancellation.
            _codexStatus = null;
            _codexReport = string.Empty;
            CopyCodexResultsButton.Visibility = CodexHelpButton.Visibility = InstallPluginButton.Visibility = Visibility.Collapsed;
            InstallPluginButton.IsEnabled = false;
            CodexStatusTitle.Text = install ? "Installing local plugin…" : "Checking local setup…";
            CodexStatusText.Text = "Waiting for local configuration results. No prompt or mailbox check is running.";
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
        CodexStatusTitle.Text = view.Title;
        CodexStatusText.Text = status.Message;
        InstallPluginButton.IsEnabled = status.CanInstall;
        InstallPluginButton.Visibility = ToVisibility(status.CanInstall);
        InstallPluginButton.Content = status.IsPluginConfigured ? "Update local plugin" : "Install local plugin";
        CodexHelpButton.Content = view.HelpLabel;
        CodexHelpButton.Visibility = Visibility.Visible;
        CodexHelpButton.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[
            status.CanInstall ? "DefaultButtonStyle" : "AccentButtonStyle"];
        RefreshCodexButton.Content = "Refresh status";
        RefreshCodexButton.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["QuietButton"];
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
