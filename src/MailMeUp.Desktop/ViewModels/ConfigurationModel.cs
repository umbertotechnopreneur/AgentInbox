/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Saved snapshots, draft selection, operation lifetime and notifications.
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

/// <summary>Owns account selection, edit lifecycle and operation state for Windows configuration.</summary>
internal sealed class ConfigurationModel : ObservableModel
{
    private readonly PythonEngine _engine;
    private SavedAccount? _selectedAccount;
    private bool _isBusy;
    private bool _accountsLoaded;
    private bool _creatingAccount;
    private string _previousAccountId = "";
    private string _page = "accounts";
    private int _editorTab;
    private bool _updatingAccounts;

    /// <summary>Initialize the owned model or native view and its explicit dependencies.</summary>
    /// <param name="engine">Shared adapter for the local Python management contract.</param>
    /// <param name="microsoftClientId">Public-client ID loaded from shipped desktop settings.</param>
    internal ConfigurationModel(PythonEngine engine, string microsoftClientId)
    {
        _engine = engine;
        Editor = new AccountEditorModel(microsoftClientId);
        Editor.PropertyChanged += (_, _) => Notify();
        Accounts.CollectionChanged += (_, _) =>
        {
            foreach (var row in AccountItems.Where(row => !Accounts.Contains(row.Account)).ToArray()) AccountItems.Remove(row);
            foreach (var account in Accounts.Where(account => !AccountItems.Any(row => ReferenceEquals(row.Account, account))))
                AccountItems.Add(new(account));
            Notify();
        };
        SignIn = new MicrosoftSignInModel(engine);
        Assistant = new AssistantConnectionModel(engine, Notice);
        Assistant.PropertyChanged += (_, _) => Notify();
    }

