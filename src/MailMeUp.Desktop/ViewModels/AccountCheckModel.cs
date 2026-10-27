/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: UI-independent account-check stages, folder choices and committed save outcomes.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.Collections.ObjectModel;
using System.Text.Json;
using MailMeUp.Desktop.Models;
using MailMeUp.Desktop.Services;

namespace MailMeUp.Desktop.ViewModels;

/// <summary>Owns connection-check progress, folder choices, cancellation and the known save outcome.</summary>
internal sealed class AccountCheckModel : ObservableModel, IDisposable
{
    private readonly PythonEngine _engine;
    private readonly ConfigurationModel _configuration;
    private AccountSnapshot _snapshot;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<string[]?> _selection = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _activeStage;
    private enum CheckPhase { Checking, SelectingFolders, Reading, Saving, Finished }
    private CheckPhase _phase = CheckPhase.Checking;
    private bool _closeWhenReady;
    private bool _closed;
    private bool _readMessageRequested;
    private bool _pop;
    private string _heading;
    private string _message = "Please wait while I check the connection and sign-in.";
    private readonly bool _savedCheck;

    /// <summary>Initialize the owned model or native view and its explicit dependencies.</summary>
    /// <param name="engine">Shared adapter for the local Python management contract.</param>
    /// <param name="configuration">Owner of saved snapshots, notifications and the current editor draft.</param>
    /// <param name="snapshot">Validated account settings, transient passwords and expected revision for this operation.</param>
    /// <param name="saveAccount">Whether to save the checked account after explicit folder selection.</param>
    /// <param name="smtpOnly">Whether to check only SMTP without requiring incoming credentials.</param>
    /// <param name="savedCheck">Whether this is a saved-account check that skips editing and folder-choice steps.</param>
    internal AccountCheckModel(PythonEngine engine, ConfigurationModel configuration, AccountSnapshot snapshot, bool saveAccount, bool smtpOnly, bool savedCheck = false)
    {
        _engine = engine; _configuration = configuration; _snapshot = snapshot;
        SaveAccount = saveAccount; SmtpOnly = smtpOnly;
        _savedCheck = savedCheck;
        _heading = savedCheck ? $"Checking {snapshot.Account.Label}" : smtpOnly ? "Checking sending connection" : "Checking your account";
        Stages.Add(new("settings", smtpOnly ? "Sending settings" : "Account settings"));
        Stages.Add(new("connection", "Secure connection"));
        Stages.Add(new("authentication", "Sign-in"));
        if (!smtpOnly)
        {
            Stages.Add(new("folders", "Server folders"));
            if (!savedCheck) Stages.Add(new("reading", "Read one message (optional)", "Not requested"));
            if (snapshot.Account.Smtp is not null) Stages.Add(new("smtp", "Sending connection and sign-in"));
            if (saveAccount) Stages.Add(new("save", "Save account"));
        }
    }

    public ObservableCollection<CheckStageModel> Stages { get; } = [];
    public ObservableCollection<string> AvailableFolders { get; } = [];
    public ObservableCollection<string> SelectedFolders { get; } = [];
    public bool UpdatingFolders { get; private set; }
    public bool SaveAccount { get; }
    public bool SmtpOnly { get; }
    public bool Succeeded { get; private set; }
    public bool Cancelled { get; private set; }
    public bool IsWorking => _phase is CheckPhase.Checking or CheckPhase.Reading or CheckPhase.Saving;
    public bool IsSaving => _phase == CheckPhase.Saving;
    public bool ShouldClose => _closeWhenReady && !IsWorking;
    public bool ShowFolders => AvailableFolders.Count > 0;
    public bool ShowPrimary => _phase == CheckPhase.SelectingFolders;
    public bool CanSelectFolders => ShowPrimary && !_pop;
    public bool CanChooseFolders => ShowPrimary && SelectedFolders.Count is >= 1 and <= 20;
    public bool CanClose => !IsSaving && !_stop.IsCancellationRequested;
    public string Heading => _heading;
    public string Message => _message;
    public string CloseText => IsWorking || (ShowPrimary && SaveAccount) ? "Cancel" : "Close";
    public string CloseHint => IsSaving ? "Wait while the account is saved." : IsWorking ? "Cancel this check without sending an email." : "Close the connection check window.";
    public string PrimaryText => (SaveAccount, ReadMessageRequested) switch
    {
        (true, true) => "Test reading and add account", (true, false) => "Add account",
        (false, true) => "Test reading and use folders", _ => "Use selected folders"
    };
    public string PrimaryHint => ReadMessageRequested ? "Test reading one message, then apply the selected folders. No email is sent."
        : SaveAccount ? "Save this checked account with only the selected folders." : "Use these folder choices in the form without saving it.";
    public bool ReadMessageRequested { get => _readMessageRequested; set { if (Set(ref _readMessageRequested, value)) Notify(); } }

