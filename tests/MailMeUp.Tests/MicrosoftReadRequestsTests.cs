using System.Net;
using System.Net.Http.Headers;
using MailMeUp.Core;
using MailMeUp.Providers.Microsoft;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailMeUp.Tests;

public sealed class MicrosoftReadRequestsTests
{
    private static readonly Account Account = new("microsoft:synthetic-reader", "microsoft", "Sample", "reader@example.test", true, true);

    [Theory]
    [InlineData(400, ReadFailureKind.InvalidRequest)]
    [InlineData(401, ReadFailureKind.SignInRequired)]
    [InlineData(403, ReadFailureKind.AccessDenied)]
    [InlineData(404, ReadFailureKind.ItemUnavailable)]
    [InlineData(429, ReadFailureKind.RateLimited)]
    [InlineData(503, ReadFailureKind.ProviderUnavailable)]
    public void HttpFailuresKeepSpecificCategories(int status, ReadFailureKind expected) =>
        Assert.Equal(expected, MicrosoftReadRequests.ClassifyStatus(status));

    [Theory]
    [InlineData("graph.mailFolders.get", false)]
    [InlineData("graph.messages.list", false)]
    [InlineData("graph.calendars.list", false)]
    [InlineData("graph.events.list", false)]
    [InlineData("graph.messages.get", true)]
    [InlineData("graph.events.get", true)]
    public async Task EveryEndpointUsesTheSameMailboxGovernor(string endpoint, bool detail)
    {
        var governor = new RecordingGovernor();
        using var client = new HttpClient(new Handler((_, _) =>
        {
            Assert.True(governor.LeaseActive);
            return Task.FromResult(Success());
        }));
        using var result = await Read(new(client, governor), endpoint);

        var acquired = Assert.Single(governor.Acquired);
        Assert.Equal("microsoft-outlook", acquired.Service);
        Assert.Equal(Account.Id, acquired.AccountId);
        Assert.Equal(detail, acquired.Detail);
        Assert.False(governor.LeaseActive);
    }

    [Fact]
    public async Task ThrottleRecordsRetryAfterBeforeReleasingLeaseThenRetriesFreshRequest()
    {
        var governor = new RecordingGovernor();
        var clock = new AdvancingClock(governor);
        var messages = new List<HttpRequestMessage>();
        using var client = new HttpClient(new Handler((message, _) =>
        {
            messages.Add(message);
            if (messages.Count > 1) return Task.FromResult(Success());
            var failure = Failure(HttpStatusCode.TooManyRequests);
            failure.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(4));
            return Task.FromResult(failure);
        }));
        using var result = await Read(new(client, governor, clock.UtcNow, clock.Delay));

