using MailMeUp.Application;
using MailMeUp.Core;
using Xunit;

namespace MailMeUp.Tests;

public sealed class BoundedDetailCacheTests
{
    private static readonly Account Sample = new("google:cache", "google", "Cache sample", "cache@example.test", true, true);
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OffsetPagesAndFreshSearchReferencesReuseTheSameFullProviderBody()
    {
        var reader = new MailReader((_, _) => Task.FromResult(Message("abcdefghij")));
        var application = Create(reader);
        var reference = await SearchReference(application);

        var first = await application.ReadMailAsync(new(reference, MaxCharacters: 4));
        var second = await application.ReadMailAsync(new(reference, Offset: 4, MaxCharacters: 4));
        var freshReference = await SearchReference(application);
        var third = await application.ReadMailAsync(new(freshReference, Offset: 8, MaxCharacters: 4));

        Assert.NotEqual(reference, freshReference);
        Assert.Equal("abcd", first.Text);
        Assert.Equal("efgh", second.Text);
        Assert.Equal("ij", third.Text);
        Assert.True(first.MoreAvailable);
        Assert.False(third.MoreAvailable);
        Assert.Equal(1, reader.ReadCalls);
    }

    [Fact]
    public async Task ConcurrentReadsForOneMessageCoalesceBeforeApplyingDifferentOffsets()
    {
        var response = new TaskCompletionSource<ProviderMailMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new MailReader((_, _) => response.Task);
        var application = Create(reader);
        var reference = await SearchReference(application);

        var first = application.ReadMailAsync(new(reference, MaxCharacters: 4));
        var second = application.ReadMailAsync(new(reference, Offset: 4, MaxCharacters: 4));
        Assert.Equal(1, reader.ReadCalls);
        response.SetResult(Message("abcdefgh"));
        var results = await Task.WhenAll(first, second);

        Assert.Equal("abcd", results[0].Text);
        Assert.Equal("efgh", results[1].Text);
        Assert.Equal(1, reader.ReadCalls);
    }

    [Fact]
    public async Task FailedProviderReadDoesNotPoisonTheNextReadOfTheSameReference()
    {
        var reader = new MailReader((call, _) => call == 1
            ? Task.FromException<ProviderMailMessage>(new ProviderReadException("Synthetic failure", ReadFailureKind.RateLimited))
            : Task.FromResult(Message("available now")));
        var application = Create(reader);
        var reference = await SearchReference(application);

        var failure = await Assert.ThrowsAsync<ProviderReadException>(() => application.ReadMailAsync(new(reference)));
        var result = await application.ReadMailAsync(new(reference));

        Assert.Equal(ReadFailureKind.RateLimited, failure.Kind);
        Assert.Equal("available now", result.Text);
        Assert.Equal(2, reader.ReadCalls);
    }

