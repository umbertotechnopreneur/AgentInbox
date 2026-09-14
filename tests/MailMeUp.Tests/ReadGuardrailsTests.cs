using MailMeUp.Core;
using Xunit;

namespace MailMeUp.Tests;

public sealed class ReadGuardrailsTests
{
    [Fact]
    public async Task AttemptsShareAProfileLimitButKeepServiceScopesIndependent()
    {
        var clock = new TestClock();
        var guardrails = clock.Create(new()
        {
            ProviderAttemptsPerAccountServicePerMinute = 1,
            ProviderAttemptsPerProfilePerMinute = 2
        });
        await using (await guardrails.AcquireAsync("gmail", "first@example.test", false)) { }
        await using (await guardrails.AcquireAsync("google-calendar", "first@example.test", false)) { }

        var failure = await Assert.ThrowsAsync<ProviderReadException>(() =>
            guardrails.AcquireAsync("microsoft-outlook", "second@example.test", false));

        Assert.Equal(ReadFailureKind.BudgetExceeded, failure.Kind);
        clock.Now += TimeSpan.FromMinutes(1);
        await using (await guardrails.AcquireAsync("microsoft-outlook", "second@example.test", false)) { }
    }

    [Fact]
    public async Task MetadataAndDetailAttemptsUseTheSameSpacingHistory()
    {
        var clock = new TestClock();
        var guardrails = clock.Create();
        await using (await guardrails.AcquireAsync("gmail", "account@example.test", false)) { }
        await using (await guardrails.AcquireAsync("gmail", "account@example.test", false)) { }
        await using (await guardrails.AcquireAsync("gmail", "account@example.test", true)) { }

        Assert.Equal(new[] { TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1) }, clock.Delays);
    }

    [Theory]
    [InlineData(ReadFailureKind.RateLimited)]
    [InlineData(ReadFailureKind.ProviderUnavailable)]
    [InlineData(ReadFailureKind.Timeout)]
    public async Task ProviderCooldownPreservesItsCategoryAndDoesNotPauseOtherServices(ReadFailureKind kind)
    {
        var clock = new TestClock();
        var guardrails = clock.Create();
        await using (var lease = await guardrails.AcquireAsync("gmail", "account@example.test", false))
            await lease.SetCooldownAsync(TimeSpan.FromSeconds(15), kind);

        var failure = await Assert.ThrowsAsync<ProviderReadException>(() =>
            guardrails.AcquireAsync("gmail", "account@example.test", false));
        Assert.Equal(kind, failure.Kind);
        await using (await guardrails.AcquireAsync("google-calendar", "account@example.test", false)) { }
        clock.Now += TimeSpan.FromSeconds(15);
        await using (await guardrails.AcquireAsync("gmail", "account@example.test", false)) { }
    }

    [Fact]
    public async Task VeryLongRetryAfterStaysPausedWithoutOverflowOrCategoryChanges()
    {
        var clock = new TestClock();
        var guardrails = clock.Create();
        await using (var lease = await guardrails.AcquireAsync("gmail", "account@example.test", false))
            await lease.SetCooldownAsync(TimeSpan.MaxValue, ReadFailureKind.RateLimited);

        clock.Now += TimeSpan.FromDays(2);
        var failure = await Assert.ThrowsAsync<ProviderReadException>(() =>
            guardrails.AcquireAsync("gmail", "account@example.test", false));
        Assert.Equal(ReadFailureKind.RateLimited, failure.Kind);
    }

    [Fact]
    public async Task DetailAdmissionsExhaustBeforeTheBroaderContentBudgetAndThenExpire()
    {
        var clock = new TestClock();
        var guardrails = clock.Create(new() { ContentReadsPerWindow = 3, DetailReadsPerWindow = 1 });
        await guardrails.AdmitReadAsync(true);
        var failure = await Assert.ThrowsAsync<ProviderReadException>(() => guardrails.AdmitReadAsync(true));
        Assert.Equal(ReadFailureKind.BudgetExceeded, failure.Kind);
        await guardrails.AdmitReadAsync(false);
        await guardrails.AdmitReadAsync(false);
        await Assert.ThrowsAsync<ProviderReadException>(() => guardrails.AdmitReadAsync(false));

        clock.Now += TimeSpan.FromMinutes(15);
        await guardrails.AdmitReadAsync(true);
    }

    [Fact]
    public async Task OutputChargesCannotExceedTheCumulativeBudgetUnderConcurrentRequests()
    {
        var clock = new TestClock();
        var guardrails = clock.Create(new() { ResponseBytes = 1024, OutputBytesPerWindow = 4096 });
        var accepted = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            try { await guardrails.ChargeOutputAsync(1024); return true; }
            catch (ProviderReadException failure) when (failure.Kind == ReadFailureKind.BudgetExceeded) { return false; }
        }));

        Assert.Equal(4, accepted.Count(value => value));
        await Assert.ThrowsAsync<ProviderReadException>(() => guardrails.AdmitReadAsync(false));
        clock.Now += TimeSpan.FromMinutes(15);
        await guardrails.AdmitReadAsync(false);
        await guardrails.ChargeOutputAsync(1024);
    }

    [Fact]
    public async Task OversizedResponseIsRejectedWithoutConsumingTheWindowBudget()
    {
        var guardrails = new InMemoryReadGuardrails(new() { ResponseBytes = 1024, OutputBytesPerWindow = 1024 });

        await Assert.ThrowsAsync<ProviderReadException>(() => guardrails.ChargeOutputAsync(1025));
        await guardrails.ChargeOutputAsync(1024);
    }

    [Fact]
    public void InvalidLimitsCannotDisableTheGuardrails()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryReadGuardrails(new() { ContentReadsPerWindow = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryReadGuardrails(new() { DetailReadsPerWindow = 121 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryReadGuardrails(new() { ResponseBytes = 1024 * 1024 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryReadGuardrails(new() { MetadataIntervalMilliseconds = 0 }));
    }

    private sealed class TestClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> Delays { get; } = [];
        public InMemoryReadGuardrails Create(ReadGuardrailLimits? limits = null) => new(limits, () => Now, (delay, token) =>
        {
            token.ThrowIfCancellationRequested();
            Delays.Add(delay);
            Now += delay;
            return Task.CompletedTask;
        });
    }
}
