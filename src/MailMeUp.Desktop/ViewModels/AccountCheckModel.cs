// SPDX-License-Identifier: MIT

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

    internal void SelectFolders(IEnumerable<string> folders)
    {
        if (UpdatingFolders || !ShowPrimary) return;
        SelectedFolders.Clear();
        foreach (var folder in folders) SelectedFolders.Add(folder);
        Notify();
    }

    internal void ChooseFolders()
    {
        if (!CanChooseFolders) return;
        var folders = SelectedFolders.ToArray();
        _phase = SaveAccount && !ReadMessageRequested ? CheckPhase.Saving : CheckPhase.Reading;
        Notify();
        _selection.TrySetResult(folders);
    }

    internal void RequestClose()
    {
        _closeWhenReady = true;
        if (!IsSaving) _stop.Cancel();
        Notify();
    }

    internal void Closed()
    {
        _closed = true;
        _selection.TrySetResult(null);
        if (!IsSaving) _stop.Cancel();
    }

    internal void Report(AccountCheckProgress progress)
    {
        var stage = Stages.FirstOrDefault(item => item.Key == progress.Stage);
        if (_closed || !IsWorking || stage is null) return;
        stage.Status = progress.State switch { "passed" => "Passed", "empty" => "Empty folder", _ => "Checking…" };
        _activeStage = progress.State == "started" ? progress.Stage : null;
    }

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

    private void Complete(bool succeeded, string message)
    {
        Succeeded = succeeded;
        if (!succeeded && _activeStage is not null)
        {
            var stage = Stages.First(item => item.Key == _activeStage);
            stage.Status = "Failed";
        }
        _phase = CheckPhase.Finished;
        _heading = SmtpOnly ? succeeded ? "Sending check complete" : "Sending check stopped"
            : succeeded ? SaveAccount ? "Account added" : "Account check complete" : "Account check stopped";
        _message = message;
        Notify();
    }

    /// <summary>Performs engine calls and records their outcome without reading any UI control.</summary>
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
                : "The check was cancelled or timed out. No account was saved.");
        }
        catch (Exception exception)
        {
            var failure = exception is EngineException ? exception.Message : "The account check could not finish. No success is assumed.";
            if (saving && committed is null) failure += " Reload your accounts before trying to save again.";
            Complete(committed is not null, committed is not null ? "Your account was saved. Reload your accounts to update the form." : failure);
        }
        finally { _snapshot.ClearPasswords(); }
    }

    public void Dispose()
    {
        _snapshot.ClearPasswords();
        _stop.Dispose();
    }
}

/// <summary>One engine-reported check stage, separate from its rendered status label.</summary>
internal sealed class CheckStageModel(string key, string label, string status = "Waiting") : ObservableModel
{
    private string _status = status;
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Status { get => _status; set => Set(ref _status, value); }
}
