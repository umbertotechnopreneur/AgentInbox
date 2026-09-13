using System.Globalization;
using MailMeUp.Application;
using MailMeUp.Core;
using Xunit;

namespace MailMeUp.Tests;

public sealed class MailSearchWorkBudgetTests
{
    private static readonly DateTimeOffset Recent = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GlobalLimitHydratesSmallPagesAndRefillsOnlyTheNewestExhaustedAccount()
    {
        var accounts = Enumerable.Range(0, 4).Select(index => Account(index.ToString(CultureInfo.InvariantCulture))).ToArray();
        var reader = new Reader((account, limit, cursor) =>
        {
            var index = int.Parse(account.Id, CultureInfo.InvariantCulture);
            return Page(account.Id, limit, cursor, 100, Recent.AddDays(-index));
        });
        var application = Create(accounts, reader);

        var result = await application.SearchMailAsync(new("sample", Limit: 20));

        Assert.Equal(Enumerable.Range(0, 20).Select(index => $"0-{index}"), result.Items.Select(item => item.Subject));
        Assert.True(result.CoverageComplete);
        Assert.NotNull(result.NextCursor);
        Assert.Equal(5, reader.Calls.Count);
        Assert.Equal(2, reader.Calls.Count(call => call.AccountId == "0"));
        Assert.All(reader.Calls, call => Assert.Equal(10, call.Limit));
        Assert.Equal(50, reader.Calls.Sum(call => call.Limit));
    }

    [Fact]
    public async Task BufferedResultsNeedNoProviderRefillWhenTheyFillTheNextOutputPage()
    {
        var reader = new Reader((_, limit, cursor) => Page("a", limit, cursor, 40, Recent));
        var application = Create([Account("a")], reader);
        var first = await application.SearchMailAsync(new("sample", Limit: 25));
        Assert.Equal(3, reader.Calls.Count);

        var second = await application.SearchMailAsync(new("sample", Limit: 5, Cursor: first.NextCursor));

        Assert.Equal(Enumerable.Range(25, 5).Select(index => $"a-{index}"), second.Items.Select(item => item.Subject));
        Assert.Equal(3, reader.Calls.Count);
        Assert.NotNull(second.NextCursor);
        var third = await application.SearchMailAsync(new("sample", Limit: 20, Cursor: second.NextCursor));
        Assert.Equal(10, third.Items.Count);
        Assert.All(reader.Calls, call => Assert.Equal(10, call.Limit));
        Assert.Null(third.NextCursor);
    }

    [Fact]
    public async Task BudgetPauseOnTheFirstPageKeepsAResumableStartingPosition()
    {
        var paused = true;
        var reader = new Reader((_, limit, cursor) => paused
            ? throw new ProviderReadException("Synthetic request budget reached.", ReadFailureKind.BudgetExceeded)
            : Page("a", limit, cursor, 2, Recent));
        var application = Create([Account("a")], reader);

        var pausedPage = await application.SearchMailAsync(new("sample", Limit: 2));

        Assert.Empty(pausedPage.Items);
        Assert.Equal(ReadFailureKind.BudgetExceeded, Assert.Single(pausedPage.FailedAccounts).Kind);
        Assert.False(pausedPage.CoverageComplete);
        Assert.NotNull(pausedPage.NextCursor);
        Assert.Single(reader.Calls);
        paused = false;

        var resumed = await application.SearchMailAsync(new("sample", Limit: 2, Cursor: pausedPage.NextCursor));

        Assert.Equal(["a-0", "a-1"], resumed.Items.Select(item => item.Subject));
        Assert.True(resumed.CoverageComplete);
        Assert.Empty(resumed.FailedAccounts);
        Assert.Null(resumed.NextCursor);
        Assert.All(reader.Calls, call => Assert.Null(call.Cursor));
    }

    [Fact]
    public async Task BudgetPauseKeepsSuccessfulResultsAndRetriesOnlyTheUnfinishedProviderCursor()
    {
        var paused = true;
        var reader = new Reader((_, _, cursor) => cursor switch
        {
            null => new([Mail("first", Recent)], "middle"),
            "middle" => new([Mail("second", Recent.AddMinutes(-1))], "tail"),
            "tail" when paused => throw new ProviderReadException("Synthetic request budget reached.", ReadFailureKind.BudgetExceeded),
            "tail" => new([Mail("third", Recent.AddMinutes(-2))], null),
            _ => throw new InvalidOperationException("Unexpected synthetic cursor.")
        });
        var application = Create([Account("a")], reader);

        var first = await application.SearchMailAsync(new("sample", Limit: 3));

        Assert.Equal(["first", "second"], first.Items.Select(item => item.Subject));
        Assert.Equal(ReadFailureKind.BudgetExceeded, Assert.Single(first.FailedAccounts).Kind);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(3, reader.Calls.Count);
        paused = false;

        var resumed = await application.SearchMailAsync(new("sample", Limit: 3, Cursor: first.NextCursor));

        Assert.Equal("third", Assert.Single(resumed.Items).Subject);
        Assert.True(resumed.CoverageComplete);
        Assert.Null(resumed.NextCursor);
        Assert.Equal(new string?[] { null, "middle", "tail", "tail" }, reader.Calls.Select(call => call.Cursor));
    }