    public AccountEditorModel Editor { get; }
    public MicrosoftSignInModel SignIn { get; }
    public AssistantConnectionModel Assistant { get; }
    public ObservableCollection<SavedAccount> Accounts { get; } = [];
    public ObservableCollection<AccountListItemModel> AccountItems { get; } = [];
    public AccountListItemModel? SelectedAccountItem
    {
        get => AccountItems.FirstOrDefault(row => ReferenceEquals(row.Account, SelectedAccount));
        set { if (!_updatingAccounts) SelectedAccount = value?.Account; }
    }
    public bool CanTestAll => IsIdle && Accounts.Count > 0;
    public NotificationModel Notice { get; } = new();
    public NotificationModel Cleanup { get; } = new();
    public bool IsBusy { get => _isBusy; set { if (Set(ref _isBusy, value)) Notify(); } }
    public bool IsIdle => !IsBusy;
    public bool AccountsLoaded => _accountsLoaded;
    public string Page { get => _page; set { if (Set(ref _page, value)) Notify(); } }
    public int EditorTab { get => _editorTab; set => Set(ref _editorTab, value); }
    public bool ShowSaveBar => Page == "accounts" && EditingAccount;
    public bool CreatingAccount => _creatingAccount;
    public bool EditingAccount => AccountsLoaded && (CreatingAccount || Editor.HasSavedAccount);
    public bool ShowEmptyAccounts => AccountsLoaded && Accounts.Count == 0 && !CreatingAccount;
    public bool ShowWorkspace => AccountsLoaded && (Accounts.Count > 0 || CreatingAccount);
    public bool ShowAccountList => AccountsLoaded && Accounts.Count > 0 && !CreatingAccount;
    public bool ShowSelectionPrompt => ShowAccountList && !EditingAccount;
    public bool CanRemoveAccount => IsIdle && Editor.HasSavedAccount;
    public bool CanSave => IsIdle && (CreatingAccount || Editor.HasChanges);
    public bool CanConnectAssistant => IsIdle && Assistant.CanConnect;
    public bool CanTestAssistant => IsIdle && Assistant.IsConfigured;
    public bool CanCheckReading => IsIdle && Editor.CanCheckReading;
    public bool CanCheckSending => IsIdle && Editor.ConfigureSending;
    public bool CanChooseAccountFolders => IsIdle && Editor.CanSelectFolders;
    /// <summary>Create a check model from an explicit validated account snapshot.</summary>
    /// <param name="snapshot">Validated account settings, transient passwords and expected revision for this operation.</param>
    /// <param name="saveAccount">Whether to save the checked account after explicit folder selection.</param>
    /// <param name="smtpOnly">Whether to check only SMTP without requiring incoming credentials.</param>
    /// <param name="savedCheck">Whether this is a saved-account check that skips editing and folder-choice steps.</param>
    internal AccountCheckModel CreateCheck(AccountSnapshot snapshot, bool saveAccount, bool smtpOnly, bool savedCheck = false) =>
        new(_engine, this, snapshot, saveAccount, smtpOnly, savedCheck);
    public string SaveText => CreatingAccount ? "Create account" : "Save changes";
    public string SaveStatus => CreatingAccount ? "New connection" : Editor.HasChanges ? "Unsaved changes" : "All changes saved";
    public string SaveHint => CreatingAccount ? "Check this account and choose folders. For Microsoft, save the settings and sign in."
        : "Save this account's settings and sign-in details.";
    public SavedAccount? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (_updatingAccounts) return;
            if (ReferenceEquals(_selectedAccount, value)) return;
            _selectedAccount = value;
            _creatingAccount = false;
            Editor.Load(value);
            EditorTab = 0;
            if (!IsBusy) SignIn.Clear();
            Notify();
        }
    }

    /// <summary>Start an explicit new draft and focus its connection name.</summary>
    internal void StartNewAccount()
    {
        if (IsBusy) return;
        _previousAccountId = Editor.Id;
        _selectedAccount = null;
        Editor.Load(null);
        _creatingAccount = true;
        EditorTab = 0;
        SignIn.Clear();
        Notify();
    }

    /// <summary>Discard the new-account draft and restore selection focus.</summary>
    internal void CancelNewAccount()
    {
        if (IsBusy) return;
        _creatingAccount = false;
        _selectedAccount = Accounts.FirstOrDefault(account => account.Id == _previousAccountId);
        Editor.Load(_selectedAccount);
        Notify();
    }

    /// <summary>Reload saved-account snapshots within the configuration window lifetime.</summary>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    internal async Task LoadAccountsAsync(CancellationToken cancellationToken)
    {
        var selected = Editor.Id;
        var presetsResult = await _engine.ManageAsync("presets", new { }, cancellationToken);
        var presets = presetsResult.Deserialize<Dictionary<string, ProviderPreset>>()
            ?? throw new EngineException("The engine returned no email providers.");
        Editor.SetPresets(presets);
        var result = await _engine.ManageAsync("list_accounts", new { }, cancellationToken);
        var accounts = result.EnumerateArray().Select(SavedAccount.Read).ToArray();
        _updatingAccounts = true;
        try
        {
            Accounts.Clear();
            foreach (var account in accounts) Accounts.Add(account);
            _accountsLoaded = true;
            _creatingAccount = false;
            _selectedAccount = Accounts.FirstOrDefault(account => account.Id == selected);
            Editor.Load(_selectedAccount);
        }
        finally { _updatingAccounts = false; }
        Notify();
    }

    /// <summary>Commit the validated account snapshot through the Python settings operation.</summary>
    /// <param name="snapshot">Validated account settings, transient passwords and expected revision for this operation.</param>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    internal async Task<JsonElement> SaveAsync(AccountSnapshot snapshot, CancellationToken cancellationToken)
    {
        var saved = await _engine.ManageAsync("save_account", new
        {
            account = snapshot.Account, passwords = snapshot.Passwords, expected_revision = snapshot.Revision
        }, cancellationToken);
        try
        {
            ApplySavedAccount(saved);
            Notice.Show("Account saved", "Your assistant can use only the access you enabled.", 1);
        }
        catch (Exception)
        {
            Notice.Show("Account saved", "Your account is saved. Reload the accounts to update this page.", 2);
        }
        try { ApplyCleanup(saved); }
        catch (Exception)
        {
            Cleanup.Show("Local cleanup is pending", "The account is saved, but cleanup status could not be read. Use Retry cleanup.", 2);
        }
        return saved;
    }

    /// <summary>Apply a committed account result to saved snapshots and the current draft.</summary>
    /// <param name="saved">Saved endpoint or committed account result used to refresh model state.</param>
    internal void ApplySavedAccount(JsonElement saved)
    {
        var account = SavedAccount.Read(saved);
        _updatingAccounts = true;
        try
        {
            var previous = Accounts.FirstOrDefault(item => item.Id == account.Id);
            if (previous is not null) Accounts.Remove(previous);
            Accounts.Add(account);
            _creatingAccount = false;
            _selectedAccount = account;
            Editor.Load(account);
        }
        finally { _updatingAccounts = false; }
        Notify();
    }

    /// <summary>Confirm removal of the selected local account before revoking its settings.</summary>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    internal async Task RemoveAccountAsync(CancellationToken cancellationToken)
    {
        RequireSavedAccount();
        var identity = Editor.Id;
        var removed = await _engine.ManageAsync("remove_account", new { account_id = identity }, cancellationToken);
        var account = Accounts.First(item => item.Id == identity);
        Accounts.Remove(account);
        _selectedAccount = null;
        Editor.Load(null);
        Notice.Show("Account removed", "The local connection is no longer available to your assistant.", 1);
        ApplyCleanup(removed);
        Notify();
    }

    /// <summary>Reject operations that require an existing saved account.</summary>
    internal void RequireSavedAccount()
    {
        if (!Editor.HasSavedAccount) throw new EngineException("Save an account before using this action.");
    }

    /// <summary>Check one sample read through the shared application using saved settings.</summary>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    internal async Task TestReadingAsync(CancellationToken cancellationToken)
    {
        RequireSavedAccount();
        var protocol = SelectedAccount!.Incoming.Protocol!.ToUpperInvariant();
        var result = await _engine.ManageAsync("check_read", new { account_id = Editor.Id }, cancellationToken);
        var sampleRead = result.GetProperty("message_read").GetString() == "passed";
        var message = result.TryGetProperty("message", out var detail) ? detail.GetString() ?? "The operation completed." : "The operation completed.";
        Notice.Show(sampleRead ? $"{protocol} reading succeeded" : $"{protocol} connection succeeded", message, sampleRead ? 1 : 0);
    }

    /// <summary>Retry retired-credential cleanup separately from the completed save or sign-in.</summary>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    internal async Task RetryCleanupAsync(CancellationToken cancellationToken)
    {
        var result = await _engine.ManageAsync("retry_cleanup", new { }, cancellationToken);
        ApplyCleanup(result, clearWhenComplete: true);
        if (!result.GetProperty("cleanup_pending").GetBoolean())
            Notice.Show("Local cleanup completed", "The engine reports no remaining queued cleanup.", 1);
    }

    /// <summary>Run the model operation and present its outcome and pending cleanup separately.</summary>
    /// <param name="operation">Explicit operation name or asynchronous action forwarded to the shared model.</param>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    internal async Task RunAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        if (IsBusy || cancellationToken.IsCancellationRequested) return;
        IsBusy = true;
        Notice.IsOpen = false;
        try { await operation(); }
        catch (EngineException exception) { Notice.Show("Could not complete this action", exception.Message, 3); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Notice.Show("The operation timed out", "Check the connection settings. If you were saving changes, reload the accounts to check whether they were saved.", 2);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            // Log code locations only; exception text can contain private data.
            var locations = new System.Diagnostics.StackTrace(exception, false).GetFrames()
                .Select(frame => frame.GetMethod()).Where(method => method?.DeclaringType?.Namespace?.StartsWith("MailMeUp.Desktop", StringComparison.Ordinal) == true)
                .Take(8).Select(method => $"{method!.DeclaringType!.Name}.{method.Name}");
            Console.Error.WriteLine($"Setup error: {exception.GetType().Name} (0x{exception.HResult:X8}); {string.Join("; ", locations)}");
            Notice.Show("Could not complete this action", $"AgentInbox encountered an unexpected error ({exception.GetType().Name}, 0x{exception.HResult:X8}). No automatic retry was made.", 3);
        }
        finally { IsBusy = false; }
    }

    /// <summary>Display cleanup metadata without undoing a committed operation.</summary>
    /// <param name="result">Committed engine result containing cleanup metadata independent of its primary outcome.</param>
    /// <param name="clearWhenComplete">Whether a completed cleanup should dismiss an earlier cleanup notice.</param>
    internal void ApplyCleanup(JsonElement result, bool clearWhenComplete = false)
    {
        if (!result.TryGetProperty("cleanup_pending", out var pending)) return;
        if (!pending.GetBoolean())
        {
            if (clearWhenComplete) Cleanup.IsOpen = false;
            return;
        }
        var message = "Protected local data is still waiting to be removed. Retry cleanup when secure storage is available.";
        if (result.TryGetProperty("cleanup_error", out var error) && error.ValueKind == JsonValueKind.String
            && error.GetString() is { Length: > 0 and <= 2048 } detail) message += " " + detail;
        Cleanup.Show("Local cleanup is pending", message, 2);
    }

    /// <summary>Run the explicit Microsoft sign-in workflow for the current account.</summary>
    /// <param name="showDialog">View adapter that presents sign-in state without owning account policy.</param>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    internal async Task SignInMicrosoftAsync(Func<MicrosoftSignInModel, CancellationToken, Task> showDialog, CancellationToken cancellationToken)
    {
        SignIn.Clear();
        try
        {
            SignIn.StartLog();
            if (!Editor.IsMicrosoft) throw new EngineException("Choose Sign in with Microsoft under How to connect.");
            if (CreatingAccount || !Editor.HasSavedAccount || Editor.HasChanges)
            {
                SignIn.Append("Saving the account settings before sign-in.");
                var snapshot = Editor.Snapshot();
                try { await SaveAsync(snapshot, cancellationToken); }
                finally { snapshot.ClearPasswords(); }
                SignIn.Append("Account settings saved.");
            }
            await SignIn.RunAsync(Editor.Id, showDialog, cancellationToken);
            if (SignIn.CleanupResult is { } cleanup) ApplyCleanup(cleanup);
        }
        catch (EngineException exception) { SignIn.Append(exception.Message); throw; }
        catch (OperationCanceledException) { SignIn.Append("Sign-in was cancelled or took too long."); throw; }
        finally
        {
            SignIn.StopLog();
            if (SignIn.CleanupPending) Cleanup.Show("Local cleanup is pending", "Temporary sign-in data may still be on this computer. Use Retry cleanup.", 2);
        }
        if (!SignIn.Connected)
        {
            Notice.Show("Sign-in cancelled", "You can sign in with Microsoft again when you're ready.", 0);
            return;
        }
        try { await LoadAccountsAsync(cancellationToken); }
        catch (Exception)
        {
            SignIn.Append("Sign-in is saved, but the account list could not refresh. Reload the accounts.");
            Notice.Show("Signed in with Microsoft", "Sign-in is saved. Reload the accounts to update this page.", 2);
            return;
        }
        Notice.Show("Signed in with Microsoft", "You can now check reading and sending in the account settings.", 1);
    }
}

/// <summary>A notification's state, independent of InfoBar or any other rendering control.</summary>
internal sealed class NotificationModel : ObservableModel
{
    private string _title = "";
    private string _message = "";
    private int _severity;
    private bool _isOpen;
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
    public int Severity { get => _severity; set => Set(ref _severity, value); }
    public bool IsOpen { get => _isOpen; set => Set(ref _isOpen, value); }
    /// <summary>Publish a notification title, message and severity to observers.</summary>
    /// <param name="title">User-facing heading for this page, dialog or notification.</param>
    /// <param name="message">Safe user-facing outcome or guidance; never raw credentials or mail content.</param>
    /// <param name="severity">Notification severity used for presentation.</param>
    internal void Show(string title, string message, int severity)
    {
        Title = title; Message = message; Severity = severity; IsOpen = true;
    }
}
