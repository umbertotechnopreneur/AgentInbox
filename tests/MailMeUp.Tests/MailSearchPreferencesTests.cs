using System.Text.Json;
using MailMeUp.Application;
using MailMeUp.Core;
using MailMeUp.Storage;
using Xunit;

namespace MailMeUp.Tests;

public sealed class MailSearchPreferencesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MailMeUp.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UndatedSearchExcludesOldAndFutureMailAndReportsTheDefaultScope()
    {
        var reader = new Reader([
            Mail("old", Now.AddDays(-15)),
            Mail("boundary", Now.AddDays(-14)),
            Mail("recent", Now.AddDays(-1)),
            Mail("future", Now.AddHours(1))
        ]);
        var application = Create(reader);

        var result = await application.SearchMailAsync(new(UnreadOnly: true));

        Assert.Equal(["recent", "boundary"], result.Items.Select(item => item.Subject));
        Assert.Equal(14, result.DefaultLookbackDaysApplied);
        Assert.Equal(Now.AddDays(-14), result.EffectiveStart);
        Assert.Equal(Now, result.EffectiveEnd);
        Assert.Equal(result.EffectiveStart, Assert.Single(reader.Queries).Start);
        Assert.Equal(result.EffectiveEnd, reader.Queries[0].End);
    }

    [Theory]
    [InlineData("2020-01-01T00:00:00Z", null)]
    [InlineData(null, "2021-01-01T00:00:00Z")]
    public async Task EitherExplicitBoundaryKeepsOlderMailSearchable(string? start, string? end)
    {
        var oldDate = new DateTimeOffset(2020, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var reader = new Reader([Mail("old", oldDate)]);

        var result = await Create(reader).SearchMailAsync(new(Start: start, End: end));

        Assert.Single(result.Items);
        Assert.Null(result.DefaultLookbackDaysApplied);
        Assert.Equal(start is null ? null : DateTimeOffset.Parse(start), result.EffectiveStart);
        Assert.Equal(end is null ? null : DateTimeOffset.Parse(end), result.EffectiveEnd);
    }

    [Theory]
    [InlineData("after:2020/01/01")]
    [InlineData("(newer_than:1y OR older_than:2m)")]
    [InlineData("before:1609459200")]
    [InlineData("received:>=2020-01-01")]
    [InlineData("received>=2020-01-01")]
    [InlineData("sent<2021-01-01")]
    [InlineData("received=2020-06-01")]
    [InlineData("sent:2020-01-01..2020-12-31")]
    public async Task ExplicitNativeDateOperatorsOverrideTheDefault(string query)
    {
        var result = await Create(new Reader([])).SearchMailAsync(new(Query: query));

        Assert.Null(result.DefaultLookbackDaysApplied);
        Assert.Null(result.EffectiveStart);
        Assert.Null(result.EffectiveEnd);
    }

    [Theory]
    [InlineData("\"after:2020/01/01\"")]
    [InlineData("subject:after:2020/01/01")]
    [InlineData("subject:\"notes after:2020/01/01\"")]
    [InlineData("notes after:not-a-date")]
    [InlineData("received:")]
    [InlineData("\\after:2020/01/01")]
    public async Task QuotedPhrasesAndNonDateTokensKeepTheDefault(string query)
    {
        var result = await Create(new Reader([])).SearchMailAsync(new(Query: query));

        Assert.Equal(14, result.DefaultLookbackDaysApplied);
        Assert.Equal(Now.AddDays(-14), result.EffectiveStart);
    }

    [Fact]
    public async Task ContinuationKeepsItsOriginalWindowWhenTimeAdvances()
    {
        var clock = new FixedTimeProvider(Now);
        var reader = new Reader([Mail("new", Now.AddDays(-1)), Mail("old", Now.AddDays(-13))]);
        var application = Create(reader, clock);
        var first = await application.SearchMailAsync(new(UnreadOnly: true, Limit: 1));
        clock.UtcNow = Now.AddDays(5);

        var second = await application.SearchMailAsync(new(UnreadOnly: true, Limit: 1, Cursor: first.NextCursor));

        Assert.Equal("old", Assert.Single(second.Items).Subject);
        Assert.Equal(first.EffectiveStart, second.EffectiveStart);
        Assert.Equal(first.EffectiveEnd, second.EffectiveEnd);
        Assert.Single(reader.Queries);
    }

    [Fact]
    public async Task SavedPreferenceIsReloadedAndInvalidatesAnEarlierDefaultCursor()
    {
        var reader = new Reader([Mail("new", Now.AddDays(-1)), Mail("old", Now.AddDays(-13))]);
        var application = Create(reader);
        var first = await application.SearchMailAsync(new(UnreadOnly: true, Limit: 1));

        await new JsonMailSearchPreferencesStore(_directory).SaveAsync(new(7));
        var error = await Assert.ThrowsAsync<ArgumentException>(() => application.SearchMailAsync(
            new(UnreadOnly: true, Limit: 1, Cursor: first.NextCursor)));

        Assert.Contains("period changed", error.Message, StringComparison.Ordinal);
        Assert.Single(reader.Queries);
        var fresh = await application.SearchMailAsync(new(UnreadOnly: true));
        Assert.Equal(7, fresh.DefaultLookbackDaysApplied);
        Assert.Equal(Now.AddDays(-7), fresh.EffectiveStart);
        Assert.Equal("new", Assert.Single(fresh.Items).Subject);
    }

    [Fact]
    public async Task ChangedPreferenceDoesNotInvalidateAnExplicitDateCursor()
    {
        var reader = new Reader([Mail("new", Now.AddDays(-1)), Mail("old", Now.AddDays(-13))]);
        var application = Create(reader);
        var request = new MailSearchRequest(UnreadOnly: true, Limit: 1, Start: "2020-01-01T00:00:00Z");
        var first = await application.SearchMailAsync(request);
        await application.SaveMailSearchPreferencesAsync(new(7));

        var second = await application.SearchMailAsync(request with { Cursor = first.NextCursor });

        Assert.Equal("old", Assert.Single(second.Items).Subject);
        Assert.Null(second.DefaultLookbackDaysApplied);
    }

    [Fact]
    public async Task PreferenceChangedDuringSearchDiscardsItsResults()
    {
        var reader = new Reader([Mail("sample", Now.AddDays(-1))])
        {
            DuringSearch = () => new JsonMailSearchPreferencesStore(_directory).SaveAsync(new(7))
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(reader).SearchMailAsync(new(UnreadOnly: true)));

        Assert.Contains("period changed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingPreferencesDoNotCreateDataAndSavedChoicesCrossStoreInstances()
    {
        var writer = new JsonMailSearchPreferencesStore(_directory);
        var reader = new JsonMailSearchPreferencesStore(_directory);
        Assert.Equal(new MailSearchPreferences(14), await reader.GetAsync());
        Assert.False(Directory.Exists(_directory));

        var application = Create(new Reader([]));
        Assert.Equal(new MailSearchPreferences(30), await application.SaveMailSearchPreferencesAsync(new(30)));
        Assert.Equal(new MailSearchPreferences(30), await reader.GetAsync());
        await writer.SaveAsync(new(90));
        Assert.Equal(new MailSearchPreferences(90), await application.GetMailSearchPreferencesAsync());
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public async Task InvalidPeriodsDoNotReplaceTheSavedPreference(int days)
    {
        var application = Create(new Reader([]));
        await application.SaveMailSearchPreferencesAsync(new(30));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => application.SaveMailSearchPreferencesAsync(new(days)));

        Assert.Equal(new MailSearchPreferences(30), await application.GetMailSearchPreferencesAsync());
    }

    [Theory]
    [InlineData("{\"schema_version\":1}")]
    [InlineData("not json")]
    public async Task CorruptOrIncompletePreferencesDoNotSilentlyBroadenTheSearch(string contents)
    {
        var reader = new Reader([]);
        var application = Create(reader);
        await application.SaveMailSearchPreferencesAsync(new(7));
        await File.WriteAllTextAsync(Path.Combine(_directory, "search-preferences.json"), contents);

        await Assert.ThrowsAsync<JsonException>(() => application.SearchMailAsync(new(UnreadOnly: true)));

        Assert.Empty(reader.Queries);
    }

    private MailMeUpApplication Create(Reader reader, TimeProvider? clock = null) => new(
        new Accounts(), [], [], [], [reader], [],
        mailSearchPreferencesStore: new JsonMailSearchPreferencesStore(_directory),
        timeProvider: clock ?? new FixedTimeProvider(Now));

    private static ProviderMailSummary Mail(string id, DateTimeOffset receivedAt) =>
        new(id, id, "sender@example.test", receivedAt, "Synthetic preview", IsRead: false);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class Accounts : IAccountStore
    {
        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Account>>([new("google:sample", "google", "Sample", "sample@example.test", true)]);

        public Task SaveAsync(Account account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Reader(IReadOnlyList<ProviderMailSummary> items) : IMailReader
    {
        public string ProviderId => "google";
        public List<ProviderMailQuery> Queries { get; } = [];
        public Func<Task>? DuringSearch { get; init; }

        public async Task<ProviderMailSearchPage> SearchAsync(Account account, ProviderMailQuery query, int limit, string? cursor, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            if (DuringSearch is not null)
            {
                await DuringSearch();
            }
            return new(items, null);
        }

        public Task<ProviderMailMessage> ReadAsync(Account account, string providerMessageId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