    [Fact]
    public async Task AFailedAccountIsAttemptedOnlyOncePerCallWhileHealthyBuffersRemainAvailable()
    {
        var reader = new Reader((account, limit, cursor) => account.Id == "paused"
            ? throw new ProviderReadException("Synthetic request budget reached.", ReadFailureKind.BudgetExceeded)
            : Page("healthy", limit, cursor, 20, Recent));
        var application = Create([Account("paused"), Account("healthy")], reader);

        var first = await application.SearchMailAsync(new("sample", Limit: 15));
        var second = await application.SearchMailAsync(new("sample", Limit: 5, Cursor: first.NextCursor));

        Assert.Equal(15, first.Items.Count);
        Assert.Equal(5, second.Items.Count);
        Assert.Equal(2, reader.Calls.Count(call => call.AccountId == "paused"));
        Assert.Equal(2, reader.Calls.Count(call => call.AccountId == "healthy"));
        Assert.NotNull(second.NextCursor);
        Assert.Equal(ReadFailureKind.BudgetExceeded, Assert.Single(second.FailedAccounts).Kind);
    }

    [Fact]
    public async Task ProviderPermissionFailuresRemainStoppedOnLaterContinuations()
    {
        var reader = new Reader((account, limit, cursor) => account.Id == "denied"
            ? throw new ProviderReadException("Synthetic permission failure.", ReadFailureKind.AccessDenied)
            : Page("healthy", limit, cursor, 20, Recent));
        var application = Create([Account("denied"), Account("healthy")], reader);
        var first = await application.SearchMailAsync(new("sample", Limit: 15));

        var second = await application.SearchMailAsync(new("sample", Limit: 5, Cursor: first.NextCursor));

        Assert.Equal(1, reader.Calls.Count(call => call.AccountId == "denied"));
        Assert.Equal(ReadFailureKind.AccessDenied, Assert.Single(second.FailedAccounts).Kind);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task ProviderPageCeilingIsSharedAcrossAllRefillsWithinOneSearch()
    {
        var count = 0;
        var reader = new Reader((_, _, _) =>
        {
            count++;
            return new([Mail("item-" + count, Recent.AddMinutes(-count))], "cursor-" + count);
        });
        var application = Create([Account("a")], reader);

        var result = await application.SearchMailAsync(new("sample", Limit: 50));

        Assert.Equal(20, count);
        Assert.Equal(20, result.Items.Count);
        Assert.Equal(ReadFailureKind.ResultLimit, Assert.Single(result.FailedAccounts).Kind);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task SharingChangesDuringALaterRefillDiscardAllAccumulatedResults()
    {
        var sharing = new SharingStore();
        var reader = new Reader((_, limit, cursor) =>
        {
            if (cursor is not null) sharing.Enabled = false;
            return Page("a", limit, cursor, 20, Recent);
        });
        var application = Create([Account("a")], reader, sharing: sharing);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => application.SearchMailAsync(new("sample", Limit: 11)));

        Assert.Contains("Account access changed", failure.Message, StringComparison.Ordinal);
        Assert.Equal(2, reader.Calls.Count);
    }

    [Fact]
    public async Task PreferenceChangesDuringALaterRefillDiscardAllAccumulatedResults()
    {
        var preferences = new PreferenceStore();
        var reader = new Reader((_, limit, cursor) =>
        {
            if (cursor is not null) preferences.Days = 7;
            return Page("a", limit, cursor, 20, Recent);
        });
        var application = Create([Account("a")], reader, preferences: preferences);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => application.SearchMailAsync(new("sample", Limit: 11)));

        Assert.Contains("period changed", failure.Message, StringComparison.Ordinal);
        Assert.Equal(2, reader.Calls.Count);
    }

    private static Account Account(string id) => new(id, "google", "Synthetic " + id, id + "@example.test", true);

    private static ProviderMailSummary Mail(string id, DateTimeOffset receivedAt) =>
        new(id, id, "sender@example.test", receivedAt, "Synthetic preview");

    private static ProviderMailSearchPage Page(string accountId, int limit, string? cursor, int total, DateTimeOffset newest)
    {
        var offset = cursor is null ? 0 : int.Parse(cursor, CultureInfo.InvariantCulture);
        var count = Math.Min(limit, total - offset);
        return new(Enumerable.Range(offset, count).Select(index => Mail(accountId + "-" + index, newest.AddMinutes(-index))).ToArray(),
            offset + count < total ? (offset + count).ToString(CultureInfo.InvariantCulture) : null);
    }

    private static MailMeUpApplication Create(IReadOnlyList<Account> accounts, IMailReader reader,
        IAccountSharingStore? sharing = null, IMailSearchPreferencesStore? preferences = null) =>
        new(new Accounts(accounts), [], [], [], [reader], [], sharingStore: sharing,
            mailSearchPreferencesStore: preferences, timeProvider: FixedTimeProvider.September2026);

    private sealed record ReadCall(string AccountId, int Limit, string? Cursor);

    private sealed class Reader(Func<Account, int, string?, ProviderMailSearchPage> read) : IMailReader
    {
        public string ProviderId => "google";
        public List<ReadCall> Calls { get; } = [];

        public Task<ProviderMailSearchPage> SearchAsync(Account account, ProviderMailQuery query, int limit, string? cursor,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new(account.Id, limit, cursor));
            return Task.FromResult(read(account, limit, cursor));
        }

        public Task<ProviderMailMessage> ReadAsync(Account account, string providerMessageId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test never reads message bodies.");
    }

    private sealed class Accounts(IReadOnlyList<Account> accounts) : IAccountStore
    {
        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(accounts);
        public Task SaveAsync(Account account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SharingStore : IAccountSharingStore
    {
        public bool Enabled { get; set; } = true;
        public Task<AccountSharingSettings?> GetAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountSharingSettings?>(new(accountId, Enabled: Enabled));
        public Task SaveAsync(AccountSharingSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PreferenceStore : IMailSearchPreferencesStore
    {
        public int Days { get; set; } = 14;
        public Task<MailSearchPreferences> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new MailSearchPreferences(Days));
        public Task SaveAsync(MailSearchPreferences preferences, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
