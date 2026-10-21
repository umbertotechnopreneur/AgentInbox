// SPDX-License-Identifier: MIT

using System.Text.Json;
using MailMeUp.Desktop.Services;
using MailMeUp.Desktop.Views;
using Microsoft.UI.Xaml.Controls;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    /// <summary>Keeps only explicit folder choices when loading or refreshing the selectable server list.</summary>
    private void SetFolderChoices(IReadOnlyList<string> available, IReadOnlyCollection<string> selected)
    {
        var loading = _loadingForm;
        _loadingForm = true;
        try
        {
            _folders.Items.Clear();
            foreach (var folder in available)
            {
                _folders.Items.Add(folder);
                if (selected.Contains(folder)) _folders.SelectedItems.Add(folder);
            }
        }
        finally { _loadingForm = loading; }
    }

    /// <summary>Captures one consistent form snapshot for both connection checks and saves.</summary>
    private (Dictionary<string, object?> Account, Dictionary<string, string> Passwords, int? Revision) ReadAccountForm(bool checking = false)
    {
        if (_authentication.SelectedIndex < 0)
            throw new EngineException("Choose how to connect to these mail servers.");
        if (_authentication.SelectedIndex == 1 && !UsesMicrosoftMailServers())
            throw new EngineException("Microsoft sign-in only works with Microsoft mail. Check the servers under Read mail and Send mail.");
        var pop = _incomingProtocol.SelectedIndex == 1;
        string[] folders = pop ? ["INBOX"] : _folders.SelectedItems.Cast<string>().ToArray();
        if (checking && folders.Length == 0) folders = ["INBOX"];
        if (folders.Length is < 1 or > 20 || folders.Any(folder => folder.Any(character => character < 32 || character > 126)))
            throw new EngineException("Choose between one and twenty ASCII server folders.");
        var authentication = new Dictionary<string, string> { ["method"] = "password" };
        if (_authentication.SelectedIndex == 1)
        {
            if (!Guid.TryParse(_microsoftClientId, out var clientId))
                throw new EngineException("This app's Microsoft settings need fixing. The saved app ID is invalid.");
            var tenant = _tenant.SelectedIndex switch { 0 => "common", 1 => "organizations", 2 => "consumers", _ => _tenantId.Text.Trim() };
            if (_tenant.SelectedIndex == 3 && !Guid.TryParse(tenant, out _))
                throw new EngineException("Enter the organization ID from your IT team, or choose Personal or Work or school.");
            authentication = new() { ["method"] = "microsoft_oauth", ["client_id"] = clientId.ToString(), ["tenant"] = tenant };
        }
        var account = new Dictionary<string, object?>
        {
            ["id"] = _accountId, ["label"] = _label.Text.Trim(), ["email"] = _email.Text.Trim(),
            ["preset"] = _preset.SelectedIndex switch
            {
                0 => "custom", 1 => "gmail", 2 => "icloud", 3 => "outlook", 4 => "microsoft365",
                _ => throw new EngineException("Choose your email provider.")
            },
            ["authentication"] = authentication,
            ["incoming"] = Connection(_incomingHost, _incomingPort, _incomingTls, _incomingUser, pop ? "pop3" : "imap"),
            ["smtp"] = _configureSmtp.IsOn ? Connection(_smtpHost, _smtpPort, _smtpTls, _smtpUser) : null,
            ["read_enabled"] = _readEnabled.IsOn, ["send_enabled"] = _configureSmtp.IsOn && _sendEnabled.IsOn,
            ["folders"] = folders
        };
        var passwords = new Dictionary<string, string>
        {
            ["incoming"] = _authentication.SelectedIndex == 0 ? _incomingPassword.Password : "",
            ["smtp"] = _authentication.SelectedIndex == 0 && _configureSmtp.IsOn ? _smtpPassword.Password : ""
        };
        return (account, passwords, _revision);
    }

    /// <summary>Checks password-based new accounts before saving; keeps explicit Microsoft sign-in preparation separate.</summary>
    private async Task SaveAccountAsync()
    {
        if (_creatingAccount && _authentication.SelectedIndex == 0)
        {
            await CheckAccountAsync(saveAccount: true);
            return;
        }
        var form = ReadAccountForm();
        try { ApplySavedAccount(await CommitAccountAsync(form.Account, form.Passwords, form.Revision)); }
        finally { form.Passwords.Clear(); }
    }

    /// <summary>Uses the shared check window for SMTP progress, cancellation and the final server result.</summary>
    private async Task TestSmtpAsync()
    {
        using var window = new AccountCheckWindow(this, smtp: false, saveAccount: false, smtpOnly: true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, window.StopToken);
        using var lifetime = _lifetime.Token.Register(() => window.DispatcherQueue.TryEnqueue(window.Close));
        window.Activate();
        Dictionary<string, string>? passwords = null;
        try
        {
            window.Report(new AccountCheckProgress("settings", "started"));
            var form = ReadAccountForm(checking: true);
            passwords = form.Passwords;
            await _engine.ManageAsync("check_smtp", new
            {
                account = form.Account, passwords = form.Passwords, expected_revision = form.Revision
            }, cancellation.Token, window.Report);
            window.Complete(true, "The secure connection and sign-in passed. No email was sent and no settings were saved.");
        }
        catch (OperationCanceledException)
        {
            window.Complete(false, "The sending check was cancelled or took too long. No email was sent and no settings were saved.");
        }
        catch (Exception exception)
        {
            window.Complete(false, exception is EngineException ? exception.Message
                : "The sending check could not finish. No email was sent.");
        }
        finally { passwords?.Clear(); }
        await window.ClosedTask;
    }

    /// <summary>Runs actual checks against the captured form, then lets the owner select folders and optionally save.</summary>
    private async Task CheckAccountAsync(bool saveAccount)
    {
        using var window = new AccountCheckWindow(this, _configureSmtp.IsOn, saveAccount);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, window.StopToken);
        using var lifetime = _lifetime.Token.Register(() => window.DispatcherQueue.TryEnqueue(window.Close));
        window.Activate();
        Dictionary<string, string>? passwords = null;
        JsonElement? committed = null;
        var saveStarted = false;
        try
        {
            window.Report(new AccountCheckProgress("settings", "started"));
            var form = ReadAccountForm(checking: true);
            passwords = form.Passwords;
            var result = await _engine.ManageAsync("check_account", new
            {
                account = form.Account, passwords = form.Passwords, expected_revision = form.Revision
            }, cancellation.Token, window.Report);
            cancellation.Token.ThrowIfCancellationRequested();
            var available = result.GetProperty("folders").EnumerateArray().Select(folder => folder.GetString()!).ToArray();
            if (available.Length == 0) throw new EngineException("The server returned no selectable folders. No account was saved.");
            var selected = _folders.SelectedItems.Cast<string>().ToArray();
            window.ShowFolders(available, selected, Value(result, "protocol") == "pop3");
            var chosen = await window.FolderSelection;
            if (chosen is null) return;
            form.Account["folders"] = chosen;
            var readingSummary = "No message was read and no email was sent.";
            if (window.ReadMessageRequested)
            {
                window.BeginReadCheck();
                cancellation.Token.ThrowIfCancellationRequested();
                var reading = await _engine.ManageAsync("check_account", new
                {
                    account = form.Account, passwords = form.Passwords, expected_revision = form.Revision, read_message = true
                }, cancellation.Token, window.Report);
                cancellation.Token.ThrowIfCancellationRequested();
                available = reading.GetProperty("folders").EnumerateArray().Select(folder => folder.GetString()!).ToArray();
                readingSummary = Value(reading, "message_read") == "passed"
                    ? "One message was read successfully. No email was sent."
                    : "The selected folder is empty: sign-in passed, but no message read could be tested. No email was sent.";
            }
            SetFolderChoices(available, chosen);
            MarkAccountChanged();
            if (saveAccount)
            {
                window.BeginSaving();
                saveStarted = true;
                // A save is one commit; closing the progress window cannot cancel it halfway through.
                committed = await CommitAccountAsync(form.Account, form.Passwords, form.Revision);
                window.Report(new AccountCheckProgress("save", "passed"));
                ApplySavedAccount(committed.Value);
            }
            window.Complete(true, (saveAccount
                ? "Your account was added with the selected folders. "
                : "Connection and sign-in passed. Folder choices are in your form; save when you are ready. ")
                + readingSummary + " You can close this window.");
        }
        catch (OperationCanceledException)
        {
            window.Complete(false, saveStarted
                ? "The save result could not be confirmed. Reload your accounts before trying again."
                : "The check was cancelled or timed out. No account was saved.");
        }
        catch (Exception exception)
        {
            var failure = exception is EngineException ? exception.Message : "The account check could not finish. No success is assumed.";
            if (saveStarted && committed is null) failure += " Reload your accounts before trying to save again.";
            window.Complete(committed is not null, committed is not null
                ? "Your account was saved, but the form could not refresh. Close this window and reload your accounts."
                : failure);
        }
        finally { passwords?.Clear(); }
        await window.ClosedTask;
    }

    /// <summary>Returns the committed save result separately from subsequent local rendering.</summary>
    private Task<JsonElement> CommitAccountAsync(Dictionary<string, object?> account, Dictionary<string, string> passwords, int? revision) =>
        _engine.ManageAsync("save_account", new { account, passwords, expected_revision = revision }, _lifetime.Token);

    /// <summary>Reflects an already committed account before showing its separate cleanup outcome.</summary>
    private void ApplySavedAccount(JsonElement saved)
    {
        var savedId = Value(saved, "id");
        _accounts.RemoveAll(item => Value(item, "id") == savedId);
        _accounts.Add(saved.Clone());
        _creatingAccount = false;
        PopulateAccountPicker(savedId);
        ShowNotice("Account saved", "Your assistant can use only the access you enabled.", InfoBarSeverity.Success);
        ShowCleanup(saved);
    }
}
