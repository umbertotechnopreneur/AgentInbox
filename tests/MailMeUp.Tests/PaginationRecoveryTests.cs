using MailMeUp.Application;
using MailMeUp.Core;
using Xunit;

namespace MailMeUp.Tests;

public sealed class PaginationRecoveryTests
{
    private static readonly Account Google = new("google:synthetic", "google", "Google sample", "google@example.test", true, true);
    private static readonly Account Microsoft = new("microsoft:synthetic", "microsoft", "Microsoft sample", "microsoft@example.test", true, true);

    [Fact]
    public async Task MailFollowsShortAndEmptyPagesBeforeMergingOtherAccounts()
    {
        var google = new MailReader("google", (_, _, cursor, _) => Task.FromResult(cursor switch
        {
            null => new ProviderMailSearchPage([Mail("g12", 12)], "empty"),
            "empty" => new ProviderMailSearchPage([], "tail"),
            "tail" => new ProviderMailSearchPage([Mail("g11", 11), Mail("g9", 9)], null),
            _ => throw new InvalidOperationException("Unexpected synthetic cursor.")
        }));
        var microsoft = FixedMail("microsoft", Mail("m10", 10), Mail("m8", 8));
        var application = Create(new MemoryAccounts(Google, Microsoft), [google, microsoft]);

        var first = await application.SearchMailAsync(new("sample", Limit: 3));
        var second = await application.SearchMailAsync(new("sample", Limit: 3, Cursor: first.NextCursor));

        Assert.Equal(["g12", "g11", "m10"], first.Items.Select(item => item.Subject));
        Assert.Equal(["g9", "m8"], second.Items.Select(item => item.Subject));
        Assert.True(first.CoverageComplete);
        Assert.True(second.CoverageComplete);
        Assert.Null(second.NextCursor);
        Assert.Equal(3, google.Calls.Count);
    }

    [Fact]
    public async Task CalendarFollowsShortAndEmptyPagesBeforeMergingOtherAccounts()
    {
        var google = new CalendarReader("google", (_, _, cursor, _) => Task.FromResult(cursor switch
        {
            null => new ProviderEventSearchPage([Event("g8", 8)], "empty"),
            "empty" => new ProviderEventSearchPage([], "tail"),
            "tail" => new ProviderEventSearchPage([Event("g9", 9)], null),
            _ => throw new InvalidOperationException("Unexpected synthetic cursor.")
        }));
        var microsoft = FixedCalendar("microsoft", Event("m10", 10), Event("m11", 11));
        var application = Create(new MemoryAccounts(Google, Microsoft), calendars: [google, microsoft]);

        var first = await application.SearchEventsAsync(Agenda(limit: 2));
        var second = await application.SearchEventsAsync(Agenda(limit: 2, cursor: first.NextCursor));

        Assert.Equal(["g8", "g9"], first.Events.Select(item => item.Title));
        Assert.Equal(["m10", "m11"], second.Events.Select(item => item.Title));
        Assert.True(first.CoverageComplete);
        Assert.True(second.CoverageComplete);
        Assert.Null(second.NextCursor);
        Assert.Equal(3, google.Calls.Count);
    }

    [Fact]
    public async Task MailRefillsExistingShortBufferBeforeSelectingTheNextGlobalPage()
    {
        var google = new MailReader("google", (_, _, cursor, _) => Task.FromResult(cursor is null
            ? new ProviderMailSearchPage([Mail("g12", 12), Mail("g10", 10)], "tail")
            : new ProviderMailSearchPage([Mail("g9", 9)], null)));
        var application = Create(new MemoryAccounts(Google, Microsoft), [google, FixedMail("microsoft", Mail("m11", 11), Mail("m8", 8))]);

        var first = await application.SearchMailAsync(new("sample", Limit: 2));
        var second = await application.SearchMailAsync(new("sample", Limit: 2, Cursor: first.NextCursor));

        Assert.Equal(["g12", "m11"], first.Items.Select(item => item.Subject));
        Assert.Equal(["g10", "g9"], second.Items.Select(item => item.Subject));
        Assert.Equal(2, google.Calls.Count);
    }