    /// <summary>Forward an explicit folder selection into operation or editor state.</summary>
    /// <param name="folders">Exact folder names returned by the server or explicitly selected by the user.</param>
    internal void SelectFolders(IEnumerable<string> folders)
    {
        if (UpdatingFolders || !ShowPrimary) return;
        SelectedFolders.Clear();
        foreach (var folder in folders) SelectedFolders.Add(folder);
        Notify();
    }

    /// <summary>Complete the pending folder-choice step with the validated selection.</summary>
    internal void ChooseFolders()
    {
        if (!CanChooseFolders) return;
        var folders = SelectedFolders.ToArray();
        _phase = SaveAccount && !ReadMessageRequested ? CheckPhase.Saving : CheckPhase.Reading;
        Notify();
        _selection.TrySetResult(folders);
    }

    /// <summary>Request cancellation when safe while preserving an in-progress committed save.</summary>
    internal void RequestClose()
    {
        _closeWhenReady = true;
        if (!IsSaving) _stop.Cancel();
        Notify();
    }

    /// <summary>Release the pending folder choice and cancel checks after the window closes.</summary>
    internal void Closed()
    {
        _closed = true;
        _selection.TrySetResult(null);
        if (!IsSaving) _stop.Cancel();
    }

    /// <summary>Apply an engine progress record to the active check stage.</summary>
    /// <param name="progress">Optional account-check progress callback or one completed engine stage update.</param>
    internal void Report(AccountCheckProgress progress)
    {
        var stage = Stages.FirstOrDefault(item => item.Key == progress.Stage);
        if (_closed || !IsWorking || stage is null) return;
        stage.Status = progress.State switch { "passed" => "Passed", "empty" => "Empty folder", _ => "Checking…" };
        _activeStage = progress.State == "started" ? progress.Stage : null;
    }

    /// <summary>Expose selectable server folders while retaining valid snapshot selections.</summary>
    /// <param name="folders">Exact folder names returned by the server or explicitly selected by the user.</param>
    private void ShowFolderChoices(string[] folders)
    {
        _pop = _snapshot.Account.Incoming.Protocol == "pop3";
        _phase = CheckPhase.SelectingFolders;
        _heading = "Connection verified";
        _message = _pop ? "POP3 provides one mailbox: INBOX. No message was read or sent."
            : $"Found {folders.Length} selectable folders. Choose between one and twenty. No message was read or sent.";
        UpdatingFolders = true;
        try
        {
            foreach (var folder in folders) AvailableFolders.Add(folder);
            foreach (var folder in _snapshot.Account.Folders.Where(folders.Contains)) SelectedFolders.Add(folder);
        }
        finally { UpdatingFolders = false; }
        Notify();
    }

    /// <summary>Record the check outcome and finish operation presentation.</summary>
    /// <param name="succeeded">Whether the requested operation completed successfully within its stated scope.</param>
    /// <param name="message">Safe user-facing outcome or guidance; never raw credentials or mail content.</param>
    private void Complete(bool succeeded, string message)
    {
        Succeeded = succeeded;
        if (!succeeded && _activeStage is not null)
        {
            var stage = Stages.First(item => item.Key == _activeStage);
            stage.Status = Cancelled ? "Cancelled" : "Failed";
        }
        _phase = CheckPhase.Finished;
        _heading = SmtpOnly ? succeeded ? "Sending check complete" : "Sending check stopped"
            : succeeded ? SaveAccount ? "Account added" : "Account check complete" : "Account check stopped";
        if (_savedCheck && !succeeded) _heading = $"Check stopped: {_snapshot.Account.Label}";
        _message = message;
        Notify();
    }

