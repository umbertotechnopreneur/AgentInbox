// SPDX-License-Identifier: MIT

using System.Text.Json;
using MailMeUp.Desktop.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private async Task SignInMicrosoftAsync()
    {
        _microsoftLog.Text = "";
        _signInClock.Restart();
        AppendMicrosoftLog("Checking the account settings.");
        var flowId = "";
        var connected = false;
        try
        {
            if (_authentication.SelectedIndex != 1)
                throw new EngineException("Choose Sign in with Microsoft under How to connect.");
            if (_creatingAccount || _accountId.Length == 0 || _hasUnsavedChanges)
            {
                AppendMicrosoftLog("Saving the account settings before sign-in.");
                await SaveAccountAsync();
                AppendMicrosoftLog("Account settings saved.");
            }
            var start = await _engine.ManageAsync("start_microsoft_signin", new { account_id = _accountId },
                _lifetime.Token, diagnostic: AppendMicrosoftLog);
            flowId = Value(start, "flow_id");
            connected = await CompleteMicrosoftSignInAsync(start);
        }
        catch (EngineException exception)
        {
            AppendMicrosoftLog(exception.Message);
            throw;
        }
        catch (OperationCanceledException)
        {
            AppendMicrosoftLog("Sign-in was cancelled or took too long.");
            throw;
        }
        catch (Exception)
        {
            AppendMicrosoftLog("The app could not finish sign-in. No success is assumed.");
            throw;
        }
        finally
        {
            if (flowId.Length > 0 && !connected && !_lifetime.IsCancellationRequested)
            {
                try
                {
                    AppendMicrosoftLog("Removing the temporary sign-in data.");
                    var cleanup = await _engine.ManageAsync("cancel_microsoft_signin", new { flow_id = flowId }, _lifetime.Token);
                    ShowCleanup(cleanup);
                    AppendMicrosoftLog(cleanup.GetProperty("cleanup_pending").GetBoolean()
                        ? "Temporary-data cleanup is still pending. Use Retry cleanup." : "Temporary sign-in data removed.");
                }
                catch (Exception exception)
                {
                    AppendMicrosoftLog("Temporary-data cleanup could not finish. Use Retry cleanup."
                        + (exception is EngineException ? " " + exception.Message : ""));
                    CleanupNotice.Message = "Temporary sign-in data may still be on this computer. Use Retry cleanup.";
                    CleanupNotice.IsOpen = true;
                }
            }
            _signInClock.Stop();
        }
        if (!connected) return;
        AppendMicrosoftLog("Signed in. Windows saved the sign-in securely.");
        try { await LoadAccountsAsync(); }
        catch (Exception)
        {
            AppendMicrosoftLog("Sign-in is saved, but the account list could not refresh. Reload the accounts.");
            ShowNotice("Signed in with Microsoft", "Sign-in is saved. Reload the accounts to update this page.", InfoBarSeverity.Warning);
            return;
        }
        ShowNotice("Signed in with Microsoft", "You can now check reading and sending in the account settings.", InfoBarSeverity.Success);
    }

    private async Task<bool> CompleteMicrosoftSignInAsync(JsonElement start)
    {
        var flowId = Value(start, "flow_id");
        var verificationUri = MicrosoftVerificationUri(Value(start, "verification_uri"));
        var expiresIn = start.GetProperty("expires_in").GetInt32();
        var interval = start.GetProperty("interval").GetInt32();
        if (flowId.Length == 0 || expiresIn is < 1 or > 3600 || interval is < 1 or > 300)
            throw new EngineException("Microsoft could not start sign-in. Please try again.");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        using var flowCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        AppendMicrosoftLog("Ready. Open Microsoft and enter the displayed code. Codes are excluded from these details.");
        var status = Text("Waiting for you to sign in on Microsoft's page.");
        var userCode = new TextBox
        {
            Text = Value(start, "user_code"), Width = 240,
            IsReadOnly = true, FontSize = 24, FontFamily = new FontFamily("Consolas"),
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(userCode, "Microsoft sign-in code");
        var copyCode = Action("Copy code", () =>
        {
            try
            {
                CopyText(userCode.Text);
                status.Text = "Code copied. Paste it on Microsoft's page.";
            }
            catch (EngineException exception) { status.Text = exception.Message; }
        }, tooltip: "Copy this code to paste on Microsoft's sign-in page.");
        copyCode.Content = new SymbolIcon(Symbol.Copy);
        copyCode.HorizontalAlignment = HorizontalAlignment.Left;
        copyCode.VerticalAlignment = VerticalAlignment.Center;
        var codeRow = new Grid { ColumnSpacing = 8 };
        codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(userCode, 1);
        Grid.SetColumn(copyCode, 2);
        codeRow.Children.Add(userCode);
        codeRow.Children.Add(copyCode);
        var codeLabel = Text("Enter this code");
        codeLabel.TextAlignment = TextAlignment.Center;
        var openBrowser = Action("Open Microsoft", async () =>
        {
            try
            {
                var opened = await Windows.System.Launcher.LaunchUriAsync(verificationUri);
                AppendMicrosoftLog(opened ? "Microsoft's page opened in the browser." : "Windows could not open the browser.");
                status.Text = opened
                    ? "Enter the code on Microsoft's page and sign in. I'll wait here."
                    : "Windows could not open the browser. Open the displayed Microsoft address manually.";
            }
            catch (Exception)
            {
                AppendMicrosoftLog("Windows could not open the browser.");
                status.Text = "Windows could not open the browser. Open the displayed Microsoft address manually.";
            }
        }, accent: true);
        var details = SignInLogBox();
        details.SetBinding(TextBox.TextProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Source = _microsoftLog, Path = new PropertyPath("Text"), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay
        });
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "Sign in with Microsoft", CloseButtonText = "Cancel",
            CloseButtonStyle = DialogButtonStyle("Cancel Microsoft sign-in."),
            DefaultButton = ContentDialogButton.Close,
            Content = Stack(
                Hint("Press Open Microsoft, enter this code on the page, then sign in with your Microsoft account."),
                codeLabel, codeRow, Text(verificationUri.AbsoluteUri), openBrowser, status,
                Hint($"This code expires at {deadline.ToLocalTime():t}. You can check reading and sending after signing in."), SignInDetails(details))
        };
        var connected = false;
        string? failure = null;
        JsonElement? cleanupResult = null;

        async Task PollAsync()
        {
            try
            {
                while (!flowCancellation.IsCancellationRequested)
                {
                    var remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero) throw new EngineException("This code has expired. Press Sign in with Microsoft to get a new one.");
                    await Task.Delay(remaining < TimeSpan.FromSeconds(interval) ? remaining : TimeSpan.FromSeconds(interval), flowCancellation.Token);
                    if (DateTimeOffset.UtcNow >= deadline) throw new EngineException("This code has expired. Press Sign in with Microsoft to get a new one.");
                    var result = await _engine.ManageAsync("poll_microsoft_signin", new { flow_id = flowId },
                        flowCancellation.Token, diagnostic: AppendMicrosoftLog);
                    switch (Value(result, "status"))
                    {
                        case "connected":
                            cleanupResult = result.Clone();
                            connected = true;
                            AppendMicrosoftLog("Sign-in saved securely."
                                + (result.GetProperty("cleanup_pending").GetBoolean() ? " Temporary-data cleanup is still pending." : " Temporary code consumed."));
                            dialog.Hide();
                            return;
                        case "pending":
                            interval = result.GetProperty("interval").GetInt32();
                            if (interval is < 1 or > 300) throw new EngineException("Microsoft returned an invalid polling interval.");
                            AppendMicrosoftLog($"Waiting for Microsoft. Checking again in {interval} seconds.");
                            break;
                        default:
                            throw new EngineException("Microsoft sent a reply I couldn't read. Please try signing in again.");
                    }
                }
            }
            catch (OperationCanceledException) when (flowCancellation.IsCancellationRequested) { }
            catch (EngineException exception) { failure = exception.Message; dialog.Hide(); }
            catch (Exception)
            {
                failure = "Microsoft sign-in could not finish. Please try again.";
                dialog.Hide();
            }
        }

        using var windowCancellation = _lifetime.Token.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
        var polling = PollAsync();
        try { await dialog.ShowAsync(); }
        finally
        {
            flowCancellation.Cancel();
            await polling;
        }
        if (_lifetime.IsCancellationRequested) return connected;
        if (cleanupResult is { } cleanup) ShowCleanup(cleanup);
        if (failure is not null) throw new EngineException(failure);
        if (!connected)
        {
            AppendMicrosoftLog("Sign-in cancelled.");
            ShowNotice("Sign-in cancelled", "You can sign in with Microsoft again when you're ready.", InfoBarSeverity.Informational);
        }
        return connected;
    }

    private static Uri MicrosoftVerificationUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.Port == 0)
            throw new EngineException("Microsoft did not return a valid secure sign-in address. Try again.");
        return uri;
    }

    private static TextBox SignInLogBox()
    {
        var box = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 180 };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(box, "Microsoft sign-in details, with private sign-in values hidden");
        return box;
    }

    private static Expander SignInDetails(params UIElement[] content) => new()
    {
        Header = "Sign-in details", IsExpanded = false,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Content = Stack(content)
    };

    private void AppendMicrosoftLog(string message)
    {
        const int maximum = 12000;
        var text = _microsoftLog.Text + $"[{_signInClock.Elapsed:mm\\:ss}] {message}\n";
        if (text.Length > maximum)
        {
            var start = text.IndexOf('\n', text.Length - maximum);
            text = start >= 0 ? text[(start + 1)..] : text[^maximum..];
        }
        _microsoftLog.Text = text;
        _microsoftLog.Select(text.Length, 0);
    }

}
