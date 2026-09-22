using MailMeUp.Desktop.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private string _codexReport = string.Empty;
    private bool? _keepCodexPlugin;
    private bool _renderingCodexChoices;

    private bool IsCodexSetupReady => CodexSetupPresentation.IsLocalSetupReady(_codexStatus)
        && (_codexStatus?.Code != "DirectConfigured" || _keepCodexPlugin != true);

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
            CopyCodexResultsButton.Visibility = InstallPluginButton.Visibility = RefreshCodexButton.Visibility = Visibility.Collapsed;
            CodexStatusPanel.Visibility = Visibility.Visible;
            CodexConnectionChoices.Visibility = CodexNextStepsPanel.Visibility = CodexVerificationPanel.Visibility = CodexFirstTaskPanel.Visibility = Visibility.Collapsed;
            SwitchCodexConnectionButton.Visibility = Visibility.Collapsed;
            InstallPluginButton.IsEnabled = false;
            CodexStatusTitle.Text = install ? "Adding AgentInbox to Codex…" : "Checking your Codex settings…";
            CodexStatusText.Text = install ? "Installing the AgentInbox plugin on this device." : "Looking for an existing AgentInbox connection.";
            CodexStatusCaption.Text = "Your email is not read during this step.";
            CodexStatusIcon.Glyph = "\uE895";
            CodexDiagnosticText.Text = "Waiting for local configuration results. No prompt or mailbox check is running.";
            CodexCheckedText.Text = "Check in progress…";
            RenderCodexChecks(CodexSetupCheck.Pending());
            UpdateCodexPageHeading();
            UpdateCodexFooter();
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
        if (status.Code == "DirectRegistrationExists")
            _keepCodexPlugin ??= true;
        else if (status.Code == "DirectConfigured")
            _keepCodexPlugin ??= false;
        CodexDiagnosticText.Text = status.Message;
        var checks = status.Checks.Count > 0 ? status.Checks : CodexSetupCheck.Pending();
        RenderCodexChecks(checks);
        CodexCheckedText.Text = $"Last attempt: {DateTimeOffset.Now:HH:mm:ss} · Local configuration only";
        _codexReport = string.Join(Environment.NewLine,
            new[] { "AgentInbox — Codex setup", CodexCheckedText.Text, $"Result: {status.Code}", status.Message }
                .Concat(checks.Select(check => $"{check.Name}: {check.Result}"))
                .Append("Runtime connection, Codex sign-in and mailbox access: not tested."));
        CopyCodexResultsButton.Content = "Copy setup results";
        CopyCodexResultsButton.Visibility = Visibility.Visible;
        CopyCodexPromptButton.Content = "Copy prompt";
        RenderCodexPage();
        UpdateProgress();
    }

    private void RenderCodexPage()
    {
        if (_codexStatus is not { } status) return;
        var view = CodexSetupPresentation.FromStatus(status);
        var ready = IsCodexSetupReady;
        var chooseConnection = !ready && (status.Code is "DirectRegistrationExists" or "DirectConfigured");

        CodexStatusPanel.Visibility = ToVisibility(!chooseConnection);
        CodexStatusTitle.Text = status.Code == "DirectConfigured" && !ready ? "Direct connection configured" : view.Title;
        CodexStatusText.Text = CodexStatusSummary(status);
        CodexStatusCaption.Text = ready ? "Settings checked. Try your first request below to test the connection."
            : status.Code == "ReadyToInstall" ? "Select Install plugin to add AgentInbox to Codex."
            : "Your saved account and sharing choices are kept.";
        CodexStatusIcon.Glyph = ready ? "\uE73E" : status.Code == "ReadyToInstall" ? "\uE943" : "\uE946";

        CodexConnectionChoices.Visibility = ToVisibility(chooseConnection);
        _renderingCodexChoices = true;
        CodexPluginChoice.IsChecked = _keepCodexPlugin == true;
        CodexDirectChoice.IsChecked = _keepCodexPlugin == false;
        _renderingCodexChoices = false;
        CodexPluginChoiceStatus.Text = status.Checks.FirstOrDefault(check => check.Name == "AgentInbox plugin")?.Result
            ?? (status.IsPluginConfigured ? "Installed and enabled" : "Not checked");
        CodexDirectChoiceStatus.Text = status.DirectRegistrationCount > 1 ? $"{status.DirectRegistrationCount} entries found"
            : status.IsDirectRegistrationEnabled switch
            {
                true => "Entry enabled", false => "Entry disabled", null => "Entry found · State unknown"
            };
        CodexPluginChoiceCard.Background = ThemeBrush(_keepCodexPlugin == true ? "WizardSelectionBrush" : "CardBackgroundFillColorDefaultBrush");
        CodexPluginChoiceCard.BorderBrush = ThemeBrush(_keepCodexPlugin == true ? "WizardAccentBrush" : "CardStrokeColorDefaultBrush");
        CodexDirectChoiceCard.Background = ThemeBrush(_keepCodexPlugin == false ? "WizardSelectionBrush" : "CardBackgroundFillColorDefaultBrush");
        CodexDirectChoiceCard.BorderBrush = ThemeBrush(_keepCodexPlugin == false ? "WizardAccentBrush" : "CardStrokeColorDefaultBrush");

        CodexFirstTaskPanel.Visibility = ToVisibility(ready);
        CodexNextStepsPanel.Visibility = ToVisibility(!ready);
        CodexNextStepNumber.Visibility = ToVisibility(chooseConnection);
        CodexVerificationPanel.Visibility = ToVisibility(chooseConnection);
        CodexNextStepsTitle.Text = chooseConnection ? "Update Codex"
            : status.Code == "ReadyToInstall" ? "What happens next" : "Next step";
        CodexNextStepsText.Text = chooseConnection
            ? CodexSetupPresentation.ConnectionInstructions(status, _keepCodexPlugin == true)
            : string.Join(Environment.NewLine + Environment.NewLine, view.Steps.Where(step => step != status.Message));
        RefreshCodexButton.Visibility = ToVisibility(ready);
        InstallPluginButton.Visibility = ToVisibility(ready && status.CanInstall);
        InstallPluginButton.IsEnabled = status.CanInstall;
        SwitchCodexConnectionButton.Visibility = ToVisibility(ready && status.Code == "DirectConfigured");
        UpdateCodexPageHeading();
        UpdateCodexFooter();
    }

    private void UpdateCodexPageHeading()
    {
        if (_step != 3) return;
        PageTitle.Text = IsCodexSetupReady ? "You're ready to try AgentInbox" : "Finish connecting to Codex";
        PageSubtitle.Text = IsCodexSetupReady ? "Your selected accounts, one conversation."
            : _codexStatus?.Code is "DirectRegistrationExists" or "DirectConfigured"
                ? "Choose one connection, update Codex, then verify it here."
                : "Let Codex find emails and appointments in the accounts you choose.";
    }

    private void UpdateCodexFooter()
    {
        FinishLaterButton.Visibility = ToVisibility(_step == 3 && !IsCodexSetupReady);
        FinishLaterButton.IsEnabled = !_busy;
        if (_step != 3) return;
        NextButton.Content = IsCodexSetupReady ? "Finish setup"
            : _codexStatus is { CanInstall: true } ? "Install plugin"
            : _codexStatus is null ? "Check Codex setup" : "Verify connection";
        NextButton.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
    }

    private void CodexConnectionChoice_Checked(object sender, RoutedEventArgs e)
    {
        if (_renderingCodexChoices || !_loaded || _busy) return;
        _keepCodexPlugin = ReferenceEquals(sender, CodexPluginChoice);
        RenderCodexPage();
        UpdateProgress();
        if (IsCodexSetupReady) CopyCodexPromptButton.Focus(FocusState.Programmatic);
    }

    private void SwitchCodexConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _dialogOpen) return;
        _keepCodexPlugin = true;
        RenderCodexPage();
        UpdateProgress();
        PageScroll.ChangeView(null, 0, null, true);
    }

    private static string CodexStatusSummary(CodexSetupStatus status) => status.Code switch
    {
        "ReadyToInstall" => "The local plugin is ready to add to Codex.",
        "PluginConfigured" => "AgentInbox plugin installed and enabled.",
        "DirectConfigured" => "One direct AgentInbox entry is enabled, with no enabled AgentInbox plugin.",
        "DirectRegistrationExists" => status.IsPluginConfigured
            ? "Choose one AgentInbox connection in Codex to avoid duplicate tools."
            : "You can keep your existing connection or review how to switch to the plugin.",
        "PluginDisabled" => "Enable AgentInbox in Codex's plugin settings, then verify the connection here.",
        "OtherPluginExists" => "Review the existing AgentInbox plugin before adding this local copy.",
        "AliasUnavailable" => "Restore the portable folder or enable the installed AgentInbox command to continue.",
        "CodexUnavailable" => "Automatic setup needs the native Codex command-line app.",
        "MarketplaceConflict" => "Review the existing local marketplace before installing AgentInbox.",
        "CheckInterrupted" => "The operation was interrupted. Verify the connection before trying again.",
        "ConfigurationUnknown" => "Setup could not be confirmed. Verify the connection to try again.",
        "PluginStatusUnknown" => "The plugin could not be checked. Verify the connection or review the details.",
        "MarketplaceStatusUnknown" => "The plugin source could not be checked. Verify the connection to try again.",
        "InstallationIncomplete" => "Installation was not confirmed. Verify the connection before trying again.",
        _ => "Review the setup details and next steps to continue."
    };

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

    private void CopyCodexPromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !IsCodexSetupReady) return;
        try
        {
            var data = new DataPackage();
            data.SetText(CodexFirstTaskPrompt.Text);
            Clipboard.SetContent(data);
            CopyCodexPromptButton.Content = "Prompt copied";
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Copying the first Codex prompt failed ({ErrorType})", exception.GetType().Name);
            CopyCodexPromptButton.Content = "Copy failed — select the prompt above";
        }
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
