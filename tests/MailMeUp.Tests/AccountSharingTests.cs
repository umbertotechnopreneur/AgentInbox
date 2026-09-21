using System.Text.Json;
using MailMeUp.Application;
using MailMeUp.Core;
using MailMeUp.Storage;
using Xunit;

namespace MailMeUp.Tests;

public sealed class AccountSharingTests : IDisposable
{
    private static readonly Account Sample = new("google:sample", "google", "Sample", "sample@example.test", true, true);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AgentInbox.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LegacyAccountKeepsConsentUntilAnExplicitSharingChoice()
    {
        var accountStore = new MemoryAccountStore([Sample]);
        var application = CreateApplication(accountStore);

        Assert.Equal(Sample, Assert.Single(await application.ListSharedAccountsAsync()));
        Assert.True(Assert.Single(await application.ListAccountSharingAsync()).Enabled);
        Assert.False(Directory.Exists(_directory));

        await application.SaveAccountSharingAsync(new(Sample.Id, ShareMail: false));
        var shared = Assert.Single(await application.ListSharedAccountsAsync());
        Assert.False(shared.MailReadEnabled);
        Assert.True(shared.CalendarReadEnabled);
        Assert.True(Assert.Single(await application.ListAccountsAsync()).MailReadEnabled);
    }

    [Fact]
    public async Task OnboardingDeniesNewAccountBeforeItsMetadataIsVisible()
    {
        var sharing = new JsonAccountSharingStore(_directory);
        var accountStore = new MemoryAccountStore([])
        {
            BeforeSave = async account => Assert.False((await sharing.GetAsync(account.Id))!.Enabled)
        };
        var application = CreateApplication(accountStore, connector: new FakeConnector());

        await application.ConnectAccountAsync("google", new(ShareWithAssistant: false));

        Assert.Single(await application.ListAccountsAsync());
        Assert.Empty(await application.ListSharedAccountsAsync());
        var consent = Assert.Single(await application.ListAccountsAsync());
        Assert.True(consent.MailReadEnabled);
        Assert.True(consent.CalendarReadEnabled);
    }

    [Fact]
    public async Task AnotherStoreInstanceSeesNewChoicesWithoutRestartAndPreservesOtherAccounts()
    {
        var writer = new JsonAccountSharingStore(_directory);
        var reader = new JsonAccountSharingStore(_directory);
        Assert.Null(await reader.GetAsync(Sample.Id));
        await writer.SaveAsync(new(Sample.Id, CalendarIds: ["work", "personal", "work"]));
        await writer.SaveAsync(new("google:other", Enabled: false));

        Assert.Equal(["personal", "work"], (await reader.GetAsync(Sample.Id))!.CalendarIds);
        await writer.SaveAsync(new(Sample.Id, Enabled: false));
        Assert.False((await reader.GetAsync(Sample.Id))!.Enabled);
        Assert.False((await reader.GetAsync("google:other"))!.Enabled);
    }

    [Fact]
    public async Task IncompletePersistedSettingsFailClosed()
    {
        var sharing = new JsonAccountSharingStore(_directory);
        await sharing.SaveAsync(new(Sample.Id));
        var path = Assert.Single(Directory.GetFiles(Path.Combine(_directory, "sharing"), "*.json"));
        await File.WriteAllTextAsync(path, """
            {"schema_version":1,"settings":{"account_id":"google:sample","share_mail":true,"share_calendars":true,"calendar_ids":null}}
            """);

        await Assert.ThrowsAsync<JsonException>(() => CreateApplication(new MemoryAccountStore([Sample])).ListSharedAccountsAsync());
    }