    [Fact]
    public async Task CancelledOwnerCannotPopulateTheCacheWithALateProviderResponse()
    {
        var response = new TaskCompletionSource<ProviderMailMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new MailReader((call, _) => call == 1 ? response.Task : Task.FromResult(Message("fresh response")));
        var application = Create(reader);
        var reference = await SearchReference(application);
        using var cancellation = new CancellationTokenSource();
        var owner = application.ReadMailAsync(new(reference), cancellation.Token);
        var observer = application.ReadMailAsync(new(reference));

        cancellation.Cancel();
        response.SetResult(Message("cancelled response"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner);
        await Assert.ThrowsAsync<ProviderReadException>(() => observer);
        var retry = await application.ReadMailAsync(new(reference));

        Assert.Equal("fresh response", retry.Text);
        Assert.Equal(2, reader.ReadCalls);
    }

    [Fact]
    public async Task CancellingAJoinedCallerDoesNotCancelTheOriginalRead()
    {
        var response = new TaskCompletionSource<ProviderMailMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new MailReader((_, _) => response.Task);
        var application = Create(reader);
        var reference = await SearchReference(application);
        var owner = application.ReadMailAsync(new(reference));
        using var cancellation = new CancellationTokenSource();
        var joined = application.ReadMailAsync(new(reference), cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joined);
        response.SetResult(Message("owner response"));
        Assert.Equal("owner response", (await owner).Text);
        Assert.Equal("owner response", (await application.ReadMailAsync(new(reference))).Text);
        Assert.Equal(1, reader.ReadCalls);
    }

    [Fact]
    public async Task RevokedSharingBlocksAnAlreadyCachedMessageWithoutAnotherProviderRead()
    {
        var reader = new MailReader((_, _) => Task.FromResult(Message("shared body")));
        var sharing = new SharingStore();
        var application = Create(reader, sharing: sharing);
        var reference = await SearchReference(application);
        await application.ReadMailAsync(new(reference));

        await sharing.SaveAsync(new(Sample.Id, Enabled: false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => application.ReadMailAsync(new(reference)));
        Assert.Equal(1, reader.ReadCalls);
    }

    [Fact]
    public async Task AChangedSharingFingerprintDoesNotReuseEarlierCachedContent()
    {
        var reader = new MailReader((call, _) => Task.FromResult(Message(call == 1 ? "old body" : "fresh body")));
        var sharing = new SharingStore();
        var application = Create(reader, sharing: sharing);
        var reference = await SearchReference(application);
        Assert.Equal("old body", (await application.ReadMailAsync(new(reference))).Text);

        await sharing.SaveAsync(new(Sample.Id, ShareCalendars: false));

        Assert.Equal("fresh body", (await application.ReadMailAsync(new(reference))).Text);
        Assert.Equal(2, reader.ReadCalls);
    }

    [Fact]
    public async Task AnExhaustedDetailBudgetRejectsBeforeTheProviderIsCalled()
    {
        var reader = new MailReader((_, _) => Task.FromResult(Message("body")));
        var budget = new InMemoryReadGuardrails(new() { DetailReadsPerWindow = 1 });
        var application = Create(reader, budget: budget);
        var reference = await SearchReference(application);
        await budget.AdmitReadAsync(detail: true);

        var failure = await Assert.ThrowsAsync<ProviderReadException>(() => application.ReadMailAsync(new(reference)));

        Assert.Equal(ReadFailureKind.BudgetExceeded, failure.Kind);
        Assert.Equal(0, reader.ReadCalls);
    }

    [Fact]
    public async Task ACacheHitStillRequiresRemainingDetailBudget()
    {
        var reader = new MailReader((_, _) => Task.FromResult(Message("body")));
        var application = Create(reader, budget: new InMemoryReadGuardrails(new() { DetailReadsPerWindow = 1 }));
        var reference = await SearchReference(application);
        await application.ReadMailAsync(new(reference));

        var failure = await Assert.ThrowsAsync<ProviderReadException>(() => application.ReadMailAsync(new(reference)));

        Assert.Equal(ReadFailureKind.BudgetExceeded, failure.Kind);
        Assert.Equal(1, reader.ReadCalls);
    }

    [Fact]
    public async Task CachedContentExpiresAfterTwoMinutesWithoutSlidingOnHits()
    {
        var clock = new MutableClock();
        var reader = new MailReader((call, _) => Task.FromResult(Message(call == 1 ? "old body" : "new body")));
        var application = Create(reader, clock: clock);
        var reference = await SearchReference(application);
        Assert.Equal("old body", (await application.ReadMailAsync(new(reference))).Text);
        clock.Now += TimeSpan.FromSeconds(119);
        Assert.Equal("old body", (await application.ReadMailAsync(new(reference))).Text);
        clock.Now += TimeSpan.FromSeconds(1);

        Assert.Equal("new body", (await application.ReadMailAsync(new(reference))).Text);
        Assert.Equal(2, reader.ReadCalls);
    }

    [Fact]
    public async Task ABodyLargerThanTheEntryBudgetIsReturnedInSegmentsButNotCached()
    {
        var reader = new MailReader((_, _) => Task.FromResult(Message(new string('x', 128 * 1024 + 1))));
        var application = Create(reader);
        var reference = await SearchReference(application);

        var first = await application.ReadMailAsync(new(reference, MaxCharacters: 100));
        var second = await application.ReadMailAsync(new(reference, Offset: 100, MaxCharacters: 100));

        Assert.Equal(100, first.Text.Length);
        Assert.Equal(100, second.Text.Length);
        Assert.True(first.MoreAvailable);
        Assert.True(second.MoreAvailable);
        Assert.Equal(2, reader.ReadCalls);
    }

    private static MailMeUpApplication Create(MailReader reader, SharingStore? sharing = null, IReadBudget? budget = null,
        TimeProvider? clock = null) => new(new AccountStore(), [], [], [], [reader], [],
            sharingStore: sharing ?? new SharingStore(), timeProvider: clock ?? new MutableClock(), readBudget: budget);

    private static async Task<string> SearchReference(MailMeUpApplication application) =>
        Assert.Single((await application.SearchMailAsync(new("sample"))).Items).Reference;

    private static ProviderMailMessage Message(string text) => new("provider-message", "Sample", "sender@example.test",
        [Sample.EmailAddress], [], ReceivedAt, text);

    private sealed class MailReader(Func<int, CancellationToken, Task<ProviderMailMessage>> read) : IMailReader
    {
        private int _readCalls;
        public string ProviderId => "google";
        public int ReadCalls => Volatile.Read(ref _readCalls);
        public Task<ProviderMailSearchPage> SearchAsync(Account account, ProviderMailQuery query, int limit, string? cursor,
            CancellationToken cancellationToken = default) => Task.FromResult(new ProviderMailSearchPage(
                [new("provider-message", "Sample", "sender@example.test", ReceivedAt, "Sample preview")], null));
        public Task<ProviderMailMessage> ReadAsync(Account account, string providerMessageId, CancellationToken cancellationToken = default) =>
            read(Interlocked.Increment(ref _readCalls), cancellationToken);
    }

    private sealed class AccountStore : IAccountStore
    {
        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Account>>([Sample]);
        public Task SaveAsync(Account account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SharingStore : IAccountSharingStore
    {
        private AccountSharingSettings _settings = new(Sample.Id);
        public Task<AccountSharingSettings?> GetAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountSharingSettings?>(_settings);
        public Task SaveAsync(AccountSharingSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = ReceivedAt + TimeSpan.FromHours(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