        Assert.Equal(2, messages.Count);
        Assert.NotSame(messages[0], messages[1]);
        Assert.Equal(2, governor.Acquired.Count);
        Assert.Equal((TimeSpan.FromSeconds(4), ReadFailureKind.RateLimited), Assert.Single(governor.Cooldowns));
        Assert.Equal(TimeSpan.FromSeconds(4), Assert.Single(clock.Delays));
    }

    [Fact]
    public async Task PersistentThrottleStopsAfterThreeRetriesAndPreservesTheLastCooldown()
    {
        var governor = new RecordingGovernor();
        var clock = new AdvancingClock(governor);
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Failure(HttpStatusCode.TooManyRequests));
        }));

        var failure = await Assert.ThrowsAsync<ProviderReadException>(() => Read(new(client, governor, clock.UtcNow, clock.Delay)));

        Assert.Equal(ReadFailureKind.RateLimited, failure.Kind);
        Assert.Equal(4, calls);
        Assert.Equal(4, governor.Cooldowns.Count);
        Assert.Equal(3, clock.Delays.Count);
        Assert.InRange(clock.Delays[0].TotalSeconds, 1, 1.25);
        Assert.InRange(clock.Delays[1].TotalSeconds, 2, 2.25);
        Assert.InRange(clock.Delays[2].TotalSeconds, 4, 4.25);
        Assert.False(governor.LeaseActive);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task PermanentFailuresDoNotRetry(HttpStatusCode status)
    {
        var governor = new RecordingGovernor();
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Failure(status))));

        await Assert.ThrowsAsync<ProviderReadException>(() => Read(new(client, governor)));

        Assert.Single(governor.Acquired);
        Assert.Empty(governor.Cooldowns);
        Assert.False(governor.LeaseActive);
    }

    [Fact]
    public async Task RetryAfterBeyondBudgetRecordsCooldownWithoutWaitingOrAnotherRequest()
    {
        var governor = new RecordingGovernor();
        var clock = new AdvancingClock(governor);
        using var client = new HttpClient(new Handler((_, _) =>
        {
            var failure = Failure(HttpStatusCode.TooManyRequests);
            failure.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return Task.FromResult(failure);
        }));

        var exception = await Assert.ThrowsAsync<ProviderReadException>(() => Read(new(client, governor, clock.UtcNow, clock.Delay)));

        Assert.Equal(ReadFailureKind.RateLimited, exception.Kind);
        Assert.Equal(TimeSpan.FromMinutes(2), Assert.Single(governor.Cooldowns).Delay);
        Assert.Single(governor.Acquired);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task MailThrottleStopsAnotherCalendarReaderThroughTheSharedGovernor()
    {
        var now = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        var governor = new InMemoryReadGuardrails(utcNow: () => now);
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            var failure = Failure(HttpStatusCode.TooManyRequests);
            failure.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return Task.FromResult(failure);
        }));

        var mail = await Assert.ThrowsAsync<ProviderReadException>(() => Read(new(client, governor, () => now)));
        var calendar = await Assert.ThrowsAsync<ProviderReadException>(() =>
            Read(new(client, governor, () => now), "graph.events.get", preferUtc: true));

        Assert.Equal(ReadFailureKind.RateLimited, mail.Kind);
        Assert.Equal(ReadFailureKind.RateLimited, calendar.Kind);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellationDuringRetryWaitReleasesLeaseAndStopsFurtherRequests()
    {
        var governor = new RecordingGovernor();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Failure(HttpStatusCode.ServiceUnavailable))));
        var reader = new MicrosoftReadRequests(client, governor, delay: (_, token) =>
        {
            Assert.False(governor.LeaseActive);
            cancellation.Cancel();
            return Task.FromCanceled(token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read(reader, cancellationToken: cancellation.Token));

        Assert.Single(governor.Acquired);
        Assert.Single(governor.Cooldowns);
    }

    [Fact]
    public async Task CalendarDetailsRetainBothTextAndTimeZoneHeaders()
    {
        var governor = new RecordingGovernor();
        using var client = new HttpClient(new Handler((request, _) =>
        {
            var values = request.Headers.GetValues("Prefer").ToArray();
            Assert.Contains("outlook.timezone=\"UTC\"", values);
            Assert.Contains("outlook.body-content-type=\"text\"", values);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return Task.FromResult(Success());
        }));
        using var result = await Read(new(client, governor), "graph.events.get", preferUtc: true);
    }

    private static Task<System.Text.Json.JsonDocument> Read(
        MicrosoftReadRequests reader, string endpoint = "graph.messages.get",
        CancellationToken cancellationToken = default, bool preferUtc = false) =>
        reader.GetJsonAsync(Account, "https://graph.microsoft.com/v1.0/me/messages/synthetic", "synthetic-token",
            NullLogger.Instance, endpoint, 1024, true, cancellationToken, preferUtc);

    private static HttpResponseMessage Success() => new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
    private static HttpResponseMessage Failure(HttpStatusCode status) =>
        new(status) { Content = new StringContent("{\"error\":{\"code\":\"TooManyRequests\"}}") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class AdvancingClock(RecordingGovernor governor)
    {
        private DateTimeOffset _now = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        internal List<TimeSpan> Delays { get; } = [];
        internal DateTimeOffset UtcNow() => _now;
        internal Task Delay(TimeSpan duration, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.False(governor.LeaseActive);
            Delays.Add(duration);
            _now += duration;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingGovernor : IProviderRequestGovernor
    {
        internal List<(string Service, string AccountId, bool Detail)> Acquired { get; } = [];
        internal List<(TimeSpan Delay, ReadFailureKind Kind)> Cooldowns { get; } = [];
        internal bool LeaseActive { get; private set; }

        public Task<IProviderRequestLease> AcquireAsync(string service, string accountId, bool detail, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.False(LeaseActive);
            LeaseActive = true;
            Acquired.Add((service, accountId, detail));
            return Task.FromResult<IProviderRequestLease>(new Lease(this));
        }

        private sealed class Lease(RecordingGovernor owner) : IProviderRequestLease
        {
            public Task SetCooldownAsync(TimeSpan delay, ReadFailureKind kind, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.True(owner.LeaseActive);
                Assert.True(cancellationToken.CanBeCanceled);
                owner.Cooldowns.Add((delay, kind));
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                Assert.True(owner.LeaseActive);
                owner.LeaseActive = false;
                return ValueTask.CompletedTask;
            }
        }
    }
}