    [Fact]
    public async Task CalendarRefillsExistingShortBufferBeforeSelectingTheNextGlobalPage()
    {
        var google = new CalendarReader("google", (_, _, cursor, _) => Task.FromResult(cursor is null
            ? new ProviderEventSearchPage([Event("g8", 8), Event("g10", 10)], "tail")
            : new ProviderEventSearchPage([Event("g11", 11)], null)));
        var application = Create(new MemoryAccounts(Google, Microsoft), calendars: [google, FixedCalendar("microsoft", Event("m9", 9), Event("m12", 12))]);

        var first = await application.SearchEventsAsync(Agenda(limit: 2));
        var second = await application.SearchEventsAsync(Agenda(limit: 2, cursor: first.NextCursor));

        Assert.Equal(["g8", "m9"], first.Events.Select(item => item.Title));
        Assert.Equal(["g10", "g11"], second.Events.Select(item => item.Title));
        Assert.Equal(2, google.Calls.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ChangingOutputLimitPreservesTheProviderPageSizeAndAllMessages(int continuationLimit)
    {
        var reader = new MailReader("microsoft", (_, limit, cursor, _) =>
        {
            // Model a Graph next link retaining the original $top while its adapter bounds returned items.
            ProviderMailSummary[] items = cursor is null ? [Mail("m12", 12), Mail("m11", 11)] : [Mail("m10", 10), Mail("m9", 9)];
            return Task.FromResult(new ProviderMailSearchPage(items.Take(limit).ToArray(), cursor is null ? "tail" : null));
        });
        var application = Create(new MemoryAccounts(Microsoft), [reader]);
        var page = await application.SearchMailAsync(new("sample", Limit: 2));
        var subjects = page.Items.Select(item => item.Subject).ToList();
        for (var index = 0; page.NextCursor is not null && index < 8; index++)
        {
            page = await application.SearchMailAsync(new("sample", Limit: continuationLimit, Cursor: page.NextCursor));
            subjects.AddRange(page.Items.Select(item => item.Subject));
        }

        Assert.Equal(["m12", "m11", "m10", "m9"], subjects);
        Assert.Null(page.NextCursor);
        Assert.Equal(2, reader.Calls.Count);
        Assert.All(reader.Calls, call => Assert.Equal(2, call.Limit));
    }

    [Fact]
    public async Task MailContinuationCycleAcrossOutputPagesEndsWithPartialCoverage()
    {
        var reader = new MailReader("google", (_, _, cursor, _) => Task.FromResult(cursor switch
        {
            null => new ProviderMailSearchPage([Mail("g12", 12)], "a"),
            "a" => new ProviderMailSearchPage([Mail("g11", 11)], "b"),
            _ => new ProviderMailSearchPage([Mail("g10", 10)], "a")
        }));
        var application = Create(new MemoryAccounts(Google), [reader]);

        var first = await application.SearchMailAsync(new("sample", Limit: 1));
        var second = await application.SearchMailAsync(new("sample", Limit: 1, Cursor: first.NextCursor));
        var third = await application.SearchMailAsync(new("sample", Limit: 1, Cursor: second.NextCursor));

        Assert.True(first.CoverageComplete);
        Assert.True(second.CoverageComplete);
        Assert.False(third.CoverageComplete);
        Assert.Contains("pagination", Assert.Single(third.FailedAccounts).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(third.NextCursor);
        Assert.Equal(3, reader.Calls.Count);
    }

    [Fact]
    public async Task CalendarCycleOnAnEmptyPagePreservesHealthyResultsAndTerminates()
    {
        var google = new CalendarReader("google", (_, _, _, _) => Task.FromResult(new ProviderEventSearchPage([], "repeated")));
        var application = Create(new MemoryAccounts(Google, Microsoft), calendars: [google, FixedCalendar("microsoft", Event("healthy", 10))]);

        var result = await application.SearchEventsAsync(Agenda(limit: 1));

        Assert.Equal("healthy", Assert.Single(result.Events).Title);
        Assert.False(result.CoverageComplete);
        Assert.Equal(Google.Id, Assert.Single(result.FailedAccounts).AccountId);
        Assert.Null(result.NextCursor);
        Assert.Equal(2, google.Calls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndlessDistinctEmptyPagesAreBoundedAndReportedAsPartial(bool calendar)
    {
        var count = 0;
        string Next()
        {
            if (++count > 100)
            {
                throw new ProviderReadException("Synthetic guard against an unbounded test.");
            }

            return $"page-{count}";
        }

        if (calendar)
        {
            var application = Create(new MemoryAccounts(Google, Microsoft), calendars:
            [
                new CalendarReader("google", (_, _, _, _) => Task.FromResult(new ProviderEventSearchPage([], Next()))),
                FixedCalendar("microsoft", Event("healthy", 10))
            ]);
            var result = await application.SearchEventsAsync(Agenda(limit: 1));
            Assert.Equal("healthy", Assert.Single(result.Events).Title);
            Assert.False(result.CoverageComplete);
            Assert.Contains("partial", Assert.Single(result.FailedAccounts).Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Null(result.NextCursor);
        }
        else
        {
            var application = Create(new MemoryAccounts(Google, Microsoft),
            [
                new MailReader("google", (_, _, _, _) => Task.FromResult(new ProviderMailSearchPage([], Next()))),
                FixedMail("microsoft", Mail("healthy", 10))
            ]);
            var result = await application.SearchMailAsync(new("sample", Limit: 1));
            Assert.Equal("healthy", Assert.Single(result.Items).Subject);
            Assert.False(result.CoverageComplete);
            Assert.Contains("partial", Assert.Single(result.FailedAccounts).Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Null(result.NextCursor);
        }

        Assert.InRange(count, 1, 64);
    }

    [Fact]
    public async Task RemovedAccountCannotReturnBufferedMailFromAnExistingCursor()
    {
        var accounts = new MemoryAccounts(Google, Microsoft);
        var application = Create(accounts,
        [
            FixedMail("google", Mail("g12", 12), Mail("g10", 10)),
            FixedMail("microsoft", Mail("m11", 11), Mail("m9", 9))
        ]);
        var first = await application.SearchMailAsync(new("sample", Limit: 2));
        Assert.NotNull(first.NextCursor);
        await accounts.DeleteAsync(Google.Id);

        var second = await application.SearchMailAsync(new("sample", Limit: 2, Cursor: first.NextCursor));

        Assert.Equal("m9", Assert.Single(second.Items).Subject);
        Assert.False(second.CoverageComplete);
        Assert.Equal(Google.Id, Assert.Single(second.FailedAccounts).AccountId);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task RemovedAccountCannotReturnBufferedCalendarEventsFromAnExistingCursor()
    {
        var accounts = new MemoryAccounts(Google, Microsoft);
        var application = Create(accounts, calendars:
        [
            FixedCalendar("google", Event("g8", 8), Event("g10", 10)),
            FixedCalendar("microsoft", Event("m9", 9), Event("m11", 11))
        ]);
        var first = await application.SearchEventsAsync(Agenda(limit: 2));
        Assert.NotNull(first.NextCursor);
        await accounts.DeleteAsync(Google.Id);

        var second = await application.SearchEventsAsync(Agenda(limit: 2, cursor: first.NextCursor));

        Assert.Equal("m11", Assert.Single(second.Events).Title);
        Assert.False(second.CoverageComplete);
        Assert.Equal(Google.Id, Assert.Single(second.FailedAccounts).AccountId);
        Assert.Null(second.NextCursor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderCancellationIsSanitizedAsTimeoutAndOtherAccountsStillComplete(bool calendar)
    {
        const string privateDiagnostic = "synthetic provider response that must not reach the result";
        if (calendar)
        {
            var application = Create(new MemoryAccounts(Google, Microsoft), calendars:
            [
                new CalendarReader("google", (_, _, _, _) => Task.FromException<ProviderEventSearchPage>(new TaskCanceledException(privateDiagnostic))),
                FixedCalendar("microsoft", Event("healthy", 10))
            ]);
            var result = await application.SearchEventsAsync(Agenda(limit: 1));
            Assert.Equal("healthy", Assert.Single(result.Events).Title);
            Assert.False(result.CoverageComplete);
            Assert.Contains("timed out", Assert.Single(result.FailedAccounts).Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(privateDiagnostic, result.FailedAccounts[0].Reason, StringComparison.Ordinal);
        }
        else
        {
            var application = Create(new MemoryAccounts(Google, Microsoft),
            [
                new MailReader("google", (_, _, _, _) => Task.FromException<ProviderMailSearchPage>(new TaskCanceledException(privateDiagnostic))),
                FixedMail("microsoft", Mail("healthy", 10))
            ]);
            var result = await application.SearchMailAsync(new("sample", Limit: 1));
            Assert.Equal("healthy", Assert.Single(result.Items).Subject);
            Assert.False(result.CoverageComplete);
            Assert.Contains("timed out", Assert.Single(result.FailedAccounts).Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(privateDiagnostic, result.FailedAccounts[0].Reason, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCallerCancellationPropagatesInsteadOfBecomingPartialCoverage(bool calendar)
    {
        using var caller = new CancellationTokenSource();
        if (calendar)
        {
            var microsoft = FixedCalendar("microsoft", Event("unreached", 10));
            var application = Create(new MemoryAccounts(Google, Microsoft), calendars:
            [
                new CalendarReader("google", (_, _, _, token) =>
                {
                    caller.Cancel();
                    return Task.FromCanceled<ProviderEventSearchPage>(token);
                }),
                microsoft
            ]);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => application.SearchEventsAsync(Agenda(limit: 1), caller.Token));
            Assert.Empty(microsoft.Calls);
        }
        else
        {
            var microsoft = FixedMail("microsoft", Mail("unreached", 10));
            var application = Create(new MemoryAccounts(Google, Microsoft),
            [
                new MailReader("google", (_, _, _, token) =>
                {
                    caller.Cancel();
                    return Task.FromCanceled<ProviderMailSearchPage>(token);
                }),
                microsoft
            ]);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => application.SearchMailAsync(new("sample", Limit: 1), caller.Token));
            Assert.Empty(microsoft.Calls);
        }
    }

    [Fact]
    public async Task DifferentLocalReferencesToTheSameCalendarProduceOneSearchTarget()
    {
        var reader = FixedCalendar("google", Event("one-event", 10));
        var application = Create(new MemoryAccounts(Google), calendars: [reader]);
        var firstReference = Assert.Single((await application.ListCalendarsAsync(new())).Calendars).Reference;
        var secondReference = Assert.Single((await application.ListCalendarsAsync(new())).Calendars).Reference;
        Assert.NotEqual(firstReference, secondReference);

        var result = await application.SearchEventsAsync(Agenda() with { CalendarReferences = [firstReference, secondReference] });

        Assert.Equal("one-event", Assert.Single(result.Events).Title);
        Assert.True(result.CoverageComplete);
        Assert.Single(reader.Calls);
    }

    private static MailMeUpApplication Create(MemoryAccounts accounts, IReadOnlyList<IMailReader>? mail = null, IReadOnlyList<ICalendarReader>? calendars = null) =>
        new(accounts, [], [], [], mail ?? [], calendars ?? [], timeProvider: FixedTimeProvider.September2026);

    private static EventSearchRequest Agenda(int limit = 20, string? cursor = null) =>
        new("2026-09-05T00:00:00Z", "2026-09-06T00:00:00Z", Limit: limit, Cursor: cursor);

    private static DateTimeOffset Instant(int hour) => new(2026, 9, 5, hour, 0, 0, TimeSpan.Zero);
    private static ProviderMailSummary Mail(string id, int hour) => new(id, id, "sender@example.test", Instant(hour), "Synthetic preview");
    private static ProviderEventSummary Event(string id, int hour) => new(id, Instant(hour), id, Instant(hour).ToString("O"), Instant(hour).AddMinutes(30).ToString("O"), false, false, string.Empty);
    private static MailReader FixedMail(string provider, params ProviderMailSummary[] items) => new(provider, (_, _, _, _) => Task.FromResult(new ProviderMailSearchPage(items, null)));
    private static CalendarReader FixedCalendar(string provider, params ProviderEventSummary[] events) => new(provider, (_, _, _, _) => Task.FromResult(new ProviderEventSearchPage(events, null)));

    private sealed record ReadCall(int Limit, string? Cursor);

    private sealed class MailReader(string providerId, Func<Account, int, string?, CancellationToken, Task<ProviderMailSearchPage>> search) : IMailReader
    {
        public string ProviderId { get; } = providerId;
        public List<ReadCall> Calls { get; } = [];

        public Task<ProviderMailSearchPage> SearchAsync(Account account, ProviderMailQuery query, int limit, string? cursor, CancellationToken cancellationToken = default)
        {
            Calls.Add(new(limit, cursor));
            return search(account, limit, cursor, cancellationToken);
        }

        public Task<ProviderMailMessage> ReadAsync(Account account, string providerMessageId, CancellationToken cancellationToken = default) =>
            Task.FromException<ProviderMailMessage>(new InvalidOperationException("This synthetic test does not read message bodies."));
    }

    private sealed class CalendarReader(string providerId, Func<Account, int, string?, CancellationToken, Task<ProviderEventSearchPage>> search) : ICalendarReader
    {
        public string ProviderId { get; } = providerId;
        public List<ReadCall> Calls { get; } = [];

        public Task<IReadOnlyList<ProviderCalendar>> ListCalendarsAsync(Account account, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderCalendar>>([new(account.Id + "-calendar", "Synthetic calendar", true, "UTC")]);

        public Task<ProviderEventSearchPage> SearchEventsAsync(Account account, string providerCalendarId, DateTimeOffset start, DateTimeOffset end, int limit, string? cursor, CancellationToken cancellationToken = default)
        {
            Calls.Add(new(limit, cursor));
            return search(account, limit, cursor, cancellationToken);
        }

        public Task<ProviderEvent> ReadEventAsync(Account account, string providerCalendarId, string providerEventId, CancellationToken cancellationToken = default) =>
            Task.FromException<ProviderEvent>(new InvalidOperationException("This synthetic test does not read event details."));
    }

    private sealed class MemoryAccounts(params Account[] accounts) : IAccountStore
    {
        private readonly List<Account> _accounts = [.. accounts];

        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Account>>(_accounts.ToArray());

        public Task SaveAsync(Account account, CancellationToken cancellationToken = default)
        {
            _accounts.RemoveAll(item => item.Id == account.Id);
            _accounts.Add(account);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default) => Task.FromResult(_accounts.RemoveAll(account => account.Id == accountId) != 0);
    }
}
