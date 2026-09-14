using MailMeUp.Core;

namespace MailMeUp.Application;

/// <summary>The application boundary shared by command-line and MCP adapters.</summary>
public interface IMailMeUpApplication
{
    /// <summary>Reports this build's readiness and supported provider modules.</summary>
    ApplicationStatus GetStatus();

    /// <summary>Returns non-secret account metadata from the local registry.</summary>
    Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns only accounts shared with the assistant, with effective read categories.</summary>
    Task<IReadOnlyList<Account>> ListSharedAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns local sharing choices for all connected accounts; for local setup adapters only.</summary>
    Task<IReadOnlyList<AccountSharingSettings>> ListAccountSharingAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves explicit local sharing choices independently of provider consent; for local setup adapters only.</summary>
    Task<AccountSharingSettings> SaveAccountSharingAsync(AccountSharingSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Reads the local default period used by mail searches without an explicit date.</summary>
    Task<MailSearchPreferences> GetMailSearchPreferencesAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the global default mail search period; for local setup adapters only.</summary>
    Task<MailSearchPreferences> SaveMailSearchPreferencesAsync(MailSearchPreferences preferences, CancellationToken cancellationToken = default);

    /// <summary>Returns local read limits and aggregate usage, or null when this host cannot manage guardrails.</summary>
    Task<ReadGuardrailStatus?> GetReadGuardrailStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ReadGuardrailStatus?>(null);
    }

    /// <summary>Saves local read limits against the last loaded settings; reserved for local setup, never an MCP write tool.</summary>
    Task<ReadGuardrailStatus> SaveReadGuardrailLimitsAsync(
        ReadGuardrailLimits limits,
        ReadGuardrailLimits expectedLimits,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<ReadGuardrailStatus>(new NotSupportedException("Read guardrail settings are unavailable in this host."));
    }

    /// <summary>Discovers calendar names for local setup even before assistant sharing is enabled; never expose as an MCP tool.</summary>
    Task<IReadOnlyList<ProviderCalendar>> ListAvailableCalendarsAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>Checks local account reachability and refreshes provider tokens silently when possible; for local setup adapters only.</summary>
    Task<AccountConnectionCheckResult> CheckConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports local provider app registration readiness without returning credentials.</summary>
    Task<IReadOnlyList<ProviderSetupStatus>> ListProviderSetupAsync(CancellationToken cancellationToken = default);

    /// <summary>Configures a provider app identity from local CLI input.</summary>
    Task<ProviderSetupResult> ConfigureProviderAsync(string providerId, string source, CancellationToken cancellationToken = default);

    /// <summary>Runs local interactive sign-in for one read-only provider account.</summary>
    Task<AccountConnectionResult> ConnectAccountAsync(
        string providerId,
        AccountConnectionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one account and its local credential cache without changing provider data.</summary>
    Task<AccountRemovalResult> RemoveAccountAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>Searches mail across selected accounts and returns compact references.</summary>
    Task<MailSearchResult> SearchMailAsync(MailSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads a bounded plain-text segment for one prior search reference.</summary>
    Task<MailMessageResult> ReadMailAsync(MailReadRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists visible calendars with short local references.</summary>
    Task<CalendarListResult> ListCalendarsAsync(CalendarListRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns a bounded unified agenda from selected calendars.</summary>
    Task<EventSearchResult> SearchEventsAsync(EventSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads bounded details for one prior appointment reference.</summary>
    Task<EventResult> ReadEventAsync(EventReadRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Describes capabilities available in the running build.</summary>
public sealed record ApplicationStatus(string Stage, string Transport, bool ReadOnly, bool CanConnectAccounts, IReadOnlyList<ProviderDescriptor> Providers);