    /// <summary>Performs engine calls and records their outcome without reading any UI control.</summary>
    /// <param name="lifetime">Configuration-window lifetime; saving uses this rather than view-close cancellation.</param>
    internal async Task RunAsync(CancellationToken lifetime)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime, _stop.Token);
        JsonElement? committed = null;
        var saving = false;
        try
        {
            Report(new("settings", "started"));
            var result = await _engine.ManageAsync(SmtpOnly ? "check_smtp" : "check_account", new
            {
                account = _snapshot.Account, passwords = _snapshot.Passwords, expected_revision = _snapshot.Revision
            }, cancellation.Token, Report);
            if (SmtpOnly)
            {
                Complete(true, "The secure connection and sign-in passed. No email was sent and no settings were saved.");
                return;
            }
            cancellation.Token.ThrowIfCancellationRequested();
            var available = result.GetProperty("folders").EnumerateArray().Select(folder => folder.GetString()!).ToArray();
            if (available.Length == 0) throw new EngineException("The server returned no selectable folders. No account was saved.");
            if (_savedCheck)
            {
                var missing = _snapshot.Account.Folders.Where(folder => !available.Contains(folder)).ToArray();
                if (missing.Length > 0) throw new EngineException("One or more saved folders are no longer available. Check this account's folder settings.");
                Complete(true, "Connection and sign-in passed. No message was read or sent and no settings were changed.");
                _closeWhenReady = true;
                Notify();
                return;
            }

            // Unsaved checks pause for an explicit folder choice before any sample read.
            ShowFolderChoices(available);
            var chosen = await _selection.Task.WaitAsync(cancellation.Token);
            if (chosen is null) return;
            _snapshot = _snapshot with { Account = _snapshot.Account with { Folders = chosen } };
            var readingSummary = "No message was read and no email was sent.";
            if (ReadMessageRequested)
            {
                _phase = CheckPhase.Reading;
                _heading = "Testing message reading";
                _message = "I will read one bounded sample from the first selected folder. No email will be sent.";
                Notify();
                var reading = await _engine.ManageAsync("check_account", new
                {
                    account = _snapshot.Account, passwords = _snapshot.Passwords, expected_revision = _snapshot.Revision, read_message = true
                }, cancellation.Token, Report);
                cancellation.Token.ThrowIfCancellationRequested();
                available = reading.GetProperty("folders").EnumerateArray().Select(folder => folder.GetString()!).ToArray();
                readingSummary = reading.GetProperty("message_read").GetString() == "passed"
                    ? "One message was read successfully. No email was sent."
                    : "The selected folder is empty: sign-in passed, but no message read could be tested. No email was sent.";
            }

            // Update the draft first; persistence remains a separate explicit operation.
            _configuration.Editor.ApplyFolderChoices(available, chosen);
            if (SaveAccount)
            {
                _phase = CheckPhase.Saving;
                _heading = "Saving your account";
                saving = true;
                Report(new("save", "started"));
                Notify();
                // A save is one commit; closing its view cannot cancel it halfway through.
                committed = await _configuration.SaveAsync(_snapshot, lifetime);
                Report(new("save", "passed"));
            }
            Complete(true, (SaveAccount ? "Your account was added with the selected folders. "
                : "Connection and sign-in passed. Folder choices are in your form; save when you are ready. ") + readingSummary + " You can close this window.");
        }
        catch (OperationCanceledException)
        {
            Cancelled = _stop.IsCancellationRequested || lifetime.IsCancellationRequested;
            Complete(false, saving ? "The save result could not be confirmed. Reload your accounts before trying again."
                : Cancelled ? "The check was cancelled. No settings were changed."
                : "The connection check timed out. No settings were changed. Check the connection and try again.");
        }
        catch (Exception exception)
        {
            var failure = exception is EngineException ? exception.Message : "The account check could not finish. No success is assumed.";
            if (saving && committed is null) failure += " Reload your accounts before trying to save again.";
            Complete(committed is not null, committed is not null ? "Your account was saved. Reload your accounts to update the form." : failure);
        }
        finally { _snapshot.ClearPasswords(); }
    }

    /// <summary>Release owned resources and sensitive operation state.</summary>
    public void Dispose()
    {
        _snapshot.ClearPasswords();
        _stop.Dispose();
    }
}

/// <summary>One engine-reported check stage, separate from its rendered status label.</summary>
/// <param name="key">Engine progress-stage key, independent of its visible label.</param>
/// <param name="label">Visible field or operation-stage label.</param>
/// <param name="status">Initial stage presentation, defaulting to Waiting.</param>
internal sealed class CheckStageModel(string key, string label, string status = "Waiting") : ObservableModel
{
    private string _status = status;
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Status { get => _status; set { if (Set(ref _status, value)) Notify(); } }
    public string StatusGlyph => Status switch { "Passed" => "✓", "Failed" => "✕", "Empty folder" or "Cancelled" => "!", _ => "" };
    public int StatusTone => Status switch { "Passed" => 1, "Failed" => 3, "Empty folder" or "Cancelled" => 2, _ => 0 };
    public bool ShowStatusText => StatusGlyph.Length == 0;
    public string StatusDescription => Label + ": " + Status;
}
