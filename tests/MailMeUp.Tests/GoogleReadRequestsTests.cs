using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MailMeUp.Core;
using MailMeUp.Diagnostics;
using MailMeUp.Providers.Google;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailMeUp.Tests;

public sealed class GoogleReadRequestsTests
{
    private static readonly Account Account = new("google:synthetic-reader", "google", "Sample", "reader@example.test", true, true);

    [Theory]
    [InlineData(403, "rateLimitExceeded", ReadFailureKind.RateLimited)]
    [InlineData(403, "userRateLimitExceeded", ReadFailureKind.RateLimited)]
    [InlineData(403, "RESOURCE_EXHAUSTED", ReadFailureKind.RateLimited)]
    [InlineData(403, "dailyLimitExceeded", ReadFailureKind.RateLimited)]
    [InlineData(403, "quotaExceeded", ReadFailureKind.RateLimited)]
    [InlineData(403, "insufficientPermissions", ReadFailureKind.AccessDenied)]
    [InlineData(403, "domainPolicy", ReadFailureKind.AccessDenied)]
    [InlineData(403, "PERMISSION_DENIED", ReadFailureKind.AccessDenied)]
    [InlineData(401, "rateLimitExceeded", ReadFailureKind.SignInRequired)]
    [InlineData(429, "", ReadFailureKind.RateLimited)]
    public void ClassifiesStatusWithSpecificQuotaReasons(int status, string reason, ReadFailureKind expected) =>
        Assert.Equal(expected, GoogleReadRequests.ClassifyFailure(new(status, [reason], null)));

    [Fact]
    public async Task PermissionDeniedEnvelopeWithRateLimitReasonRetriesAndRecovers()
    {
        var clock = new AdvancingClock();
        var requests = new List<HttpRequestMessage>();
        using var client = new HttpClient(new Handler((request, _) =>
        {
            requests.Add(request);
            return Task.FromResult(requests.Count == 1
                ? Failure(HttpStatusCode.Forbidden, "rateLimitExceeded") : Success());
        }));
        var reader = new GoogleReadRequests(client, clock.UtcNow, clock.Delay);

        using var result = await Read(reader);

        Assert.Equal(2, requests.Count);
        Assert.NotSame(requests[0], requests[1]);
        Assert.InRange(Assert.Single(clock.Delays).TotalSeconds, 1, 1.25);
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task PersistentThrottlingStopsAfterThreeExponentialRetries()
    {
        var clock = new AdvancingClock();
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Failure(HttpStatusCode.TooManyRequests, "userRateLimitExceeded"));
        }));
        var reader = new GoogleReadRequests(client, clock.UtcNow, clock.Delay);

        var failure = await Assert.ThrowsAsync<ProviderReadException>(() => Read(reader));

        Assert.Equal(ReadFailureKind.RateLimited, failure.Kind);
        Assert.Equal(4, calls);
        Assert.Equal(3, clock.Delays.Count);
        Assert.InRange(clock.Delays[0].TotalSeconds, 1, 1.25);
        Assert.InRange(clock.Delays[1].TotalSeconds, 2, 2.25);
        Assert.InRange(clock.Delays[2].TotalSeconds, 4, 4.25);
    }

    [Theory]
    [InlineData("insufficientPermissions", ReadFailureKind.AccessDenied)]
    [InlineData("domainPolicy", ReadFailureKind.AccessDenied)]
    [InlineData("dailyLimitExceeded", ReadFailureKind.RateLimited)]
    [InlineData("quotaExceeded", ReadFailureKind.RateLimited)]
    public async Task PermissionAndPersistentQuotaFailuresAreNotRetried(string reason, ReadFailureKind kind)
    {
        var clock = new AdvancingClock();
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Failure(HttpStatusCode.Forbidden, reason));
        }));

        var failure = await Assert.ThrowsAsync<ProviderReadException>(() => Read(new(client, clock.UtcNow, clock.Delay)));

        Assert.Equal(kind, failure.Kind);
        Assert.Equal(1, calls);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task RetryAfterBeyondBudgetAlsoStopsQueuedAccountReadsWithoutAnotherRequest()
    {
        var clock = new AdvancingClock();
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            var response = Failure(HttpStatusCode.TooManyRequests, "userRateLimitExceeded");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return Task.FromResult(response);
        }));
        var reader = new GoogleReadRequests(client, clock.UtcNow, clock.Delay);

        var first = await Assert.ThrowsAsync<ProviderReadException>(() => Read(reader));
        var second = await Assert.ThrowsAsync<ProviderReadException>(() => Read(reader, "google.events.get"));

        Assert.Equal(ReadFailureKind.RateLimited, first.Kind);
        Assert.Equal(ReadFailureKind.RateLimited, second.Kind);
        Assert.Equal(1, calls);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task RetryAfterWithinBudgetIsRespected()
    {
        var clock = new AdvancingClock();
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            if (++calls > 1)
                return Task.FromResult(Success());
            var response = Failure(HttpStatusCode.ServiceUnavailable, "backendError");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return Task.FromResult(response);
        }));

        using var result = await Read(new(client, clock.UtcNow, clock.Delay));

        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(clock.Delays));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task MailAndCalendarShareTheAccountGateAndOtherAccountsRemainIndependent()
    {
        var clock = new AdvancingClock();
        var blocked = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
            Interlocked.Increment(ref calls) == 1 ? blocked.Task : Task.FromResult(Success())));
        var reader = new GoogleReadRequests(client, clock.UtcNow, clock.Delay);
        var first = Read(reader);
        var queued = Read(reader, "google.events.get");
        Assert.Equal(1, calls);

        using var separate = await Read(reader, account: Account with { Id = "google:other", EmailAddress = "other@example.test" });
        Assert.Equal(2, calls);
        blocked.SetResult(Success());
        using var firstResult = await first;
        using var queuedResult = await queued;

        Assert.Equal(3, calls);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(clock.Delays));
    }

    [Fact]
    public async Task CancellationWhileQueuedDoesNotIssueAnHttpRequest()
    {
        var clock = new AdvancingClock();
        var blocked = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return blocked.Task;
        }));
        var reader = new GoogleReadRequests(client, clock.UtcNow, clock.Delay);
        var first = Read(reader);
        using var cancellation = new CancellationTokenSource();
        var queued = Read(reader, cancellationToken: cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(1, calls);
        blocked.SetResult(Success());
        using var result = await first;
    }

    [Fact]
    public async Task CancellationDuringBackoffPreventsTheRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Failure(HttpStatusCode.Forbidden, "rateLimitExceeded"));
        }));
        var reader = new GoogleReadRequests(client, delay: (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read(reader, cancellationToken: cancellation.Token));
        Assert.Equal(1, calls);
    }

    private static Task<JsonDocument> Read(GoogleReadRequests reader, string endpoint = "gmail.messages.get",
        Account? account = null, CancellationToken cancellationToken = default) =>
        reader.GetJsonAsync(account ?? Account, "https://provider.example.test/message", "synthetic-token",
            NullLogger.Instance, endpoint, 1024, cancellationToken);

    private static HttpResponseMessage Success() => new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };

    private static HttpResponseMessage Failure(HttpStatusCode status, string reason) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            error = new { status = "PERMISSION_DENIED", message = "private provider content", errors = new[] { new { reason } } }
        }))
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private sealed class AdvancingClock
    {
        private DateTimeOffset _now = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> Delays { get; } = [];
        public DateTimeOffset UtcNow() => _now;
        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(duration);
            _now += duration;
            return Task.CompletedTask;
        }
    }
}
