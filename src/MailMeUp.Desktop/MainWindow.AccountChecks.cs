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
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using MailMeUp.Desktop.Models;
using MailMeUp.Desktop.Services;
using MailMeUp.Desktop.ViewModels;
using MailMeUp.Desktop.Views;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    /// <summary>Show the retained session result for the selected account check.</summary>
    /// <param name="sender">Native event source; business state is obtained from the model.</param>
    /// <param name="args">Arguments supplied by the launch or WinUI event; no mail operation is inferred from them.</param>
    private async void AccountCheckDetails_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Button { DataContext: AccountListItemModel row }
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
            using var model = _configuration.CreateCheck(snapshot, saveAccount: false, savedCheck: true);
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

    /// <summary>Save the current draft through the shared configuration model.</summary>
    private async Task SaveAccountAsync()
    {
        if (_configuration.Editor.NeedsCheckBeforeSave)
        {
            await CheckAccountAsync(saveAccount: true);
            return;
        }
        try
        {
            var snapshot = _configuration.Editor.Snapshot();
            try { await _configuration.SaveAsync(snapshot, _lifetime.Token); }
            finally { snapshot.ClearPasswords(); }
        }
        catch (EngineException error) { ShowAccountError(error); throw; }
    }

    /// <summary>Check the current sending configuration without submitting mail.</summary>
    private async Task TestSmtpAsync()
    {
        try { await ShowAccountCheckAsync(_configuration.CreateSmtpCheck(_configuration.Editor.CaptureSmtpCheck())); }
        catch (EngineException error) { ShowAccountError(error); throw; }
    }

    /// <summary>Open an account check for the current draft.</summary>
    /// <param name="saveAccount">Whether to save the checked account after explicit folder selection.</param>
    private async Task CheckAccountAsync(bool saveAccount)
    {
        try { await ShowAccountCheckAsync(_configuration.CreateCheck(_configuration.Editor.Snapshot(), saveAccount)); }
        catch (EngineException error) { ShowAccountError(error); throw; }
    }

    /// <summary>Own the check window until its operation and close handling complete.</summary>
    /// <param name="model">Owned progress and outcome model for the requested connection check.</param>
    private async Task ShowAccountCheckAsync(AccountCheckModel model)
    {
        using var check = model;
        using var window = new AccountCheckWindow(this, model);
        using var lifetime = _lifetime.Token.Register(() => window.DispatcherQueue.TryEnqueue(window.Close));
        window.Activate();
        await model.RunAsync(_lifetime.Token);
        await window.ClosedTask;
        if (!model.Succeeded) FocusAccountError();
    }
}
