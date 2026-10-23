// SPDX-License-Identifier: MIT

using MailMeUp.Desktop.Models;
using MailMeUp.Desktop.Services;
using MailMeUp.Desktop.Views;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private async Task TestAllAccountsAsync()
    {
        var accounts = _configuration.AccountItems.ToArray();
        foreach (var row in accounts) row.ResetCheck();
        foreach (var row in accounts)
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            var snapshot = new AccountSnapshot(row.Account, new() { ["incoming"] = "", ["smtp"] = "" }, row.Account.Revision);
            using var model = _configuration.CreateCheck(snapshot, saveAccount: false, smtpOnly: false, savedCheck: true);
            using var window = new AccountCheckWindow(this, model);
            using var lifetime = _lifetime.Token.Register(() => window.DispatcherQueue.TryEnqueue(window.Close));
            window.Activate();
            await model.RunAsync(_lifetime.Token);
            row.RecordCheck(model.Succeeded, model.Cancelled, model.Message);
            await window.ClosedTask;
            if (!model.Succeeded) return;
            if (model.Cancelled) return;
        }
    }

    private AccountSnapshot CaptureAccount(bool checking = false, bool smtpOnly = false)
    {
        try { return _configuration.Editor.Snapshot(checking, smtpOnly); }
        catch (EngineException) { FocusAccountError(); throw; }
    }

    private async Task SaveAccountAsync()
    {
        if (_configuration.Editor.NeedsCheckBeforeSave)
        {
            await CheckAccountAsync(saveAccount: true);
            return;
        }
        var snapshot = CaptureAccount();
        try { await _configuration.SaveAsync(snapshot, _lifetime.Token); }
        finally { snapshot.ClearPasswords(); }
    }

    private Task TestSmtpAsync() => ShowAccountCheckAsync(saveAccount: false, smtpOnly: true);
    private Task CheckAccountAsync(bool saveAccount) => ShowAccountCheckAsync(saveAccount, smtpOnly: false);

    private async Task ShowAccountCheckAsync(bool saveAccount, bool smtpOnly)
    {
        var snapshot = CaptureAccount(checking: true, smtpOnly: smtpOnly);
        using var model = _configuration.CreateCheck(snapshot, saveAccount, smtpOnly);
        using var window = new AccountCheckWindow(this, model);
        using var lifetime = _lifetime.Token.Register(() => window.DispatcherQueue.TryEnqueue(window.Close));
        window.Activate();
        await model.RunAsync(_lifetime.Token);
        await window.ClosedTask;
    }
}
