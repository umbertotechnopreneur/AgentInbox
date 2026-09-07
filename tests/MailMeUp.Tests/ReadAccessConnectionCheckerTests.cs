using MailMeUp.Application;
using MailMeUp.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailMeUp.Tests;

public sealed class ReadAccessConnectionCheckerTests
{
    private static readonly Account Account = new("google:synthetic", "google", "Sample", "one@example.test", true, true);

    [Fact]
    public async Task UsesRealSearchModesAndDetailsInsteadOfAnAuthenticationProbe()
    {
        var mail = new MailReader();
        var calendar = new CalendarReader();
        var result = await Create(mail, calendar).CheckAsync(Account);
        Assert.True(result.SampleChecksPassed);
        Assert.Equal(3, mail.Queries.Count);
        Assert.Contains(mail.Queries, query => query.UnreadOnly && query.Start is not null && query.End is not null);
        Assert.Contains(mail.Queries, query => query.Text.Length > 0 && query.Start is not null);
        Assert.Equal(1, mail.DetailReads);
        Assert.Equal(1, calendar.DetailReads);
    }

    [Theory]
    [InlineData(ReadFailureKind.Unknown)]
    [InlineData(ReadFailureKind.SignInRequired)]
    [InlineData(ReadFailureKind.InvalidRequest)]
    public async Task DetailFailureCannotBeReportedAsSuccessAndCalendarStillRuns(ReadFailureKind kind)
    {
        var result = await Create(new MailReader { DetailFailure = new ProviderReadException("private detail", kind) }, new CalendarReader()).CheckAsync(Account);
        Assert.True(result.HasFailures);
        Assert.False(result.SampleChecksPassed);
        Assert.False(result.MailReachable);
        Assert.Equal(kind, result.MailFailureKind);
        Assert.True(result.CalendarReachable);
        Assert.Equal(ReadCheckEvidence.SampleRead, result.CalendarEvidence);
    }

    [Fact]
    public async Task EmptySearchIsIncompleteRatherThanGreenOrReconnectRequired()
    {
        var result = await Create(new MailReader { Empty = true }, new CalendarReader()).CheckAsync(Account);
        Assert.False(result.HasFailures);
        Assert.False(result.SampleChecksPassed);
        Assert.Equal(ReadCheckEvidence.SearchOnly, result.MailEvidence);
        Assert.Null(result.MailFailureKind);
    }

    [Fact]
    public async Task EmptyCalendarWindowDoesNotClaimDetailVerification()
    {
        var result = await Create(new MailReader(), new CalendarReader { Empty = true }).CheckAsync(Account);
        Assert.False(result.HasFailures);
        Assert.False(result.SampleChecksPassed);
        Assert.Equal(ReadCheckEvidence.SearchOnly, result.CalendarEvidence);
    }

    [Fact]
    public async Task MoreThanFiveCalendarsAreActuallyChecked()
    {
        var calendar = new CalendarReader { CalendarCount = 6 };
        var result = await Create(new MailReader(), calendar).CheckAsync(Account);

        Assert.True(result.SampleChecksPassed);
        Assert.Null(result.CalendarFailureKind);
        Assert.Equal(6, calendar.SearchCalls);
        Assert.Equal(6, calendar.DetailReads);
    }

    [Fact]
    public async Task FailureOnSixthCalendarIsNotSilentlySkipped()
    {
        var calendar = new CalendarReader { CalendarCount = 6, FailureCalendarId = "calendar-5" };
        var result = await Create(new MailReader(), calendar).CheckAsync(Account);

        Assert.False(result.CalendarReachable);
        Assert.Equal(ReadFailureKind.AccessDenied, result.CalendarFailureKind);
        Assert.False(result.SampleChecksPassed);
        Assert.True(result.MailReachable);
        Assert.Equal(6, calendar.SearchCalls);
        Assert.Equal(5, calendar.DetailReads);
    }

    [Fact]
    public async Task MissingSampleOnSixthCalendarRemainsSearchOnly()
    {
        var calendar = new CalendarReader { CalendarCount = 6, EmptyCalendarId = "calendar-5" };
        var result = await Create(new MailReader(), calendar).CheckAsync(Account);

        Assert.False(result.HasFailures);
        Assert.False(result.SampleChecksPassed);
        Assert.Equal(ReadCheckEvidence.SearchOnly, result.CalendarEvidence);
        Assert.Equal(6, calendar.SearchCalls);
        Assert.Equal(5, calendar.DetailReads);
    }

