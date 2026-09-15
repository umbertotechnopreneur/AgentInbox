using MailMeUp.Application;
using MailMeUp.Core;
using MailMeUp.Mcp;
using Xunit;

namespace MailMeUp.Tests;

public sealed class ApplicationReadTests
{
    [Fact]
    public async Task MailSearchMergesAccountsAndContinuesFromAnOpaqueCursor()
    {
        var googleAccount = new Account("google:test", "google", "Google", "google@example.test", true, false);
        var microsoftAccount = new Account("microsoft:test", "microsoft", "Microsoft", "microsoft@example.test", true, false);
        var google = new FakeMailReader(
            "google",
            new ProviderMailSearchPage(
            [
                new("g-new", "Newest", "sender@example.test", Instant(10), "Newest preview"),
                new("g-old", "Oldest", "sender@example.test", Instant(8), "Oldest preview")
            ],
            NextCursor: null),
            new ProviderMailMessage(
                "g-new",
                "Newest",
                "sender@example.test",
                ["google@example.test"],
                [],
                Instant(10),
                "body from google"));
        var microsoft = new FakeMailReader(
            "microsoft",
            new ProviderMailSearchPage(
                [new("m-middle", "Middle", "sender@example.test", Instant(9), "Middle preview")],
                NextCursor: null),
            new ProviderMailMessage(
                "m-middle",
                "Middle",
                "sender@example.test",
                ["microsoft@example.test"],
                [],
                Instant(9),
                "body from microsoft"));
        var application = CreateApplication([googleAccount, microsoftAccount], [google, microsoft], []);

        var first = await application.SearchMailAsync(new(
            "quarterly plan",
            Limit: 2,
            Sender: "sender@example.test",
            Start: "2026-09-05T00:00:00Z",
            End: "2026-09-06T00:00:00Z"));

        Assert.Equal(["Newest", "Middle"], first.Items.Select(item => item.Subject));
        Assert.All(first.Items, item => Assert.StartsWith("m_", item.Reference, StringComparison.Ordinal));
        Assert.True(first.CoverageComplete);
        Assert.NotNull(first.NextCursor);
        Assert.StartsWith("c_", first.NextCursor, StringComparison.Ordinal);
        Assert.Equal("sender@example.test", google.LastQuery?.Sender);
        Assert.Equal(Instant(0), google.LastQuery?.Start);
        Assert.Equal(Instant(24), google.LastQuery?.End);

        var message = await application.ReadMailAsync(new(first.Items[0].Reference, MaxCharacters: 4));
        Assert.Equal("body", message.Text);
        Assert.True(message.MoreAvailable);
        Assert.Equal(googleAccount.Id, message.AccountId);

        var second = await application.SearchMailAsync(new(
            "quarterly plan",
            Limit: 2,
            Cursor: first.NextCursor,
            Sender: "sender@example.test",
            Start: "2026-09-05T00:00:00Z",
            End: "2026-09-06T00:00:00Z"));
        Assert.Equal("Oldest", Assert.Single(second.Items).Subject);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task MailSearchReportsPartialCoverageWithoutDiscardingOtherAccounts()
    {
        var accounts = new[]
        {
            new Account("google:test", "google", "Google", "google@example.test", true, false),
            new Account("microsoft:test", "microsoft", "Microsoft", "microsoft@example.test", true, false)
        };
        var google = new FakeMailReader("google", failSearch: true);
        var microsoft = new FakeMailReader(
            "microsoft",
            new ProviderMailSearchPage(
                [new("m-1", "Available", "sender@example.test", Instant(11), "Preview")],
                NextCursor: null));
        var application = CreateApplication(accounts, [google, microsoft], []);

        var result = await application.SearchMailAsync(new("sample"));

        Assert.False(result.CoverageComplete);
        Assert.Equal("Available", Assert.Single(result.Items).Subject);
        Assert.Equal("google:test", Assert.Single(result.FailedAccounts).AccountId);
    }

    [Fact]
    public async Task StructuredMailFiltersSelectUnreadMessagesWithMatchingRecipientsAndAttachments()
    {
        var account = new Account("google:test", "google", "Google", "google@example.test", true, false);
        var google = new FakeMailReader(
            "google",
            new ProviderMailSearchPage(
            [
                new("match", "Match", "Acme Alerts <alerts@example.test>", Instant(10), "Match preview",
                    IsRead: false, HasAttachments: true, Recipients: ["Team <team@example.test>"]),
                new("read", "Read", "Acme Alerts <alerts@example.test>", Instant(11), "Read preview",
                    IsRead: true, HasAttachments: true, Recipients: ["Team <team@example.test>"]),
                new("no-attachment", "No attachment", "Acme Alerts <alerts@example.test>", Instant(12), "No attachment preview",
                    IsRead: false, HasAttachments: false, Recipients: ["Team <team@example.test>"]),
                new("wrong-recipient", "Wrong recipient", "Acme Alerts <alerts@example.test>", Instant(13), "Wrong recipient preview",
                    IsRead: false, HasAttachments: true, Recipients: ["Other <other@example.test>"])
            ],
            NextCursor: null));
        var application = CreateApplication([account], [google], []);

        var result = await application.SearchMailAsync(new(
            Query: null,
            Sender: "acme",
            Start: "2026-09-05T00:00:00Z",
            End: "2026-09-06T00:00:00Z",
            RecipientContains: "team@example.test",
            UnreadOnly: true,
            HasAttachments: true));

        var item = Assert.Single(result.Items);
        Assert.Equal("Match", item.Subject);
        Assert.False(item.IsRead);
        Assert.True(item.HasAttachments);
        Assert.Equal("acme", google.LastQuery?.Sender);
        Assert.Equal("team@example.test", google.LastQuery?.RecipientContains);
        Assert.True(google.LastQuery?.UnreadOnly == true);
        Assert.True(google.LastQuery?.HasAttachments == true);
    }

    [Fact]
    public async Task InboxOnlyIsAValidStructuredSearchAndUsesTheDefaultDateWindow()
    {
        var account = new Account("google:test", "google", "Google", "google@example.test", true, false);
        var reader = new FakeMailReader("google");
        var application = CreateApplication([account], [reader], []);

        var result = await application.SearchMailAsync(new(InboxOnly: true));

        Assert.True(result.InboxOnly);
        Assert.True(reader.LastQuery?.InboxOnly == true);
        Assert.NotNull(result.EffectiveStart);
        Assert.NotNull(result.EffectiveEnd);
        Assert.Equal(14, result.DefaultLookbackDaysApplied);
        Assert.Equal(result.EffectiveStart, reader.LastQuery?.Start);
        Assert.Equal(result.EffectiveEnd, reader.LastQuery?.End);
        Assert.True(result.CoverageComplete);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MailContinuationRejectsAChangedFolderScope(bool inboxOnly)
    {
        var account = new Account("google:test", "google", "Google", "google@example.test", true, false);
        var reader = new FakeMailReader("google", new ProviderMailSearchPage(
        [
            new("new", "Newest", "sender@example.test", Instant(10), "Preview", IsRead: false),
            new("old", "Older", "sender@example.test", Instant(9), "Preview", IsRead: false)
        ], null));
        var application = CreateApplication([account], [reader], []);
        var request = new MailSearchRequest(UnreadOnly: true, InboxOnly: inboxOnly, Limit: 1,
            Start: "2026-09-05T00:00:00Z", End: "2026-09-06T00:00:00Z");

        var first = await application.SearchMailAsync(request);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(inboxOnly, first.InboxOnly);
        var originalQuery = reader.LastQuery;

        await Assert.ThrowsAsync<ArgumentException>(() => application.SearchMailAsync(
            request with { Cursor = first.NextCursor, InboxOnly = !inboxOnly }));
        Assert.Same(originalQuery, reader.LastQuery);

        var continued = await application.SearchMailAsync(request with { Cursor = first.NextCursor });
        Assert.Equal(inboxOnly, continued.InboxOnly);
        Assert.Equal("Older", Assert.Single(continued.Items).Subject);
        Assert.Null(continued.NextCursor);
    }

    [Theory]
    [InlineData("unread", true)]
    [InlineData("unread-all-folders", false)]
    [InlineData("search", false)]
    [InlineData("search-inbox", true)]
    [InlineData("date", false)]
    [InlineData("date-inbox", true)]
    public async Task MailToolsApplyAndReportTheRequestedInboxScope(string tool, bool expectedInboxOnly)
    {
        var account = new Account("google:test", "google", "Google", "google@example.test", true, false);
        var reader = new FakeMailReader("google", new ProviderMailSearchPage(
            [new("match", "Match", "sender@example.test", Instant(10), "Preview", IsRead: false)], null));
        var tools = new MailTools(CreateApplication([account], [reader], []));
        const string start = "2026-09-05T00:00:00Z";
        const string end = "2026-09-06T00:00:00Z";

        var response = tool switch
        {
            "unread" => await tools.SearchUnreadMailAsync(start: start, end: end),
            "unread-all-folders" => await tools.SearchUnreadMailAsync(start: start, end: end, inboxOnly: false),
            "search" => await tools.SearchMailAsync("sample", start: start, end: end),
            "search-inbox" => await tools.SearchMailAsync("sample", start: start, end: end, inboxOnly: true),
            "date" => await tools.SearchMailByDateAsync(start, end),
            "date-inbox" => await tools.SearchMailByDateAsync(start, end, inboxOnly: true),
            _ => throw new ArgumentException("Unknown synthetic tool case.", nameof(tool))
        };

        Assert.False(response.IsError == true);
        Assert.Equal(expectedInboxOnly, reader.LastQuery?.InboxOnly);
        Assert.Equal(tool.StartsWith("unread", StringComparison.Ordinal), reader.LastQuery?.UnreadOnly);
        Assert.Equal(Instant(0), reader.LastQuery?.Start);
        Assert.Equal(Instant(24), reader.LastQuery?.End);
        var payload = Assert.IsType<System.Text.Json.JsonElement>(response.StructuredContent);
        Assert.Equal(expectedInboxOnly, payload.GetProperty("inbox_only").GetBoolean());
        Assert.Equal(1, payload.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task CalendarResultsStayBoundToTheirAccountAndUseBoundedDetails()
    {
        var googleAccount = new Account("google:test", "google", "Google", "google@example.test", false, true);
        var microsoftAccount = new Account("microsoft:test", "microsoft", "Microsoft", "microsoft@example.test", false, true);
        var google = new FakeCalendarReader(
            "google",
            [new("g-cal", "Google calendar", true, "UTC")],
            new ProviderEventSearchPage(
                [new("g-event", Instant(10), "Later", "2026-09-05T10:00:00Z", "2026-09-05T11:00:00Z", false, false, "Room G")],
                NextCursor: null),
            new ProviderEvent(
                "g-event",
                "Later",
                "2026-09-05T10:00:00Z",
                "2026-09-05T11:00:00Z",
                false,
                false,
                "Room G",
                "abcdef",
                ["Guest <guest@example.test>"],
                "https://meet.example.test/google"));
        var microsoft = new FakeCalendarReader(
            "microsoft",
            [new("m-cal", "Microsoft calendar", true, "UTC")],
            new ProviderEventSearchPage(
                [new("m-event", Instant(9), "Earlier", "2026-09-05T09:00:00Z", "2026-09-05T09:30:00Z", false, false, "Room M")],
                NextCursor: null),
            new ProviderEvent(
                "m-event",
                "Earlier",
                "2026-09-05T09:00:00Z",
                "2026-09-05T09:30:00Z",
                false,
                false,
                "Room M",
                "notes",
                [],
                null));
        var application = CreateApplication([googleAccount, microsoftAccount], [], [google, microsoft]);

        var calendars = await application.ListCalendarsAsync(new());
        Assert.True(calendars.CoverageComplete);
        Assert.Equal(2, calendars.Calendars.Count);
        Assert.All(calendars.Calendars, item => Assert.StartsWith("cal_", item.Reference, StringComparison.Ordinal));

        var agenda = await application.SearchEventsAsync(new(
            "2026-09-05T00:00:00Z",
            "2026-09-06T00:00:00Z",
            calendars.Calendars.Select(item => item.Reference).ToArray()));
        Assert.Equal(["Earlier", "Later"], agenda.Events.Select(item => item.Title));
        Assert.True(agenda.CoverageComplete);
        Assert.All(agenda.Events, item => Assert.StartsWith("evt_", item.Reference, StringComparison.Ordinal));

        var details = await application.ReadEventAsync(new(
            agenda.Events.Single(item => item.Title == "Later").Reference,
            MaxDescriptionCharacters: 4));
        Assert.Equal("abcd", details.Description);
        Assert.True(details.DescriptionTruncated);
        Assert.Equal(googleAccount.Id, details.AccountId);
        Assert.Equal("https://meet.example.test/google", details.MeetingLink);

        var limited = await application.SearchEventsAsync(new(
            "2026-09-05T00:00:00Z",
            "2026-09-06T00:00:00Z",
            calendars.Calendars.Select(item => item.Reference).ToArray(),
            Limit: 1));
        Assert.NotNull(limited.NextCursor);
        await Assert.ThrowsAsync<ArgumentException>(() => application.SearchEventsAsync(new(
            "2026-09-05T00:00:00Z",
            "2026-09-06T00:00:00Z",
            [calendars.Calendars[0].Reference],
            Limit: 1,
            Cursor: limited.NextCursor)));
    }

    private static MailMeUpApplication CreateApplication(
        IReadOnlyList<Account> accounts,
        IReadOnlyList<IMailReader> mailReaders,
        IReadOnlyList<ICalendarReader> calendarReaders) => new(
            new MemoryAccountStore(accounts),
            [],
            [],
            [],
            mailReaders,
            calendarReaders,
            timeProvider: FixedTimeProvider.September2026);

    private static DateTimeOffset Instant(int hour) =>
        new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero).AddHours(hour);

    private sealed class MemoryAccountStore(IReadOnlyList<Account> initialAccounts) : IAccountStore
    {
        private readonly List<Account> _accounts = [.. initialAccounts];

        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Account>>(_accounts.ToArray());

        public Task SaveAsync(Account account, CancellationToken cancellationToken = default)
        {
            _accounts.RemoveAll(item => item.Id == account.Id);
            _accounts.Add(account);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_accounts.RemoveAll(item => item.Id == accountId) == 1);
    }

    private sealed class FakeMailReader : IMailReader
    {
        private readonly ProviderMailSearchPage _page;
        private readonly ProviderMailMessage? _message;
        private readonly bool _failSearch;

        public FakeMailReader(
            string providerId,
            ProviderMailSearchPage? page = null,
            ProviderMailMessage? message = null,
            bool failSearch = false)
        {
            ProviderId = providerId;
            _page = page ?? new ProviderMailSearchPage([], null);
            _message = message;
            _failSearch = failSearch;
        }

        public string ProviderId { get; }
        public ProviderMailQuery? LastQuery { get; private set; }

        public Task<ProviderMailSearchPage> SearchAsync(
            Account account,
            ProviderMailQuery query,
            int limit,
            string? cursor,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return _failSearch
                ? Task.FromException<ProviderMailSearchPage>(new ProviderReadException("Synthetic failure."))
                : Task.FromResult(_page);
        }

        public Task<ProviderMailMessage> ReadAsync(
            Account account,
            string providerMessageId,
            CancellationToken cancellationToken = default) =>
            _message is not null && _message.ProviderMessageId == providerMessageId
                ? Task.FromResult(_message)
                : Task.FromException<ProviderMailMessage>(new ProviderReadException("Synthetic message missing."));
    }

    private sealed class FakeCalendarReader(
        string providerId,
        IReadOnlyList<ProviderCalendar> calendars,
        ProviderEventSearchPage page,
        ProviderEvent selectedEvent) : ICalendarReader
    {
        public string ProviderId { get; } = providerId;

        public Task<IReadOnlyList<ProviderCalendar>> ListCalendarsAsync(
            Account account,
            CancellationToken cancellationToken = default) => Task.FromResult(calendars);

        public Task<ProviderEventSearchPage> SearchEventsAsync(
            Account account,
            string providerCalendarId,
            DateTimeOffset start,
            DateTimeOffset end,
            int limit,
            string? cursor,
            CancellationToken cancellationToken = default) => Task.FromResult(page);

        public Task<ProviderEvent> ReadEventAsync(
            Account account,
            string providerCalendarId,
            string providerEventId,
            CancellationToken cancellationToken = default) =>
            providerEventId == selectedEvent.ProviderEventId
                ? Task.FromResult(selectedEvent)
                : Task.FromException<ProviderEvent>(new ProviderReadException("Synthetic event missing."));
    }
}
