// SPDX-License-Identifier: MIT

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

    internal ConfigurationModel(PythonEngine engine, string microsoftClientId)
    {
        _engine = engine;
        Editor = new AccountEditorModel(microsoftClientId);
        Editor.PropertyChanged += (_, _) => Notify();
        Accounts.CollectionChanged += (_, _) => Notify();
        SignIn = new MicrosoftSignInModel(engine);
        Assistant = new AssistantConnectionModel(engine, Notice);
        Assistant.PropertyChanged += (_, _) => Notify();
    }

    public AccountEditorModel Editor { get; }
    public MicrosoftSignInModel SignIn { get; }
    public AssistantConnectionModel Assistant { get; }
    public ObservableCollection<SavedAccount> Accounts { get; } = [];
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
    internal AccountCheckModel CreateCheck(AccountSnapshot snapshot, bool saveAccount, bool smtpOnly) =>
        new(_engine, this, snapshot, saveAccount, smtpOnly);
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

    internal void CancelNewAccount()
    {
        if (IsBusy) return;
        _creatingAccount = false;
        _selectedAccount = Accounts.FirstOrDefault(account => account.Id == _previousAccountId);
        Editor.Load(_selectedAccount);
        Notify();
    }

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

    internal void RequireSavedAccount()
    {
        if (!Editor.HasSavedAccount) throw new EngineException("Save an account before using this action.");
    }

    internal async Task TestReadingAsync(CancellationToken cancellationToken)
    {
        RequireSavedAccount();
        var protocol = SelectedAccount!.Incoming.Protocol!.ToUpperInvariant();
        var result = await _engine.ManageAsync("check_read", new { account_id = Editor.Id }, cancellationToken);
        var sampleRead = result.GetProperty("message_read").GetString() == "passed";
        var message = result.TryGetProperty("message", out var detail) ? detail.GetString() ?? "The operation completed." : "The operation completed.";
        Notice.Show(sampleRead ? $"{protocol} reading succeeded" : $"{protocol} connection succeeded", message, sampleRead ? 1 : 0);
    }

    internal async Task RetryCleanupAsync(CancellationToken cancellationToken)
    {
        var result = await _engine.ManageAsync("retry_cleanup", new { }, cancellationToken);
        ApplyCleanup(result, clearWhenComplete: true);
        if (!result.GetProperty("cleanup_pending").GetBoolean())
            Notice.Show("Local cleanup completed", "The engine reports no remaining queued cleanup.", 1);
    }

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
    internal void Show(string title, string message, int severity)
    {
        Title = title; Message = message; Severity = severity; IsOpen = true;
    }
}
