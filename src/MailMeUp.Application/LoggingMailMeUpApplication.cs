using System.Diagnostics;
using MailMeUp.Core;
using MailMeUp.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MailMeUp.Application;

/// <summary>Records bounded operation diagnostics for both adapters without logging request or result content.</summary>
public sealed class LoggingMailMeUpApplication(
    IMailMeUpApplication application,
    ILogger<LoggingMailMeUpApplication> logger) : IMailMeUpApplication
{
    /// <inheritdoc />
    public ApplicationStatus GetStatus()
    {
        logger.LogDebug("Reporting application capabilities");
        return application.GetStatus();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken cancellationToken = default) =>
        RunAsync("list_accounts", () => application.ListAccountsAsync(cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Account>> ListSharedAccountsAsync(CancellationToken cancellationToken = default) =>
        RunAsync("list_shared_accounts", () => application.ListSharedAccountsAsync(cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<AccountSharingSettings>> ListAccountSharingAsync(CancellationToken cancellationToken = default) =>
        RunAsync("list_account_sharing", () => application.ListAccountSharingAsync(cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<AccountSharingSettings> SaveAccountSharingAsync(AccountSharingSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync("save_account_sharing", () => application.SaveAccountSharingAsync(settings, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<MailSearchPreferences> GetMailSearchPreferencesAsync(CancellationToken cancellationToken = default) =>
        RunAsync("get_mail_search_preferences", () => application.GetMailSearchPreferencesAsync(cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<MailSearchPreferences> SaveMailSearchPreferencesAsync(MailSearchPreferences preferences, CancellationToken cancellationToken = default) =>
        RunAsync("save_mail_search_preferences", () => application.SaveMailSearchPreferencesAsync(preferences, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderCalendar>> ListAvailableCalendarsAsync(string accountId, CancellationToken cancellationToken = default) =>
        RunAsync("discover_calendars_for_setup", () => application.ListAvailableCalendarsAsync(accountId, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<AccountConnectionCheckResult> CheckConnectionsAsync(CancellationToken cancellationToken = default) =>
        RunAsync("check_account_connections", () => application.CheckConnectionsAsync(cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderSetupStatus>> ListProviderSetupAsync(CancellationToken cancellationToken = default) =>
        RunAsync("setup_status", () => application.ListProviderSetupAsync(cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<ProviderSetupResult> ConfigureProviderAsync(string providerId, string source, CancellationToken cancellationToken = default) =>
        RunAsync("configure_provider", () => application.ConfigureProviderAsync(providerId, source, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<AccountConnectionResult> ConnectAccountAsync(string providerId, AccountConnectionOptions options, CancellationToken cancellationToken = default) =>
        RunAsync("connect_account", () => application.ConnectAccountAsync(providerId, options, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<AccountRemovalResult> RemoveAccountAsync(string accountId, CancellationToken cancellationToken = default) =>
        RunAsync("remove_account", () => application.RemoveAccountAsync(accountId, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<MailSearchResult> SearchMailAsync(MailSearchRequest request, CancellationToken cancellationToken = default) =>
        RunAsync("search_mail", () => application.SearchMailAsync(request, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<MailMessageResult> ReadMailAsync(MailReadRequest request, CancellationToken cancellationToken = default) =>
        RunAsync("read_mail", () => application.ReadMailAsync(request, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<CalendarListResult> ListCalendarsAsync(CalendarListRequest request, CancellationToken cancellationToken = default) =>
        RunAsync("list_calendars", () => application.ListCalendarsAsync(request, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<EventSearchResult> SearchEventsAsync(EventSearchRequest request, CancellationToken cancellationToken = default) =>
        RunAsync("search_events", () => application.SearchEventsAsync(request, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<EventResult> ReadEventAsync(EventReadRequest request, CancellationToken cancellationToken = default) =>
        RunAsync("read_event", () => application.ReadEventAsync(request, cancellationToken), cancellationToken);

    private async Task<T> RunAsync<T>(string operation, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["OperationId"] = Guid.NewGuid().ToString("N")
        });
        var started = Stopwatch.GetTimestamp();
        logger.LogDebug("Operation {Operation} started", operation);
        try
        {
            var result = await action();
            logger.LogInformation("Operation {Operation} completed in {ElapsedMs} ms", operation, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (result is AccountConnectionCheckResult connections)
            {
                var mailChecks = connections.Accounts.Where(check => check.MailReachable.HasValue).ToArray();
                var calendarChecks = connections.Accounts.Where(check => check.CalendarReachable.HasValue).ToArray();
                var mailFailures = mailChecks.Where(check => check.MailReachable == false).ToArray();
                var calendarFailures = calendarChecks.Where(check => check.CalendarReachable == false).ToArray();
                logger.LogInformation(
                    "Operation {Operation} checked {AccountCount} accounts: mail {MailCheckCount} checked with {MailFailureCount} failures; calendar {CalendarCheckCount} checked with {CalendarFailureCount} failures",
                    operation,
                    connections.Accounts.Count,
                    mailChecks.Length,
                    mailFailures.Length,
                    calendarChecks.Length,
                    calendarFailures.Length);
                if (mailFailures.Length > 0 || calendarFailures.Length > 0)
                {
                    logger.LogWarning(
                        "Operation {Operation} read-access failure categories: mail {MailFailureCategories}; calendar {CalendarFailureCategories}",
                        operation,
                        FormatFailureCategories(mailFailures.Select(check => check.MailFailureKind ?? ReadFailureKind.Unknown)),
                        FormatFailureCategories(calendarFailures.Select(check => check.CalendarFailureKind ?? ReadFailureKind.Unknown)));
                }
            }
            var failedAccounts = result switch
            {
                MailSearchResult mail => mail.FailedAccounts.Count,
                CalendarListResult calendars => calendars.FailedAccounts.Count,
                EventSearchResult events => events.FailedAccounts.Count,
                _ => 0
            };
            if (failedAccounts > 0)
            {
                var failureCategories = result switch
                {
                    MailSearchResult mail => FormatFailureCategories(mail.FailedAccounts.Select(account => account.Kind)),
                    CalendarListResult calendars => FormatFailureCategories(calendars.FailedAccounts.Select(account => account.Kind)),
                    EventSearchResult events => FormatFailureCategories(events.FailedAccounts.Select(account => account.Kind)),
                    _ => string.Empty
                };
                logger.LogWarning(
                    "Operation {Operation} returned partial coverage; {FailedAccountCount} accounts unavailable ({FailureCategories})",
                    operation,
                    failedAccounts,
                    failureCategories);
                IEnumerable<AccountReadFailure> failures = result switch
                {
                    MailSearchResult mail => mail.FailedAccounts,
                    CalendarListResult calendars => calendars.FailedAccounts,
                    EventSearchResult events => events.FailedAccounts,
                    _ => []
                };
                foreach (var failure in failures)
                {
                    logger.LogWarning("Operation {Operation} unavailable account={AccountKey}; category={FailureCategory}",
                        operation, ReadDiagnostics.AccountKey(failure.AccountId), failure.Kind);
                }
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Operation {Operation} cancelled after {ElapsedMs} ms", operation, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            // Never pass the exception itself: its message or inner exceptions can contain private data.
            logger.LogWarning("Operation {Operation} failed after {ElapsedMs} ms ({ErrorType})", operation,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, exception.GetType().Name);
            throw;
        }
    }

    private static string FormatFailureCategories(IEnumerable<ReadFailureKind> kinds) =>
        string.Join(", ", kinds
            .GroupBy(kind => kind)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key}={group.Count()}"));
}