    [Fact]
    public async Task EmptyCalendarPageCanContinueToAReadableSample()
    {
        var calendar = new CalendarReader { EmptyFirstPage = true };
        var result = await Create(new MailReader(), calendar).CheckAsync(Account);

        Assert.True(result.SampleChecksPassed);
        Assert.Equal(2, calendar.SearchCalls);
        Assert.Equal(1, calendar.DetailReads);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public async Task RepeatedOrExhaustedCalendarContinuationRemainsIncomplete(bool distinctCursors, int expectedCalls)
    {
        var calendar = new CalendarReader { Empty = true, KeepPaging = true, DistinctCursors = distinctCursors };
        var result = await Create(new MailReader(), calendar).CheckAsync(Account);

        Assert.Equal(ReadFailureKind.ResultLimit, result.CalendarFailureKind);
        Assert.False(result.SampleChecksPassed);
        Assert.Equal(expectedCalls, calendar.SearchCalls);
        Assert.Equal(0, calendar.DetailReads);
    }

    [Fact]
    public async Task CalendarDeadlineStillBoundsChecksBeyondTheFormerCalendarLimit()
    {
        var calendar = new CalendarReader { CalendarCount = 6, StallCalendarId = "calendar-5" };
        var result = await Create(new MailReader(), calendar, TimeSpan.FromMilliseconds(50)).CheckAsync(Account);

        Assert.Equal(ReadFailureKind.Timeout, result.CalendarFailureKind);
        Assert.False(result.SampleChecksPassed);
        Assert.True(result.MailReachable);
        Assert.Equal(6, calendar.SearchCalls);
        Assert.Equal(5, calendar.DetailReads);
    }

    [Fact]
    public async Task RepeatedEmptyMailContinuationIsIncompleteNotSuccess()
    {
        var result = await Create(new MailReader { Empty = true, Cursor = "synthetic-next" }, new CalendarReader()).CheckAsync(Account);
        Assert.False(result.SampleChecksPassed);
        Assert.Equal(ReadFailureKind.ResultLimit, result.MailFailureKind);
    }

    [Fact]
    public async Task UnexpectedExceptionFailsClosed()
    {
        var result = await Create(new MailReader { DetailFailure = new InvalidOperationException("private") }, new CalendarReader()).CheckAsync(Account);
        Assert.True(result.HasFailures);
        Assert.Equal(ReadFailureKind.Unknown, result.MailFailureKind);
    }

    [Fact]
    public async Task CapabilityTimeoutDoesNotEraseOtherCapabilitySuccess()
    {
        var result = await Create(new MailReader { Stall = true }, new CalendarReader(), TimeSpan.FromMilliseconds(50)).CheckAsync(Account);
        Assert.Equal(ReadFailureKind.Timeout, result.MailFailureKind);
        Assert.True(result.CalendarReachable);
        Assert.False(result.SampleChecksPassed);
    }

    [Fact]
    public async Task CallerCancellationIsNeverConvertedToSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(new MailReader(), new CalendarReader()).CheckAsync(Account, cancellation.Token));
    }

    [Fact]
    public void ContradictoryAndEmptyResultsCannotBeGreen()
    {
        Assert.True(new AccountConnectionCheck("synthetic", true).HasFailures);
        Assert.False(new AccountConnectionCheck("synthetic", true, MailReachable: false,
            MailEvidence: ReadCheckEvidence.SampleRead).SampleChecksPassed);
        Assert.False(new AccountConnectionCheck("synthetic", true, ReadFailureKind.Unknown,
            MailReachable: true, MailEvidence: ReadCheckEvidence.SampleRead).SampleChecksPassed);
    }

    private static ReadAccessConnectionChecker Create(MailReader mail, CalendarReader calendar, TimeSpan? timeout = null) =>
        new(mail, calendar, NullLogger<ReadAccessConnectionChecker>.Instance, timeout);

    private sealed class MailReader : IMailReader
    {
        public string ProviderId => "google";
        public bool Empty { get; init; }
        public bool Stall { get; init; }
        public string? Cursor { get; init; }
        public Exception? DetailFailure { get; init; }
        public List<ProviderMailQuery> Queries { get; } = [];
        public int DetailReads { get; private set; }
        public async Task<ProviderMailSearchPage> SearchAsync(Account account, ProviderMailQuery query, int limit, string? cursor, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            Assert.Equal(1, limit);
            if (Stall)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            return new(Empty ? [] : [new("message", "Sample", "sender@example.test", DateTimeOffset.UtcNow, "")], Cursor);
        }
        public Task<ProviderMailMessage> ReadAsync(Account account, string providerMessageId, CancellationToken cancellationToken = default)
        {
            DetailReads++;
            return DetailFailure is { } exception ? Task.FromException<ProviderMailMessage>(exception) :
                Task.FromResult(new ProviderMailMessage("message", "Sample", "sender@example.test", [], [], DateTimeOffset.UtcNow, ""));
        }
    }

    private sealed class CalendarReader : ICalendarReader
    {
        public string ProviderId => "google";
        public bool Empty { get; init; }
        public bool EmptyFirstPage { get; init; }
        public bool KeepPaging { get; init; }
        public bool DistinctCursors { get; init; }
        public int CalendarCount { get; init; } = 1;
        public string? FailureCalendarId { get; init; }
        public string? EmptyCalendarId { get; init; }
        public string? StallCalendarId { get; init; }
        public int SearchCalls { get; private set; }
        public int DetailReads { get; private set; }
        public Task<IReadOnlyList<ProviderCalendar>> ListCalendarsAsync(Account account, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderCalendar>>(Enumerable.Range(0, CalendarCount)
                .Select(index => new ProviderCalendar($"calendar-{index}", "Sample", index == 0, null)).ToArray());
        public async Task<ProviderEventSearchPage> SearchEventsAsync(Account account, string providerCalendarId, DateTimeOffset start, DateTimeOffset end, int limit, string? cursor, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            if (providerCalendarId == FailureCalendarId)
                throw new ProviderReadException("Synthetic calendar access failure.", ReadFailureKind.AccessDenied);
            if (providerCalendarId == StallCalendarId)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            var empty = Empty || providerCalendarId == EmptyCalendarId || (EmptyFirstPage && cursor is null);
            var next = KeepPaging ? (DistinctCursors ? $"next-{SearchCalls}" : "next") :
                EmptyFirstPage && cursor is null ? "next" : null;
            return new ProviderEventSearchPage(empty ? [] :
                [new("event", start, "Sample", start.ToString("O"), end.ToString("O"), false, false, "")], next);
        }
        public Task<ProviderEvent> ReadEventAsync(Account account, string providerCalendarId, string providerEventId, CancellationToken cancellationToken = default)
        {
            DetailReads++;
            return Task.FromResult(new ProviderEvent("event", "Sample", "2026-09-07T10:00:00Z", "2026-09-07T11:00:00Z", false, false, "", "", [], null));
        }
    }
}