    [Fact]
    public async Task HiddenAccountCannotUseMailReferencesOrBufferedCursorAndIsOmittedFromCoverage()
    {
        var reader = new FakeMailReader();
        var application = CreateApplication(new MemoryAccountStore([Sample]), mail: reader);
        var first = await application.SearchMailAsync(new("sample", Limit: 1));
        var reference = Assert.Single(first.Items).Reference;
        Assert.NotNull(first.NextCursor);

        await new JsonAccountSharingStore(_directory).SaveAsync(new(Sample.Id, Enabled: false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => application.ReadMailAsync(new(reference)));
        var continued = await application.SearchMailAsync(new("sample", Limit: 1, Cursor: first.NextCursor));
        Assert.Empty(continued.Items);
        Assert.Empty(continued.SearchedAccountIds);
        Assert.Empty(continued.FailedAccounts);
        Assert.Null(continued.NextCursor);
        Assert.Equal(1, reader.SearchCalls);
        Assert.Equal(0, reader.ReadCalls);
    }

    [Fact]
    public async Task CalendarSelectionAppliesToListsDefaultAgendaAndExistingEventReferences()
    {
        var reader = new FakeCalendarReader();
        var application = CreateApplication(new MemoryAccountStore([Sample]), calendar: reader);
        var calendars = await application.ListCalendarsAsync(new());
        var privateCalendar = calendars.Calendars.Single(calendar => calendar.Name == "Private");
        var first = await application.SearchEventsAsync(new(
            "2026-09-06T00:00:00Z", "2026-09-07T00:00:00Z", [privateCalendar.Reference], Limit: 1));
        var eventReference = Assert.Single(first.Events).Reference;

        await new JsonAccountSharingStore(_directory).SaveAsync(new(Sample.Id, CalendarIds: ["public"]));

        Assert.Equal("Public", Assert.Single((await application.ListCalendarsAsync(new())).Calendars).Name);
        Assert.Equal(2, (await application.ListAvailableCalendarsAsync(Sample.Id)).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => application.ReadEventAsync(new(eventReference)));
        var continued = await application.SearchEventsAsync(new(
            "2026-09-06T00:00:00Z", "2026-09-07T00:00:00Z", [privateCalendar.Reference], Limit: 1, Cursor: first.NextCursor));
        Assert.Empty(continued.Events);
        Assert.Null(continued.NextCursor);
        Assert.Equal(0, reader.ReadCalls);

        await application.SearchEventsAsync(new("2026-09-06T00:00:00Z", "2026-09-07T00:00:00Z"));
        Assert.Equal("public", reader.LastCalendarId);
    }

    [Fact]
    public async Task LocalCalendarDiscoveryWorksWhileAssistantAccessIsDisabled()
    {
        var reader = new FakeCalendarReader();
        var application = CreateApplication(new MemoryAccountStore([Sample]), calendar: reader);
        await application.SaveAccountSharingAsync(new(Sample.Id, false, false, false, []));

        Assert.Empty((await application.ListCalendarsAsync(new())).Calendars);
        Assert.Equal(0, reader.ListCalls);
        Assert.Equal(2, (await application.ListAvailableCalendarsAsync(Sample.Id)).Count);
        Assert.Equal(1, reader.ListCalls);
        Assert.Empty(await application.ListSharedAccountsAsync());
    }

    [Fact]
    public async Task RevocationDuringMailSearchDiscardsTheInFlightResponse()
    {
        var reader = new FakeMailReader
        {
            DuringSearch = () => new JsonAccountSharingStore(_directory).SaveAsync(new(Sample.Id, Enabled: false))
        };
        var application = CreateApplication(new MemoryAccountStore([Sample]), mail: reader);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => application.SearchMailAsync(new("sample")));
        Assert.Contains("Account access changed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevocationDuringEventReadDiscardsTheInFlightResponse()
    {
        var reader = new FakeCalendarReader();
        var application = CreateApplication(new MemoryAccountStore([Sample]), calendar: reader);
        var result = await application.SearchEventsAsync(new("2026-09-06T00:00:00Z", "2026-09-07T00:00:00Z", Limit: 1));
        reader.DuringRead = () => new JsonAccountSharingStore(_directory).SaveAsync(new(Sample.Id, ShareCalendars: false));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => application.ReadEventAsync(new(Assert.Single(result.Events).Reference)));
        Assert.Contains("Account access changed", error.Message, StringComparison.Ordinal);
    }

    private MailMeUpApplication CreateApplication(IAccountStore accounts, IMailReader? mail = null, ICalendarReader? calendar = null, IAccountConnector? connector = null) =>
        new(accounts, [], [], connector is null ? [] : [connector], mail is null ? [] : [mail], calendar is null ? [] : [calendar], new JsonAccountSharingStore(_directory), timeProvider: FixedTimeProvider.September2026);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class MemoryAccountStore(IEnumerable<Account> accounts) : IAccountStore
    {
        private readonly Dictionary<string, Account> _accounts = accounts.ToDictionary(account => account.Id, StringComparer.Ordinal);
        public Func<Account, Task>? BeforeSave { get; init; }

        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Account>>(_accounts.Values.OrderBy(account => account.Id, StringComparer.Ordinal).ToArray());

        public async Task SaveAsync(Account account, CancellationToken cancellationToken = default)
        {
            if (BeforeSave is not null)
            {
                await BeforeSave(account);
            }
            _accounts[account.Id] = account;
        }

        public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default) => Task.FromResult(_accounts.Remove(accountId));
    }

