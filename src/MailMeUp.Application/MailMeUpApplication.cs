using System.Collections.Concurrent;
using System.Security.Cryptography;
using MailMeUp.Core;

namespace MailMeUp.Application;

/// <summary>Coordinates account lifecycle and compact read-only operations across providers.</summary>
public sealed class MailMeUpApplication : IMailMeUpApplication
{
    private const int MaximumProviderPagesPerRefill = 20;
    private const int MaximumProviderCursorHistory = 512;
    private const int MaximumConcurrentMailAccountReads = 4;
    private const int MaximumConcurrentConnectionChecks = 4;
    private static readonly TimeSpan ProviderReadTimeout = TimeSpan.FromSeconds(30);
    private readonly IAccountStore _accounts;
    private readonly IAccountSharingStore _sharing;
    private readonly IMailSearchPreferencesStore _mailSearchPreferences;
    private readonly TimeProvider _timeProvider;
    private readonly IReadBudget _readBudget;
    private readonly IReadGuardrailManagement? _readGuardrailManagement;
    private readonly BoundedReadCache<ProviderMailMessage> _mailDetails;
    private readonly BoundedReadCache<ProviderEvent> _eventDetails;
    private readonly IReadOnlyList<ProviderDescriptor> _providers;
    private readonly IReadOnlyDictionary<string, IProviderSetupService> _providerSetupServices;
    private readonly IReadOnlyDictionary<string, IAccountConnector> _accountConnectors;
    private readonly IReadOnlyDictionary<string, IMailReader> _mailReaders;
    private readonly IReadOnlyDictionary<string, ICalendarReader> _calendarReaders;
    private readonly IReadOnlyDictionary<string, IAccountConnectionChecker> _connectionCheckers;
    private readonly LocalReferenceStore _references = new();

