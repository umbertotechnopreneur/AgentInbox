using MailMeUp.Core;
using MailMeUp.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MailMeUp.Application;

/// <summary>Exercises the same provider readers used by searches, retaining partial and failed check outcomes.</summary>
public sealed class ReadAccessConnectionChecker : IAccountConnectionChecker
{
    private const int MaximumPages = 3;
    private readonly IMailReader _mail;
    private readonly ICalendarReader _calendar;
    private readonly ILogger<ReadAccessConnectionChecker> _logger;
    private readonly TimeSpan _timeout;

    /// <summary>Creates independent, bounded mail and calendar checks with injectable readers.</summary>
    public ReadAccessConnectionChecker(IMailReader mail, ICalendarReader calendar,
        ILogger<ReadAccessConnectionChecker> logger, TimeSpan? timeout = null)
    {
        if (mail.ProviderId != calendar.ProviderId)
            throw new ArgumentException("Read check providers must match.");
        _mail = mail;
        _calendar = calendar;
        _logger = logger;
        _timeout = timeout ?? TimeSpan.FromSeconds(25);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromSeconds(25))
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    /// <inheritdoc />
    public string ProviderId => _mail.ProviderId;

    /// <inheritdoc />
    public async Task<AccountConnectionCheck> CheckAsync(Account account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        cancellationToken.ThrowIfCancellationRequested();
        if (account.Provider != ProviderId)
            throw new ArgumentException("The account does not belong to this reader.", nameof(account));
        if (!account.MailReadEnabled && !account.CalendarReadEnabled)
            throw new ProviderReadException("No read service is enabled.", ReadFailureKind.LocalConfiguration);

        using var scope = ReadDiagnostics.Begin(_logger, account, "check_connections");
        // One capability's failure or deadline must not erase the other's successful result.
        var mailTask = CheckCapabilityAsync(account.MailReadEnabled, "mail", token => CheckMailAsync(account, token), cancellationToken);
        var calendarTask = CheckCapabilityAsync(account.CalendarReadEnabled, "calendar", token => CheckCalendarsAsync(account, token), cancellationToken);
        await Task.WhenAll(mailTask, calendarTask);
        var mail = await mailTask;
        var calendar = await calendarTask;
        cancellationToken.ThrowIfCancellationRequested();
        return new AccountConnectionCheck(account.Id,
            mail.Failure is null && calendar.Failure is null,
            mail.Failure ?? calendar.Failure,
            account.MailReadEnabled ? mail.Failure is null : null, mail.Failure,
            account.CalendarReadEnabled ? calendar.Failure is null : null, calendar.Failure,
            mail.Evidence, calendar.Evidence);
    }

    private async Task<CapabilityResult> CheckCapabilityAsync(bool enabled, string capability,
        Func<CancellationToken, Task<ReadCheckEvidence>> action, CancellationToken cancellationToken)
    {
        if (!enabled)
            return new(ReadCheckEvidence.None, null);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["Capability"] = capability });
        try
        {
            var evidence = await action(deadline.Token).WaitAsync(deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            _logger.LogInformation("Read check {Capability} finished: evidence={CheckEvidence}", capability, evidence);
            return new(evidence, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReadDiagnostics.Failure(_logger, exception, "read_check");
            var failure = exception switch
            {
                ProviderReadException provider => provider.Kind,
                ProviderAuthenticationException => ReadFailureKind.SignInRequired,
                OperationCanceledException => ReadFailureKind.Timeout,
                HttpRequestException => ReadFailureKind.Network,
                _ => ReadFailureKind.Unknown
            };
            _logger.LogWarning("Read check {Capability} failed: category={FailureCategory}", capability, failure);
            return new(ReadCheckEvidence.None, failure);
        }
    }

    private async Task<ReadCheckEvidence> CheckMailAsync(Account account, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var queries = new[]
        {
            new ProviderMailQuery(string.Empty, null, null, null),
            new ProviderMailQuery(string.Empty, null, now.AddDays(-7), now, UnreadOnly: true),
            new ProviderMailQuery("agentinbox", null, now.AddDays(-7), now)
        };
        var detailRead = false;
        foreach (var query in queries)
        {
            string? cursor = null;
            var completed = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var pageIndex = 0; pageIndex < MaximumPages; pageIndex++)
            {
                var page = await _mail.SearchAsync(account, query, 1, cursor, cancellationToken);
                if (page.Items.FirstOrDefault() is { } item)
                {
                    if (!detailRead)
                    {
                        await _mail.ReadAsync(account, item.ProviderMessageId, cancellationToken);
                        detailRead = true;
                    }
                    completed = true;
                    break;
                }
                cursor = page.NextCursor;
                if (string.IsNullOrEmpty(cursor))
                {
                    completed = true;
                    break;
                }
                if (!seen.Add(cursor))
                    break;
            }
            if (!completed)
                throw new ProviderReadException("The mail sample could not be checked within the page limit.", ReadFailureKind.ResultLimit);
        }
        return detailRead ? ReadCheckEvidence.SampleRead : ReadCheckEvidence.SearchOnly;
    }

    private async Task<ReadCheckEvidence> CheckCalendarsAsync(Account account, CancellationToken cancellationToken)
    {
        var calendars = await _calendar.ListCalendarsAsync(account, cancellationToken);
        if (calendars.Count == 0)
            throw new ProviderReadException("No readable calendar is available.", ReadFailureKind.ItemUnavailable);

        var checkedCount = 0;
        var sampleCount = 0;
        var now = DateTimeOffset.UtcNow;
        try
        {
            foreach (var calendar in calendars)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? cursor = null;
                var completed = false;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var pageIndex = 0; pageIndex < MaximumPages; pageIndex++)
                {
                    var page = await _calendar.SearchEventsAsync(account, calendar.ProviderCalendarId,
                        now.AddDays(-7), now.AddDays(30), 1, cursor, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (page.Events.FirstOrDefault() is { } item)
                    {
                        await _calendar.ReadEventAsync(account, calendar.ProviderCalendarId, item.ProviderEventId, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        sampleCount++;
                        completed = true;
                        break;
                    }
                    cursor = page.NextCursor;
                    if (string.IsNullOrEmpty(cursor))
                    {
                        completed = true;
                        break;
                    }
                    if (!seen.Add(cursor))
                        break;
                }
                if (!completed)
                    throw new ProviderReadException("The calendar sample could not be checked within the page limit.", ReadFailureKind.ResultLimit);
                checkedCount++;
            }
            return sampleCount == calendars.Count ? ReadCheckEvidence.SampleRead : ReadCheckEvidence.SearchOnly;
        }
        finally
        {
            _logger.LogInformation(
                "Calendar check coverage: discovered={CalendarCount}; checked={CalendarCheckedCount}; detailSamples={CalendarSampleCount}; allCalendarsChecked={AllCalendarsChecked}",
                calendars.Count, checkedCount, sampleCount, checkedCount == calendars.Count && !cancellationToken.IsCancellationRequested);
        }
    }

    private sealed record CapabilityResult(ReadCheckEvidence Evidence, ReadFailureKind? Failure);
}
