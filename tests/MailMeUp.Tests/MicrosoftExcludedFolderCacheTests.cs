using MailMeUp.Core;
using MailMeUp.Providers.Microsoft;
using Xunit;

namespace MailMeUp.Tests;

public sealed class MicrosoftExcludedFolderCacheTests
{
    [Fact]
    public async Task CachedFoldersAreSeparatedByApplicationAndMailbox()
    {
        var cache = new MicrosoftExcludedFolderCache();
        var reads = 0;
        Task<IReadOnlyList<string>> Read(CancellationToken _) =>
            Task.FromResult<IReadOnlyList<string>>([$"junk-{++reads}", $"deleted-{reads}"]);

        var first = await cache.GetAsync("client-one", "microsoft:one", Read, default);
        Assert.Same(first, await cache.GetAsync("client-one", "microsoft:one", Read, default));
        var anotherAccount = await cache.GetAsync("client-one", "microsoft:two", Read, default);
        var anotherClient = await cache.GetAsync("client-two", "microsoft:one", Read, default);

        Assert.Equal(3, reads);
        Assert.NotEqual(first[0], anotherAccount[0]);
        Assert.NotEqual(first[0], anotherClient[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)first)[0] = "changed");
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneFolderRefresh()
    {
        var cache = new MicrosoftExcludedFolderCache();
        var completion = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        Task<IReadOnlyList<string>> Read(CancellationToken _) { reads++; return completion.Task; }

        var first = cache.GetAsync("client", "microsoft:one", Read, default);
        var second = cache.GetAsync("client", "microsoft:one", Read, default);
        completion.SetResult(["junk", "deleted"]);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, reads);
        Assert.Same(results[0], results[1]);
    }

    [Fact]
    public async Task ExpiredEntryIsNotReturnedWhenRefreshFails()
    {
        var now = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        var cache = new MicrosoftExcludedFolderCache(() => now);
        await cache.GetAsync("client", "microsoft:one", _ => Task.FromResult<IReadOnlyList<string>>(["old-junk", "old-deleted"]), default);
        now += TimeSpan.FromMinutes(5);

        await Assert.ThrowsAsync<ProviderReadException>(() => cache.GetAsync("client", "microsoft:one",
            _ => Task.FromException<IReadOnlyList<string>>(new ProviderReadException("Synthetic folder lookup failed.")), default));
        var refreshed = await cache.GetAsync("client", "microsoft:one",
            _ => Task.FromResult<IReadOnlyList<string>>(["new-junk", "new-deleted"]), default);

        Assert.Equal("new-junk", refreshed[0]);
    }

    [Fact]
    public async Task CapacityEvictsOldCompletedEntries()
    {
        var now = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        var cache = new MicrosoftExcludedFolderCache(() => now, capacity: 2);
        var reads = 0;
        Task<IReadOnlyList<string>> Read(CancellationToken _) { reads++; return Task.FromResult<IReadOnlyList<string>>(["junk", "deleted"]); }

        await cache.GetAsync("client", "microsoft:one", Read, default);
        now += TimeSpan.FromSeconds(1);
        await cache.GetAsync("client", "microsoft:two", Read, default);
        now += TimeSpan.FromSeconds(1);
        await cache.GetAsync("client", "microsoft:three", Read, default);
        await cache.GetAsync("client", "microsoft:one", Read, default);

        Assert.Equal(4, reads);
    }

    [Fact]
    public async Task CancellingAWaitingCallerDoesNotCancelTheSharedRefresh()
    {
        var cache = new MicrosoftExcludedFolderCache();
        var completion = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = cache.GetAsync("client", "microsoft:one", _ => completion.Task, default);
        using var cancellation = new CancellationTokenSource();
        var waiting = cache.GetAsync("client", "microsoft:one", _ => throw new InvalidOperationException("Must use existing refresh."), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        completion.SetResult(["junk", "deleted"]);
        Assert.Equal(2, (await first).Count);
    }
}