    private sealed class FakeConnector : IAccountConnector
    {
        public string ProviderId => "google";
        public Task<AccountConnectionResult> ConnectAsync(AccountConnectionOptions options, CancellationToken cancellationToken = default) => Task.FromResult(new AccountConnectionResult(Sample));
        public Task DisconnectAsync(Account account, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMailReader : IMailReader
    {
        public string ProviderId => "google";
        public int SearchCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public Func<Task>? DuringSearch { get; init; }

        public async Task<ProviderMailSearchPage> SearchAsync(Account account, ProviderMailQuery query, int limit, string? cursor, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            if (DuringSearch is not null)
            {
                await DuringSearch();
            }
            return new([
                new("new", "Sample newer", "sender@example.test", DateTimeOffset.Parse("2026-09-06T10:00:00Z"), "Synthetic preview"),
                new("old", "Sample older", "sender@example.test", DateTimeOffset.Parse("2026-09-06T09:00:00Z"), "Synthetic preview")
            ], null);
        }

        public Task<ProviderMailMessage> ReadAsync(Account account, string providerMessageId, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(new ProviderMailMessage(providerMessageId, "Sample", "sender@example.test", [Sample.EmailAddress], [], DateTimeOffset.Parse("2026-09-06T10:00:00Z"), "Synthetic body"));
        }
    }

    private sealed class FakeCalendarReader : ICalendarReader
    {
        public string ProviderId => "google";
        public int ListCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public string? LastCalendarId { get; private set; }
        public Func<Task>? DuringRead { get; set; }

        public Task<IReadOnlyList<ProviderCalendar>> ListCalendarsAsync(Account account, CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<ProviderCalendar>>([new("private", "Private", true, "UTC"), new("public", "Public", false, "UTC")]);
        }

        public Task<ProviderEventSearchPage> SearchEventsAsync(Account account, string providerCalendarId, DateTimeOffset start, DateTimeOffset end, int limit, string? cursor, CancellationToken cancellationToken = default)
        {
            LastCalendarId = providerCalendarId;
            return Task.FromResult(new ProviderEventSearchPage([
                new("first", start.AddHours(9), "First sample", "2026-09-06T09:00:00Z", "2026-09-06T10:00:00Z", false, false, "Room"),
                new("second", start.AddHours(10), "Second sample", "2026-09-06T10:00:00Z", "2026-09-06T11:00:00Z", false, false, "Room")
            ], null));
        }

        public async Task<ProviderEvent> ReadEventAsync(Account account, string providerCalendarId, string providerEventId, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            if (DuringRead is not null)
            {
                await DuringRead();
            }
            return new(providerEventId, "Sample", "2026-09-06T09:00:00Z", "2026-09-06T10:00:00Z", false, false, "Room", "Synthetic details", ["guest@example.test"], null);
        }
    }
}