    /// <summary>Creates the complete application boundary used by the local CLI and MCP host.</summary>
    public MailMeUpApplication(
        IAccountStore accounts,
        IEnumerable<IProviderModule> providers,
        IEnumerable<IProviderSetupService> providerSetupServices,
        IEnumerable<IAccountConnector> accountConnectors,
        IEnumerable<IMailReader> mailReaders,
        IEnumerable<ICalendarReader> calendarReaders,
        IAccountSharingStore? sharingStore = null,
        IEnumerable<IAccountConnectionChecker>? connectionCheckers = null,
        IMailSearchPreferencesStore? mailSearchPreferencesStore = null,
        TimeProvider? timeProvider = null,
        IReadBudget? readBudget = null,
        IReadGuardrailManagement? readGuardrailManagement = null)
    {
        _accounts = accounts;
        _sharing = sharingStore ?? new MemoryAccountSharingStore();
        _mailSearchPreferences = mailSearchPreferencesStore ?? new MemoryMailSearchPreferencesStore();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _readBudget = readBudget ?? new InMemoryReadGuardrails();
        _readGuardrailManagement = readGuardrailManagement ?? _readBudget as IReadGuardrailManagement;
        _mailDetails = new(_timeProvider, ProviderContentSize.Mail);
        _eventDetails = new(_timeProvider, ProviderContentSize.Event);
        _providerSetupServices = providerSetupServices.ToDictionary(service => service.ProviderId, StringComparer.Ordinal);
        _accountConnectors = accountConnectors.ToDictionary(connector => connector.ProviderId, StringComparer.Ordinal);
        _mailReaders = mailReaders.ToDictionary(reader => reader.ProviderId, StringComparer.Ordinal);
        _calendarReaders = calendarReaders.ToDictionary(reader => reader.ProviderId, StringComparer.Ordinal);
        _connectionCheckers = (connectionCheckers ?? []).ToDictionary(checker => checker.ProviderId, StringComparer.Ordinal);
        _providers = providers
            .Select(provider => provider.Descriptor with
            {
                AuthenticationAvailable = _accountConnectors.ContainsKey(provider.Descriptor.Id),
                MailReadAvailable = _mailReaders.ContainsKey(provider.Descriptor.Id),
                CalendarReadAvailable = _calendarReaders.ContainsKey(provider.Descriptor.Id)
            })
            .OrderBy(provider => provider.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Creates the foundation application without setup, account connection or provider-read services.</summary>
    public MailMeUpApplication(IAccountStore accounts, IEnumerable<IProviderModule> providers)
        : this(accounts, providers, [], [], [], [])
    {
    }

    /// <inheritdoc />
    public ApplicationStatus GetStatus() => new(
        _calendarReaders.Count > 0 && _mailReaders.Count > 0
            ? "read_only_mvp"
            : _mailReaders.Count > 0
                ? "mail_read"
                : _accountConnectors.Count > 0 ? "account_setup" : "foundation",
        "stdio",
        true,
        _accountConnectors.Count > 0,
        _providers);

    /// <inheritdoc />
    public Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken cancellationToken = default) =>
        _accounts.ListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Account>> ListSharedAccountsAsync(CancellationToken cancellationToken = default)
    {
        var sharing = await ReadSharingSnapshotAsync(cancellationToken);
        await EnsureSharingUnchangedAsync(sharing, cancellationToken);
        return sharing.Accounts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountSharingSettings>> ListAccountSharingAsync(CancellationToken cancellationToken = default) =>
        (await ReadSharingSnapshotAsync(cancellationToken)).Settings.Values.ToArray();

    /// <inheritdoc />
    public async Task<AccountSharingSettings> SaveAccountSharingAsync(AccountSharingSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (!(await _accounts.ListAsync(cancellationToken)).Any(account => string.Equals(account.Id, settings.AccountId, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The account is no longer connected.", nameof(settings));
        }

        var normalized = settings with { CalendarIds = settings.CalendarIds?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
        await _sharing.SaveAsync(normalized, cancellationToken);
        return normalized;
    }

    /// <inheritdoc />
    public async Task<MailSearchPreferences> GetMailSearchPreferencesAsync(CancellationToken cancellationToken = default)
    {
        var preferences = await _mailSearchPreferences.GetAsync(cancellationToken);
        preferences.Validate();
        return preferences;
    }

    /// <inheritdoc />
    public async Task<MailSearchPreferences> SaveMailSearchPreferencesAsync(
        MailSearchPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        await _mailSearchPreferences.SaveAsync(preferences, cancellationToken);
        return preferences;
    }

    /// <inheritdoc />
    public async Task<ReadGuardrailStatus?> GetReadGuardrailStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _readGuardrailManagement is null
            ? null
            : await _readGuardrailManagement.GetStatusAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<ReadGuardrailStatus> SaveReadGuardrailLimitsAsync(
        ReadGuardrailLimits limits,
        ReadGuardrailLimits expectedLimits,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(expectedLimits);
        limits.Validate();
        expectedLimits.Validate();
        return (_readGuardrailManagement
            ?? throw new NotSupportedException("Read guardrail settings are unavailable in this host."))
            .SaveLimitsAsync(limits, expectedLimits, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderCalendar>> ListAvailableCalendarsAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var account = (await _accounts.ListAsync(cancellationToken)).SingleOrDefault(item => string.Equals(item.Id, accountId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The account is no longer connected.", nameof(accountId));
        if (!account.CalendarReadEnabled || !_calendarReaders.TryGetValue(account.Provider, out var reader))
        {
            throw new InvalidOperationException("Calendar discovery requires calendar consent for this account.");
        }

        // This operation is reserved for the owner's local setup UI and issues no assistant references.
        try
        {
            return await ReadWithDeadlineAsync(token => reader.ListCalendarsAsync(account, token), cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderReadException("Calendar discovery timed out. Try loading calendars again.");
        }
    }

    /// <inheritdoc />
    public async Task<AccountConnectionCheckResult> CheckConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await _accounts.ListAsync(cancellationToken);
        using var gate = new SemaphoreSlim(MaximumConcurrentConnectionChecks);
        var checks = await Task.WhenAll(accounts.Select(account => CheckConnectionAsync(account, gate, cancellationToken)));
        return new AccountConnectionCheckResult(checks.OrderBy(check => check.AccountId, StringComparer.Ordinal).ToArray());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderSetupStatus>> ListProviderSetupAsync(CancellationToken cancellationToken = default)
    {
        var statuses = new List<ProviderSetupStatus>(_providerSetupServices.Count);
        foreach (var service in _providerSetupServices.Values.OrderBy(item => item.ProviderId, StringComparer.Ordinal))
        {
            statuses.Add(await service.GetStatusAsync(cancellationToken));
        }

        return statuses;
    }

    /// <inheritdoc />
    public Task<ProviderSetupResult> ConfigureProviderAsync(string providerId, string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!_providerSetupServices.TryGetValue(providerId, out var service))
        {
            throw new ArgumentException("Unknown provider. Run mailmeup setup status to list supported providers.", nameof(providerId));
        }

        return service.ConfigureAsync(source, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccountConnectionResult> ConnectAccountAsync(
        string providerId,
        AccountConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!_accountConnectors.TryGetValue(providerId, out var connector))
        {
            throw new ArgumentException("Unknown account provider. Run mailmeup setup status to list supported providers.", nameof(providerId));
        }

        var existingAccounts = await _accounts.ListAsync(cancellationToken);
        var result = await connector.ConnectAsync(options, cancellationToken);
        try
        {
            if (!options.ShareWithAssistant && !existingAccounts.Any(account => string.Equals(account.Id, result.Account.Id, StringComparison.Ordinal)))
            {
                // Persist the denial before publishing account metadata to another running MCP process.
                await _sharing.SaveAsync(new AccountSharingSettings(result.Account.Id, false, false, false, []), cancellationToken);
            }

            await _accounts.SaveAsync(result.Account, cancellationToken);
            return result;
        }
        catch
        {
            // A reconnect must not delete an existing identity's credential if saving metadata fails.
            if (existingAccounts.Any(account => string.Equals(account.Id, result.Account.Id, StringComparison.Ordinal)))
            {
                throw;
            }

            try
            {
                await connector.DisconnectAsync(result.Account, CancellationToken.None);
            }
            catch (Exception)
            {
                // Preserve the metadata failure; later setup can overwrite the unreachable protected credential.
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<AccountRemovalResult> RemoveAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var account = (await _accounts.ListAsync(cancellationToken))
            .SingleOrDefault(item => string.Equals(item.Id, accountId, StringComparison.Ordinal));
        if (account is null)
        {
            return new AccountRemovalResult(accountId, Removed: false);
        }

        if (!_accountConnectors.TryGetValue(account.Provider, out var connector))
        {
            throw new InvalidOperationException("The account provider is not available in this build.");
        }

        await connector.DisconnectAsync(account, cancellationToken);
        // Keep a denial tombstone so a later reconnect cannot silently restore the previous assistant access.
        await _sharing.SaveAsync(new AccountSharingSettings(account.Id, false, false, false, []), cancellationToken);
        var removed = await _accounts.DeleteAsync(account.Id, cancellationToken);
        return new AccountRemovalResult(account.Id, removed);
    }

    /// <inheritdoc />
    public async Task<MailSearchResult> SearchMailAsync(MailSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query!.Trim();
        if (query is not null && query.Length > 500)
        {
            throw new ArgumentException("Mail search text must contain at most 500 characters.", nameof(request));
        }

        if (request.Limit is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Mail search limit must be between 1 and 50.");
        }

        var sender = NormalizeOptionalContains(request.Sender, nameof(request.Sender), "sender");
        var recipient = NormalizeOptionalContains(request.RecipientContains, nameof(request.RecipientContains), "recipient");

        var start = ParseOptionalExplicitDateTime(request.Start, nameof(request.Start));
        var end = ParseOptionalExplicitDateTime(request.End, nameof(request.End));
        if (start is not null && end is not null && end.Value <= start.Value)
        {
            throw new ArgumentException("The mail end time must be later than the start time.", nameof(request));
        }

        var hasStructuredFilter = sender is not null || recipient is not null || start is not null || end is not null ||
                                   request.UnreadOnly || request.HasAttachments is not null || request.InboxOnly;
        if (query is null && !hasStructuredFilter)
        {
            throw new ArgumentException("Mail search requires text or at least one structured filter.", nameof(request));
        }

        var requestedQuery = new ProviderMailQuery(
            query ?? string.Empty,
            sender,
            start,
            end,
            recipient,
            request.UnreadOnly,
            request.HasAttachments,
            request.InboxOnly);

        var sharing = await ReadSharingSnapshotAsync(cancellationToken);
        var allAccounts = sharing.Accounts;
        var usesDefaultPeriod = start is null && end is null && !MailSearchDateSyntax.HasExplicitDate(query);
        var preferences = usesDefaultPeriod ? await GetMailSearchPreferencesAsync(cancellationToken) : null;
        MailCursorState state;
        IReadOnlyList<Account> selectedAccounts;
        if (string.IsNullOrWhiteSpace(request.Cursor))
        {
            selectedAccounts = SelectMailAccounts(allAccounts, request.AccountIds);
            var providerQuery = requestedQuery;
            if (preferences is not null)
            {
                // Freeze both boundaries for every page of this search, including future continuations.
                var searchTime = _timeProvider.GetUtcNow();
                providerQuery = requestedQuery with
                {
                    Start = searchTime.AddDays(-preferences.DefaultLookbackDays),
                    End = searchTime
                };
            }

            state = new MailCursorState(
                requestedQuery,
                providerQuery,
                preferences?.DefaultLookbackDays,
                selectedAccounts.Select(account => account.Id).ToArray(),
                selectedAccounts.ToDictionary(
                    account => account.Id,
                    _ => new AccountMailCursorState(),
                    StringComparer.Ordinal));
        }
        else
        {
            if (request.Cursor.Length > 128)
            {
                throw new ArgumentException("The mail cursor is invalid.", nameof(request));
            }

            state = _references.Get<MailCursorState>(request.Cursor, "c_").Copy();
            if (state.RequestedQuery != requestedQuery)
            {
                throw new ArgumentException("The mail cursor belongs to a different query.", nameof(request));
            }

            if (state.DefaultLookbackDaysApplied != preferences?.DefaultLookbackDays)
            {
                throw new ArgumentException("The default mail search period changed. Start a new search.", nameof(request));
            }

            if (request.AccountIds is { Count: > 0 } &&
                !state.AccountIds.ToHashSet(StringComparer.Ordinal).SetEquals(request.AccountIds))
            {
                throw new ArgumentException("The mail cursor belongs to a different account scope.", nameof(request));
            }

            // Removed or downgraded accounts are reported below without hiding healthy cursor results.
            selectedAccounts = allAccounts.Where(account => state.AccountIds.Contains(account.Id, StringComparer.Ordinal)).ToArray();
            foreach (var accountState in state.Accounts.Values.Where(account => account.FailureKind == ReadFailureKind.BudgetExceeded))
            {
                // A local admission pause is resumable after its window resets; provider failures stay stopped.
                accountState.Failure = null;
                accountState.FailureKind = ReadFailureKind.Unknown;
            }
        }

        var accountsById = selectedAccounts.ToDictionary(account => account.Id, StringComparer.Ordinal);
        if (accountsById.Count > 0)
            await _readBudget.AdmitReadAsync(detail: false, cancellationToken);
        using var accountReadGate = new SemaphoreSlim(MaximumConcurrentMailAccountReads);
        using var workDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        workDeadline.CancelAfter(ProviderReadTimeout);
        var pagesRead = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var providerPageSize = Math.Min(request.Limit, 10);
        await Task.WhenAll(state.Accounts.Select(pair => ReadMailAccountAsync(
            pair.Key,
            pair.Value,
            accountsById,
            state.Query,
            providerPageSize,
            accountReadGate,
            pagesRead,
            workDeadline.Token,
            cancellationToken)));

        var candidates = new List<MailCandidate>(request.Limit);
        while (candidates.Count < request.Limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = state.Accounts
                .SelectMany(pair => pair.Value.Items.Select(item => new MailCandidate(pair.Key, item)))
                .OrderByDescending(item => item.Item.ReceivedAt)
                .ThenBy(item => item.AccountId, StringComparer.Ordinal)
                .ThenBy(item => item.Item.ProviderMessageId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (candidate is null) break;
            state.Accounts[candidate.AccountId].Items.Remove(candidate.Item);
            candidates.Add(candidate);
            if (candidates.Count == request.Limit) break;

            // Only an exhausted source can hide the next newest item; keep other prefetched buffers intact.
            await Task.WhenAll(state.Accounts
                .Where(pair => pair.Value.Items.Count == 0 && pair.Value.Failure is null &&
                    (!pair.Value.Started || pair.Value.NextProviderCursor is not null))
                .Select(pair => ReadMailAccountAsync(
                    pair.Key, pair.Value, accountsById, state.Query, providerPageSize,
                    accountReadGate, pagesRead, workDeadline.Token, cancellationToken)));
        }

        await EnsureSharingUnchangedAsync(sharing, cancellationToken);
        if (preferences is not null && await GetMailSearchPreferencesAsync(cancellationToken) != preferences)
        {
            throw new InvalidOperationException("The default mail search period changed. Start a new search.");
        }

        var items = new List<MailSearchItem>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var reference = _references.Put(
                "m_",
                new MailItemAddress(candidate.AccountId, candidate.Item.ProviderMessageId));
            items.Add(new MailSearchItem(
                reference,
                candidate.AccountId,
                Compact(candidate.Item.Subject, 300),
                Compact(candidate.Item.Sender, 200),
                candidate.Item.ReceivedAt,
                Compact(candidate.Item.Preview, 160),
                candidate.Item.IsRead,
                candidate.Item.HasAttachments));
        }

        var hasMore = state.Accounts.Values.Any(account =>
            account.Items.Count > 0 || account.NextProviderCursor is not null ||
            !account.Started && account.FailureKind == ReadFailureKind.BudgetExceeded);
        var nextCursor = hasMore ? _references.Put("c_", state.Copy()) : null;
        var failures = state.Accounts
            .Where(pair => pair.Value.Failure is not null && !sharing.IsHidden(pair.Key))
            .Select(pair => new AccountReadFailure(pair.Key, pair.Value.Failure!, pair.Value.FailureKind))
            .OrderBy(failure => failure.AccountId, StringComparer.Ordinal)
            .ToArray();

        return new MailSearchResult(
            items,
            state.AccountIds.Where(id => !sharing.IsHidden(id)).ToArray(),
            failures,
            failures.Length == 0,
            nextCursor,
            state.Query.Start,
            state.Query.End,
            state.DefaultLookbackDaysApplied,
            state.Query.InboxOnly);
    }

    private async Task ReadMailAccountAsync(
        string accountId,
        AccountMailCursorState accountState,
        IReadOnlyDictionary<string, Account> accountsById,
        ProviderMailQuery providerQuery,
        int providerPageSize,
        SemaphoreSlim accountReadGate,
        ConcurrentDictionary<string, int> pagesRead,
        CancellationToken workCancellationToken,
        CancellationToken cancellationToken)
    {
        var enteredGate = false;
        string? registeredCursorFingerprint = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!accountsById.TryGetValue(accountId, out var account) || !account.MailReadEnabled)
            {
                accountState.Items.Clear();
                accountState.Started = true;
                accountState.NextProviderCursor = null;
                accountState.Failure = "The source account is no longer available for mail reads.";
                accountState.FailureKind = ReadFailureKind.LocalConfiguration;
                return;
            }

            if (accountState.Items.Count > 0 || accountState.Failure is not null ||
                accountState.Started && accountState.NextProviderCursor is null)
            {
                return;
            }

            if (!_mailReaders.TryGetValue(account.Provider, out var reader))
            {
                accountState.Started = true;
                accountState.NextProviderCursor = null;
                accountState.Failure = "Mail reading is unavailable for this provider.";
                accountState.FailureKind = ReadFailureKind.ProviderUnavailable;
                return;
            }

            await accountReadGate.WaitAsync(workCancellationToken);
            enteredGate = true;
            while (accountState.Items.Count == 0 &&
                   (!accountState.Started || accountState.NextProviderCursor is not null))
            {
                workCancellationToken.ThrowIfCancellationRequested();
                if (pagesRead.AddOrUpdate(accountId, 1, (_, count) => count + 1) > MaximumProviderPagesPerRefill)
                {
                    throw new ProviderPaginationException();
                }

                var providerCursor = accountState.Started ? accountState.NextProviderCursor : null;
                RegisterProviderCursor(accountState.SeenProviderCursors, providerCursor);
                registeredCursorFingerprint = providerCursor is null ? null : ProviderCursorFingerprint(providerCursor);
                // Graph next links retain the first page size even when the output limit changes later.
                accountState.ProviderPageSize = accountState.ProviderPageSize == 0
                    ? providerPageSize
                    : accountState.ProviderPageSize;
                var page = await reader.SearchAsync(
                    account, providerQuery, accountState.ProviderPageSize, providerCursor, workCancellationToken);
                var next = ValidateProviderContinuation(accountState.SeenProviderCursors, page.NextCursor);
                accountState.Started = true;
                accountState.NextProviderCursor = next;
                accountState.Items.AddRange(page.Items.Where(item => MatchesMailFilters(providerQuery, item)));
                accountState.Failure = null;
                registeredCursorFingerprint = null;
            }
        }
        catch (Exception exception) when (IsProviderReadFailure(exception, cancellationToken))
        {
            accountState.Failure = ReadFailureReason(exception);
            accountState.FailureKind = GetReadFailureKind(exception);
            if (accountState.FailureKind == ReadFailureKind.BudgetExceeded)
            {
                // This page did not complete; retry exactly this position after the local budget resets.
                if (registeredCursorFingerprint is not null)
                    accountState.SeenProviderCursors.Remove(registeredCursorFingerprint);
            }
            else
            {
                accountState.Started = true;
                accountState.NextProviderCursor = null;
            }
        }
        finally
        {
            if (enteredGate) accountReadGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<MailMessageResult> ReadMailAsync(MailReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reference);
        if (request.Reference.Length > 128 || request.Offset < 0 || request.MaxCharacters is < 1 or > 16_000)
        {
            throw new ArgumentException("The mail reference or text window is invalid.", nameof(request));
        }

        var address = _references.Get<MailItemAddress>(request.Reference, "m_");
        var sharing = await ReadSharingSnapshotAsync(cancellationToken);
        var account = sharing.Accounts
            .SingleOrDefault(item => string.Equals(item.Id, address.AccountId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The source account is no longer connected.");
        if (!account.MailReadEnabled || !_mailReaders.TryGetValue(account.Provider, out var reader))
        {
            throw new InvalidOperationException("Mail reading is unavailable for the source account.");
        }

        ProviderMailMessage message;
        await _readBudget.AdmitReadAsync(detail: true, cancellationToken);
        try
        {
            var cacheKey = System.Text.Json.JsonSerializer.Serialize(new
            {
                sharing.Fingerprint,
                account.Id,
                address.ProviderMessageId
            });
            message = await _mailDetails.GetAsync(cacheKey,
                token => ReadWithDeadlineAsync(inner => reader.ReadAsync(account, address.ProviderMessageId, inner), token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderReadException("Provider read timed out. Try reading this message again.", ReadFailureKind.Timeout);
        }
        await EnsureSharingUnchangedAsync(sharing, cancellationToken);
        var offset = Math.Min(request.Offset, message.PlainText.Length);
        var length = Math.Min(request.MaxCharacters, message.PlainText.Length - offset);
        var text = message.PlainText.Substring(offset, length);
        return new MailMessageResult(
            request.Reference,
            account.Id,
            Compact(message.Subject, 300),
            Compact(message.Sender, 200),
            message.To.Take(20).Select(value => Compact(value, 200)).ToArray(),
            message.Cc.Take(20).Select(value => Compact(value, 200)).ToArray(),
            message.ReceivedAt,
            text,
            offset,
            offset + length < message.PlainText.Length,
            message.IsRead,
            message.HasAttachments);
    }

    /// <inheritdoc />
    public async Task<CalendarListResult> ListCalendarsAsync(
        CalendarListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sharing = await ReadSharingSnapshotAsync(cancellationToken);
        var accounts = SelectCalendarAccounts(sharing.Accounts, request.AccountIds);
        if (accounts.Count > 0)
            await _readBudget.AdmitReadAsync(detail: false, cancellationToken);
        var calendars = new List<CalendarListItem>();
        var failures = new List<AccountReadFailure>();
        foreach (var account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_calendarReaders.TryGetValue(account.Provider, out var reader))
            {
                failures.Add(new AccountReadFailure(account.Id, "Calendar reading is unavailable for this provider.", ReadFailureKind.ProviderUnavailable));
                continue;
            }

            try
            {
                var providerCalendars = await ReadWithDeadlineAsync(token => reader.ListCalendarsAsync(account, token), cancellationToken);
                var selectedCalendars = providerCalendars.Where(calendar => sharing.AllowsCalendar(account.Id, calendar.ProviderCalendarId)).ToArray();
                foreach (var calendar in selectedCalendars.Take(100))
                {
                    var reference = _references.Put(
                        "cal_",
                        new CalendarAddress(account.Id, calendar.ProviderCalendarId));
                    calendars.Add(new CalendarListItem(
                        reference,
                        account.Id,
                        Compact(calendar.Name, 200),
                        calendar.Primary,
                        CompactNullable(calendar.TimeZone, 100)));
                }
                if (selectedCalendars.Length > 100)
                {
                    failures.Add(new AccountReadFailure(account.Id, "Only the first 100 calendars were returned for this account. Calendar coverage is incomplete.", ReadFailureKind.ResultLimit));
                }
            }
            catch (Exception exception) when (IsProviderReadFailure(exception, cancellationToken))
            {
                failures.Add(new AccountReadFailure(
                    account.Id,
                    ReadFailureReason(exception), GetReadFailureKind(exception)));
            }
        }

        await EnsureSharingUnchangedAsync(sharing, cancellationToken);
        return new CalendarListResult(
            calendars
                .OrderBy(calendar => calendar.AccountId, StringComparer.Ordinal)
                .ThenByDescending(calendar => calendar.Primary)
                .ThenBy(calendar => calendar.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            accounts.Select(account => account.Id).ToArray(),
            failures.OrderBy(failure => failure.AccountId, StringComparer.Ordinal).ToArray(),
            failures.Count == 0);
    }

    /// <inheritdoc />
    public async Task<EventSearchResult> SearchEventsAsync(
        EventSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var start = ParseExplicitDateTime(request.Start, nameof(request.Start));
        var end = ParseExplicitDateTime(request.End, nameof(request.End));
        if (end <= start || end - start > TimeSpan.FromDays(31))
        {
            throw new ArgumentException("The event window must be positive and no longer than 31 days.", nameof(request));
        }

        if (request.Limit is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Event search limit must be between 1 and 50.");
        }

        var sharing = await ReadSharingSnapshotAsync(cancellationToken);
        var allAccounts = sharing.Accounts;
        var scopeKey = CreateEventScopeKey(request);
        if (allAccounts.Any(account => account.CalendarReadEnabled))
            await _readBudget.AdmitReadAsync(detail: false, cancellationToken);
        EventCursorState state;
        if (string.IsNullOrWhiteSpace(request.Cursor))
        {
            state = await CreateEventCursorStateAsync(request, start, end, scopeKey, sharing, cancellationToken);
        }
        else
        {
            if (request.Cursor.Length > 128)
            {
                throw new ArgumentException("The event cursor is invalid.", nameof(request));
            }

            state = _references.Get<EventCursorState>(request.Cursor, "ec_").Copy();
            if (state.Start != start || state.End != end || !string.Equals(state.ScopeKey, scopeKey, StringComparison.Ordinal))
            {
                throw new ArgumentException("The event cursor belongs to a different time window or calendar scope.", nameof(request));
            }
        }

        var accountsById = allAccounts.ToDictionary(account => account.Id, StringComparer.Ordinal);
        foreach (var target in state.Targets.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!accountsById.TryGetValue(target.AccountId, out var account) || !account.CalendarReadEnabled ||
                !sharing.AllowsCalendar(target.AccountId, target.ProviderCalendarId))
            {
                target.Events.Clear();
                target.Started = true;
                target.NextProviderCursor = null;
                target.Failure = "The source account is no longer available for calendar reads.";
                target.FailureKind = ReadFailureKind.LocalConfiguration;
                continue;
            }

            if (target.Events.Count >= request.Limit || target.Started && target.NextProviderCursor is null)
            {
                continue;
            }

            if (!_calendarReaders.TryGetValue(account.Provider, out var reader))
            {
                target.Started = true;
                target.NextProviderCursor = null;
                target.Failure = "Calendar reading is unavailable for this provider.";
                target.FailureKind = ReadFailureKind.ProviderUnavailable;
                continue;
            }

            try
            {
                await ReadWithDeadlineAsync(async token =>
                {
                    var pagesRead = 0;
                    while (target.Events.Count < request.Limit &&
                           (!target.Started || target.NextProviderCursor is not null))
                    {
                        token.ThrowIfCancellationRequested();
                        if (++pagesRead > MaximumProviderPagesPerRefill)
                        {
                            throw new ProviderPaginationException();
                        }

                        var providerCursor = target.Started ? target.NextProviderCursor : null;
                        RegisterProviderCursor(target.SeenProviderCursors, providerCursor);
                        target.ProviderPageSize = target.ProviderPageSize == 0 ? request.Limit : target.ProviderPageSize;
                        var page = await reader.SearchEventsAsync(
                            account, target.ProviderCalendarId, start, end, target.ProviderPageSize, providerCursor, token);
                        var next = ValidateProviderContinuation(target.SeenProviderCursors, page.NextCursor);
                        target.Started = true;
                        target.NextProviderCursor = next;
                        target.Events.AddRange(page.Events);
                        target.Failure = null;
                    }

                    return true;
                }, cancellationToken);
            }
            catch (Exception exception) when (IsProviderReadFailure(exception, cancellationToken))
            {
                target.Started = true;
                target.NextProviderCursor = null;
                target.Failure = ReadFailureReason(exception);
                target.FailureKind = GetReadFailureKind(exception);
            }
        }

        await EnsureSharingUnchangedAsync(sharing, cancellationToken);
        var candidates = state.Targets
            .SelectMany(pair => pair.Value.Events.Select(item => new EventCandidate(pair.Key, item)))
            .OrderBy(candidate => candidate.Event.SortStart)
            .ThenBy(candidate => candidate.TargetKey, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Event.ProviderEventId, StringComparer.Ordinal)
            .Take(request.Limit)
            .ToArray();
        var events = new List<EventSearchItem>(candidates.Length);
        foreach (var candidate in candidates)
        {
            var target = state.Targets[candidate.TargetKey];
            target.Events.Remove(candidate.Event);
            var reference = _references.Put(
                "evt_",
                new EventAddress(target.AccountId, target.ProviderCalendarId, candidate.Event.ProviderEventId, target.CalendarReference));
            events.Add(new EventSearchItem(
                reference,
                target.CalendarReference,
                target.AccountId,
                Compact(candidate.Event.Title, 300),
                candidate.Event.Start,
                candidate.Event.End,
                candidate.Event.AllDay,
                candidate.Event.Cancelled,
                Compact(candidate.Event.Location, 200)));
        }

        var hasMore = state.Targets.Values.Any(target =>
            target.Events.Count > 0 || target.NextProviderCursor is not null);
        var nextCursor = hasMore ? _references.Put("ec_", state.Copy()) : null;
        var failures = state.InitialFailures
            .Concat(state.Targets.Values
                .Where(target => target.Failure is not null)
                .Select(target => new AccountReadFailure(target.AccountId, target.Failure!, target.FailureKind)))
            .Where(failure => !sharing.IsHidden(failure.AccountId))
            .GroupBy(failure => failure.AccountId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(failure => failure.AccountId, StringComparer.Ordinal)
            .ToArray();
        return new EventSearchResult(
            events,
            state.AccountIds.Where(id => !sharing.IsHidden(id)).ToArray(),
            failures,
            failures.Length == 0,
            nextCursor);
    }

    /// <inheritdoc />
    public async Task<EventResult> ReadEventAsync(EventReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reference);
        if (request.Reference.Length > 128 || request.MaxDescriptionCharacters is < 1 or > 16_000)
        {
            throw new ArgumentException("The event reference or description limit is invalid.", nameof(request));
        }

        var address = _references.Get<EventAddress>(request.Reference, "evt_");
        var sharing = await ReadSharingSnapshotAsync(cancellationToken);
        var account = sharing.Accounts
            .SingleOrDefault(item => string.Equals(item.Id, address.AccountId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The source account is no longer connected.");
        if (!account.CalendarReadEnabled || !sharing.AllowsCalendar(account.Id, address.ProviderCalendarId) ||
            !_calendarReaders.TryGetValue(account.Provider, out var reader))
        {
            throw new InvalidOperationException("Calendar reading is unavailable for the source account.");
        }

        ProviderEvent providerEvent;
        await _readBudget.AdmitReadAsync(detail: true, cancellationToken);
        try
        {
            var cacheKey = System.Text.Json.JsonSerializer.Serialize(new
            {
                sharing.Fingerprint,
                account.Id,
                address.ProviderCalendarId,
                address.ProviderEventId
            });
            providerEvent = await _eventDetails.GetAsync(cacheKey,
                token => ReadWithDeadlineAsync(inner => reader.ReadEventAsync(
                    account, address.ProviderCalendarId, address.ProviderEventId, inner), token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderReadException("Provider read timed out. Try reading this appointment again.", ReadFailureKind.Timeout);
        }
        await EnsureSharingUnchangedAsync(sharing, cancellationToken);
        var descriptionLength = Math.Min(providerEvent.Description.Length, request.MaxDescriptionCharacters);
        return new EventResult(
            request.Reference,
            address.CalendarReference,
            account.Id,
            Compact(providerEvent.Title, 300),
            providerEvent.Start,
            providerEvent.End,
            providerEvent.AllDay,
            providerEvent.Cancelled,
            Compact(providerEvent.Location, 200),
            providerEvent.Description[..descriptionLength],
            descriptionLength < providerEvent.Description.Length,
            providerEvent.Attendees.Take(20).Select(attendee => Compact(attendee, 200)).ToArray(),
            CompactNullable(providerEvent.MeetingLink, 500));
    }

    private async Task<EventCursorState> CreateEventCursorStateAsync(
        EventSearchRequest request,
        DateTimeOffset start,
        DateTimeOffset end,
        string scopeKey,
        SharingSnapshot sharing,
        CancellationToken cancellationToken)
    {
        var allAccounts = sharing.Accounts;
        var targets = new Dictionary<string, CalendarTargetCursorState>(StringComparer.Ordinal);
        var failures = new List<AccountReadFailure>();
        IReadOnlyList<string> accountIds;
        if (request.CalendarReferences is { Count: > 0 })
        {
            if (request.CalendarReferences.Count > 20)
            {
                throw new ArgumentException("Select at most 20 calendars per event search.", nameof(request));
            }

            var selected = request.CalendarReferences.Distinct(StringComparer.Ordinal).ToArray();
            var byId = allAccounts.ToDictionary(account => account.Id, StringComparer.Ordinal);
            var selectedCalendars = new HashSet<CalendarAddress>();
            var targetIndex = 0;
            foreach (var reference in selected)
            {
                if (reference.Length > 128)
                {
                    throw new ArgumentException("A selected calendar reference is invalid.", nameof(request));
                }

                var address = _references.Get<CalendarAddress>(reference, "cal_");
                if (!selectedCalendars.Add(address))
                {
                    continue;
                }

                if (!byId.TryGetValue(address.AccountId, out var account) || !account.CalendarReadEnabled ||
                    !sharing.AllowsCalendar(address.AccountId, address.ProviderCalendarId))
                {
                    failures.Add(new AccountReadFailure(address.AccountId, "The source account is no longer available for calendar reads.", ReadFailureKind.LocalConfiguration));
                    targets[$"t{targetIndex++}"] = new CalendarTargetCursorState(address.AccountId, address.ProviderCalendarId, reference);
                    continue;
                }

                targets[$"t{targetIndex++}"] = new CalendarTargetCursorState(
                    account.Id,
                    address.ProviderCalendarId,
                    reference);
            }

            accountIds = targets.Values.Select(target => target.AccountId).Distinct(StringComparer.Ordinal).ToArray();
        }
        else
        {
            var accounts = SelectCalendarAccounts(allAccounts, request.AccountIds);
            accountIds = accounts.Select(account => account.Id).ToArray();
            var targetIndex = 0;
            foreach (var account in accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_calendarReaders.TryGetValue(account.Provider, out var reader))
                {
                    failures.Add(new AccountReadFailure(account.Id, "Calendar reading is unavailable for this provider.", ReadFailureKind.ProviderUnavailable));
                    continue;
                }

                try
                {
                    var calendars = await ReadWithDeadlineAsync(token => reader.ListCalendarsAsync(account, token), cancellationToken);
                    var selectedCalendars = calendars.Where(item => sharing.AllowsCalendar(account.Id, item.ProviderCalendarId)).ToArray();
                    var calendar = selectedCalendars.FirstOrDefault(item => item.Primary) ?? selectedCalendars.FirstOrDefault();
                    if (calendar is null)
                    {
                        failures.Add(new AccountReadFailure(account.Id, "No readable calendar is available.", ReadFailureKind.ItemUnavailable));
                        continue;
                    }

                    var reference = _references.Put(
                        "cal_",
                        new CalendarAddress(account.Id, calendar.ProviderCalendarId));
                    targets[$"t{targetIndex++}"] = new CalendarTargetCursorState(
                        account.Id,
                        calendar.ProviderCalendarId,
                        reference);
                }
                catch (Exception exception) when (IsProviderReadFailure(exception, cancellationToken))
                {
                    failures.Add(new AccountReadFailure(
                        account.Id,
                        ReadFailureReason(exception), GetReadFailureKind(exception)));
                }
            }
        }

        return new EventCursorState(start, end, scopeKey, accountIds, targets, failures);
    }

    private async Task<SharingSnapshot> ReadSharingSnapshotAsync(CancellationToken cancellationToken)
    {
        var accounts = await _accounts.ListAsync(cancellationToken);
        var settings = new Dictionary<string, AccountSharingSettings>(StringComparer.Ordinal);
        foreach (var account in accounts)
        {
            var stored = await _sharing.GetAsync(account.Id, cancellationToken) ?? new AccountSharingSettings(account.Id);
            stored.Validate();
            settings.Add(account.Id, stored);
        }

        var shared = accounts.Where(account => settings[account.Id].Enabled).Select(account => account with
        {
            MailReadEnabled = account.MailReadEnabled && settings[account.Id].ShareMail,
            CalendarReadEnabled = account.CalendarReadEnabled && settings[account.Id].ShareCalendars &&
                settings[account.Id].CalendarIds is not { Count: 0 }
        }).ToArray();
        // Compare the complete access snapshot before releasing results, including changes made by another process.
        var fingerprint = System.Text.Json.JsonSerializer.Serialize(new
        {
            Accounts = accounts.OrderBy(account => account.Id, StringComparer.Ordinal),
            Settings = settings.Values.OrderBy(item => item.AccountId, StringComparer.Ordinal)
        });
        return new SharingSnapshot(shared, settings, fingerprint);
    }

    private async Task EnsureSharingUnchangedAsync(SharingSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!string.Equals(snapshot.Fingerprint, (await ReadSharingSnapshotAsync(cancellationToken)).Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Account access changed during this read. Start a new request to use the current sharing choices.");
        }
    }

    private sealed record SharingSnapshot(
        IReadOnlyList<Account> Accounts,
        IReadOnlyDictionary<string, AccountSharingSettings> Settings,
        string Fingerprint)
    {
        public bool AllowsCalendar(string accountId, string calendarId) =>
            Settings.TryGetValue(accountId, out var settings) && settings.AllowsCalendar(calendarId);

        public bool IsHidden(string accountId) => Settings.TryGetValue(accountId, out var settings) && !settings.Enabled;
    }

    private sealed class MemoryAccountSharingStore : IAccountSharingStore
    {
        private readonly ConcurrentDictionary<string, AccountSharingSettings> _settings = new(StringComparer.Ordinal);

        public Task<AccountSharingSettings?> GetAsync(string accountId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_settings.GetValueOrDefault(accountId));
        }

        public Task SaveAsync(AccountSharingSettings settings, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _settings[settings.AccountId] = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryMailSearchPreferencesStore : IMailSearchPreferencesStore
    {
        private MailSearchPreferences _preferences = new();

        public Task<MailSearchPreferences> GetAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Volatile.Read(ref _preferences));
        }

        public Task SaveAsync(MailSearchPreferences preferences, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _preferences, preferences);
            return Task.CompletedTask;
        }
    }

    private static async Task<T> ReadWithDeadlineAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ProviderReadTimeout);
        var result = await read(deadline.Token);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<AccountConnectionCheck> CheckConnectionAsync(
        Account account,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!_connectionCheckers.TryGetValue(account.Provider, out var checker))
            {
                return FailedConnectionCheck(account, ReadFailureKind.ProviderUnavailable);
            }

            try
            {
                var result = await ReadWithDeadlineAsync(token => checker.CheckAsync(account, token), cancellationToken);
                if (result.AccountId != account.Id ||
                    (!account.MailReadEnabled && !account.CalendarReadEnabled) ||
                    (account.MailReadEnabled && result.MailReachable is null) ||
                    (account.CalendarReadEnabled && result.CalendarReachable is null))
                {
                    return FailedConnectionCheck(account, ReadFailureKind.Unknown);
                }
                return result.HasFailures ? result with
                {
                    Reachable = false,
                    FailureKind = result.FailureKind ?? result.MailFailureKind ?? result.CalendarFailureKind ?? ReadFailureKind.Unknown
                } : result;
            }
            catch (Exception exception) when (IsProviderReadFailure(exception, cancellationToken))
            {
                return FailedConnectionCheck(account, GetReadFailureKind(exception));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return FailedConnectionCheck(account, ReadFailureKind.Unknown);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static AccountConnectionCheck FailedConnectionCheck(Account account, ReadFailureKind failureKind) =>
        new(
            account.Id,
            Reachable: false,
            failureKind,
            account.MailReadEnabled ? false : null,
            account.MailReadEnabled ? failureKind : null,
            account.CalendarReadEnabled ? false : null,
            account.CalendarReadEnabled ? failureKind : null);

    private static bool IsProviderReadFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        (exception is ProviderReadException or ProviderAuthenticationException or OperationCanceledException or ProviderPaginationException);

    private static string ReadFailureReason(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return "Provider read timed out. Try this search again.";
        }

        if (exception is ProviderPaginationException)
        {
            return "Provider pagination could not complete within its limits. Results are partial; try a narrower search.";
        }

        var advice = ReadFailureGuidance.Describe(GetReadFailureKind(exception));
        return $"{advice.Explanation} {advice.Action}";
    }

    private static ReadFailureKind GetReadFailureKind(Exception exception) => exception switch
    {
        ProviderReadException provider => provider.Kind,
        ProviderAuthenticationException => ReadFailureKind.SignInRequired,
        OperationCanceledException => ReadFailureKind.Timeout,
        ProviderPaginationException => ReadFailureKind.ResultLimit,
        _ => ReadFailureKind.Unknown
    };

    private static void RegisterProviderCursor(HashSet<string> seenCursors, string? cursor)
    {
        if (cursor is null)
        {
            return;
        }

        if (cursor.Length > 8_192 || seenCursors.Count >= MaximumProviderCursorHistory ||
            !seenCursors.Add(ProviderCursorFingerprint(cursor)))
        {
            throw new ProviderPaginationException();
        }
    }

    private static string? ValidateProviderContinuation(HashSet<string> seenCursors, string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        if (cursor.Length > 8_192 || seenCursors.Contains(ProviderCursorFingerprint(cursor)))
        {
            throw new ProviderPaginationException();
        }

        return cursor;
    }

    private static string ProviderCursorFingerprint(string cursor) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(cursor)));

    private sealed class ProviderPaginationException : Exception
    {
    }

    private static string CreateEventScopeKey(EventSearchRequest request)
    {
        if (request.CalendarReferences is { Count: > 0 })
        {
            return "cal:" + string.Join(
                '\u001f',
                request.CalendarReferences.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        }

        return request.AccountIds is { Count: > 0 }
            ? "acc:" + string.Join(
                '\u001f',
                request.AccountIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            : "acc:*";
    }

    private static IReadOnlyList<Account> SelectCalendarAccounts(
        IReadOnlyList<Account> allAccounts,
        IReadOnlyList<string>? requestedIds)
    {
        if (requestedIds is null || requestedIds.Count == 0)
        {
            return allAccounts.Where(account => account.CalendarReadEnabled).ToArray();
        }

        var requested = requestedIds.Distinct(StringComparer.Ordinal).ToArray();
        var byId = allAccounts.ToDictionary(account => account.Id, StringComparer.Ordinal);
        var selected = new List<Account>(requested.Length);
        foreach (var id in requested)
        {
            if (!byId.TryGetValue(id, out var account))
            {
                throw new ArgumentException($"Calendar account '{id}' is not connected.", nameof(requestedIds));
            }

            if (!account.CalendarReadEnabled)
            {
                throw new ArgumentException($"Calendar account '{id}' has no calendar read consent.", nameof(requestedIds));
            }

            selected.Add(account);
        }

        return selected;
    }

    private static DateTimeOffset ParseExplicitDateTime(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var timeMarker = value.IndexOf('T');
        var hasOffset = value.EndsWith('Z') ||
                        timeMarker >= 0 && (value.LastIndexOf('+') > timeMarker || value.LastIndexOf('-') > timeMarker);
        if (!hasOffset ||
            !DateTimeOffset.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var result))
        {
            throw new ArgumentException("Event times must be ISO 8601 values with an explicit offset.", parameterName);
        }

        return result;
    }

    private static DateTimeOffset? ParseOptionalExplicitDateTime(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseExplicitDateTime(value, parameterName);

    private static string? NormalizeOptionalContains(string? value, string parameterName, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 254 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"The optional mail {fieldName} filter is invalid.", parameterName);
        }

        return normalized;
    }

    private static bool MatchesMailFilters(ProviderMailQuery query, ProviderMailSummary item) =>
        (!query.UnreadOnly || !item.IsRead) &&
        (!query.HasAttachments.HasValue || item.HasAttachments == query.HasAttachments.Value) &&
        (query.Sender is null || item.Sender.Contains(query.Sender, StringComparison.OrdinalIgnoreCase)) &&
        (query.RecipientContains is null || item.Recipients is not null &&
            item.Recipients.Any(recipient => recipient.Contains(query.RecipientContains, StringComparison.OrdinalIgnoreCase))) &&
        (query.Start is null || item.ReceivedAt >= query.Start.Value) &&
        (query.End is null || item.ReceivedAt < query.End.Value);

    private static IReadOnlyList<Account> SelectMailAccounts(
        IReadOnlyList<Account> allAccounts,
        IReadOnlyList<string>? requestedIds)
    {
        if (requestedIds is null || requestedIds.Count == 0)
        {
            return allAccounts.Where(account => account.MailReadEnabled).ToArray();
        }

        var requested = requestedIds.Distinct(StringComparer.Ordinal).ToArray();
        var byId = allAccounts.ToDictionary(account => account.Id, StringComparer.Ordinal);
        var selected = new List<Account>(requested.Length);
        foreach (var id in requested)
        {
            if (!byId.TryGetValue(id, out var account))
            {
                throw new ArgumentException($"Mail account '{id}' is not connected.", nameof(requestedIds));
            }

            if (!account.MailReadEnabled)
            {
                throw new ArgumentException($"Mail account '{id}' has no mail read consent.", nameof(requestedIds));
            }

            selected.Add(account);
        }

        return selected;
    }

    private static string Compact(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maximumLength ? compact : compact[..maximumLength];
    }

    private static string? CompactNullable(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Compact(value, maximumLength);

    private sealed record MailCandidate(string AccountId, ProviderMailSummary Item);
    private sealed record MailItemAddress(string AccountId, string ProviderMessageId);
    private sealed record CalendarAddress(string AccountId, string ProviderCalendarId);
    private sealed record EventAddress(
        string AccountId,
        string ProviderCalendarId,
        string ProviderEventId,
        string CalendarReference);
    private sealed record EventCandidate(string TargetKey, ProviderEventSummary Event);

    private sealed record MailCursorState(
        ProviderMailQuery RequestedQuery,
        ProviderMailQuery Query,
        int? DefaultLookbackDaysApplied,
        IReadOnlyList<string> AccountIds,
        Dictionary<string, AccountMailCursorState> Accounts)
    {
        public MailCursorState Copy() => new(
            RequestedQuery,
            Query,
            DefaultLookbackDaysApplied,
            AccountIds.ToArray(),
            Accounts.ToDictionary(pair => pair.Key, pair => pair.Value.Copy(), StringComparer.Ordinal));
    }

    private sealed class AccountMailCursorState
    {
        public bool Started { get; set; }
        public int ProviderPageSize { get; set; }
        public string? NextProviderCursor { get; set; }
        public string? Failure { get; set; }
        public ReadFailureKind FailureKind { get; set; }
        public HashSet<string> SeenProviderCursors { get; } = new(StringComparer.Ordinal);
        public List<ProviderMailSummary> Items { get; } = [];

        public AccountMailCursorState Copy()
        {
            var clone = new AccountMailCursorState
            {
                Started = Started,
                ProviderPageSize = ProviderPageSize,
                NextProviderCursor = NextProviderCursor,
                Failure = Failure,
                FailureKind = FailureKind
            };
            clone.Items.AddRange(Items);
            clone.SeenProviderCursors.UnionWith(SeenProviderCursors);
            return clone;
        }
    }

    private sealed record EventCursorState(
        DateTimeOffset Start,
        DateTimeOffset End,
        string ScopeKey,
        IReadOnlyList<string> AccountIds,
        Dictionary<string, CalendarTargetCursorState> Targets,
        IReadOnlyList<AccountReadFailure> InitialFailures)
    {
        public EventCursorState Copy() => new(
            Start,
            End,
            ScopeKey,
            AccountIds.ToArray(),
            Targets.ToDictionary(pair => pair.Key, pair => pair.Value.Copy(), StringComparer.Ordinal),
            InitialFailures.ToArray());
    }

    private sealed class CalendarTargetCursorState(
        string accountId,
        string providerCalendarId,
        string calendarReference)
    {
        public string AccountId { get; } = accountId;
        public string ProviderCalendarId { get; } = providerCalendarId;
        public string CalendarReference { get; } = calendarReference;
        public bool Started { get; set; }
        public int ProviderPageSize { get; set; }
        public string? NextProviderCursor { get; set; }
        public string? Failure { get; set; }
        public ReadFailureKind FailureKind { get; set; }
        public HashSet<string> SeenProviderCursors { get; } = new(StringComparer.Ordinal);
        public List<ProviderEventSummary> Events { get; } = [];

        public CalendarTargetCursorState Copy()
        {
            var clone = new CalendarTargetCursorState(AccountId, ProviderCalendarId, CalendarReference)
            {
                Started = Started,
                ProviderPageSize = ProviderPageSize,
                NextProviderCursor = NextProviderCursor,
                Failure = Failure,
                FailureKind = FailureKind
            };
            clone.Events.AddRange(Events);
            clone.SeenProviderCursors.UnionWith(SeenProviderCursors);
            return clone;
        }
    }

    private sealed class LocalReferenceStore
    {
        private const int MaximumEntries = 4_096;
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        public string Put<T>(string prefix, T value) where T : notnull
        {
            Prune();
            var reference = prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(12))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            _entries[reference] = new Entry(value, DateTimeOffset.UtcNow.Add(Lifetime));
            return reference;
        }

        public T Get<T>(string reference, string prefix)
        {
            if (!reference.StartsWith(prefix, StringComparison.Ordinal) ||
                !_entries.TryGetValue(reference, out var entry) ||
                entry.ExpiresAt <= DateTimeOffset.UtcNow ||
                entry.Value is not T value)
            {
                _entries.TryRemove(reference, out _);
                throw new ArgumentException("The local result reference is invalid or expired.", nameof(reference));
            }

            return value;
        }

        private void Prune()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var pair in _entries.Where(pair => pair.Value.ExpiresAt <= now))
            {
                _entries.TryRemove(pair.Key, out _);
            }

            if (_entries.Count < MaximumEntries)
            {
                return;
            }

            foreach (var pair in _entries.OrderBy(pair => pair.Value.ExpiresAt).Take(_entries.Count - MaximumEntries + 1))
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }

        private sealed record Entry(object Value, DateTimeOffset ExpiresAt);
    }
}
