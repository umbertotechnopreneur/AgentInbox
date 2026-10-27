/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Owned account checks and sequential saved-account verification.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using MailMeUp.Desktop.Models;
using MailMeUp.Desktop.Services;
using MailMeUp.Desktop.Views;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    /// <summary>Show the retained session result for the selected account check.</summary>
    /// <param name="sender">Native event source; business state is obtained from the model.</param>
    /// <param name="args">Arguments supplied by the launch or WinUI event; no mail operation is inferred from them.</param>
    private async void AccountCheckDetails_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Button { DataContext: MailMeUp.Desktop.ViewModels.AccountListItemModel row }
            && row.HasCheckResult)
            await RunAsync(() =>
            {
                _configuration.Notice.Show($"Account check: {row.Label}", row.CheckDescription, row.CheckTone);
                return Task.CompletedTask;
            });
    }

    /// <summary>Check saved snapshots sequentially, stopping at cancellation or the first failure.</summary>
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

    /// <summary>Capture a validated editor snapshot without reading control values.</summary>
    /// <param name="checking">Whether to validate a connection check rather than a settings save.</param>
    /// <param name="smtpOnly">Whether to check only SMTP without requiring incoming credentials.</param>
    private AccountSnapshot CaptureAccount(bool checking = false, bool smtpOnly = false)
    {
        try { return _configuration.Editor.Snapshot(checking, smtpOnly); }
        catch (EngineException) { FocusAccountError(); throw; }
    }

    /// <summary>Save the current draft through the shared configuration model.</summary>
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

    /// <summary>Check the current sending configuration without submitting mail.</summary>
    private Task TestSmtpAsync() => ShowAccountCheckAsync(saveAccount: false, smtpOnly: true);
    /// <summary>Open an account check for the current draft.</summary>
    /// <param name="saveAccount">Whether to save the checked account after explicit folder selection.</param>
    private Task CheckAccountAsync(bool saveAccount) => ShowAccountCheckAsync(saveAccount, smtpOnly: false);

    /// <summary>Own the check window until its operation and close handling complete.</summary>
    /// <param name="saveAccount">Whether to save the checked account after explicit folder selection.</param>
    /// <param name="smtpOnly">Whether to check only SMTP without requiring incoming credentials.</param>
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
