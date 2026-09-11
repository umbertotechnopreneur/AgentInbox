using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using MailMeUp.Core;
using MailMeUp.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MailMeUp.Providers.Google;

/// <summary>Shares bounded Google read pacing and retries across mail and calendar readers in this process.</summary>
internal sealed class GoogleReadRequests
{
    private const int MaximumRetries = 3;
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MetadataInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan DetailInterval = TimeSpan.FromSeconds(1);
    private readonly ConcurrentDictionary<string, AccountGate> _accounts = new(StringComparer.Ordinal);
    private readonly HttpClient _client;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    internal static GoogleReadRequests Shared { get; } = new(new HttpClient());

    internal GoogleReadRequests(
        HttpClient client,
        Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _client = client;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    internal async Task<JsonDocument> GetJsonAsync(
        Account account, string url, string accessToken, ILogger logger, string endpoint,
        int maximumBytes, CancellationToken cancellationToken, bool allowNoContent = false)
    {
        // Queueing, pacing, response reads and all retries share the caller's remaining budget.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestBudget);
        var token = deadline.Token;
        var started = _utcNow();
        var gate = _accounts.GetOrAdd(account.Id, _ => new AccountGate());
        await gate.Mutex.WaitAsync(token);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var now = _utcNow();
                var next = gate.CooldownUntil > gate.NextRequestAt ? gate.CooldownUntil : gate.NextRequestAt;
                var wait = next - now;
                if (wait > TimeSpan.Zero)
                {
                    if (gate.CooldownUntil > now && wait >= RequestBudget - (now - started))
                    {
                        throw new ProviderReadException("The provider requires a longer pause before another read.", gate.CooldownKind);
                    }
                    await _delay(wait, token);
                }

                token.ThrowIfCancellationRequested();
                gate.NextRequestAt = _utcNow() + (endpoint is "gmail.messages.get" or "google.events.get"
                    ? DetailInterval : MetadataInterval);
                ProviderHttpFailure? failure = null;
                try
                {
                    // Each retry needs a fresh request; the same message cannot be sent twice.
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    return await ProviderHttpDiagnostics.ReadJsonAsync(
                        _client, request, logger, endpoint, maximumBytes, ClassifyStatus, token, allowNoContent,
                        classifyFailure: details =>
                        {
                            failure = details;
                            return ClassifyFailure(details);
                        });
                }
                catch (ProviderReadException exception) when (failure is not null &&
                    (exception.Kind == ReadFailureKind.RateLimited || IsTransient(failure)))
                {
                    var retryDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt)) +
                        TimeSpan.FromMilliseconds(Random.Shared.Next(0, 251));
                    if (HasPersistentQuota(failure))
                        retryDelay = TimeSpan.FromMinutes(1);
                    if (failure.RetryAfter is { } retryAfter && retryAfter > retryDelay)
                        retryDelay = retryAfter;

                    // Preserve the cooldown even after a failed request, so queued readers also slow down.
                    now = _utcNow();
                    gate.CooldownUntil = retryDelay >= DateTimeOffset.MaxValue - now
                        ? DateTimeOffset.MaxValue : now + retryDelay;
                    gate.CooldownKind = exception.Kind;
                    if (attempt >= MaximumRetries || HasPersistentQuota(failure) ||
                        retryDelay >= RequestBudget - (now - started))
                        throw;

                    logger.LogInformation(
                        "Provider HTTP {Endpoint} will retry: retry={Retry}; category={FailureCategory}; delayMs={DelayMs}",
                        endpoint, attempt + 1, exception.Kind, (long)retryDelay.TotalMilliseconds);
                }
            }
        }
        finally
        {
            gate.Mutex.Release();
        }
    }

    internal static ReadFailureKind ClassifyFailure(ProviderHttpFailure failure)
    {
        if (failure.StatusCode == 429 || failure.StatusCode == 403 && failure.Codes.Any(code => code is
            "rateLimitExceeded" or "userRateLimitExceeded" or "RESOURCE_EXHAUSTED" or
            "dailyLimitExceeded" or "quotaExceeded"))
            return ReadFailureKind.RateLimited;

        return ClassifyStatus(failure.StatusCode);
    }

    private static bool IsTransient(ProviderHttpFailure failure) =>
        failure.StatusCode is 408 or 429 or 500 or 502 or 503 or 504 ||
        ClassifyFailure(failure) == ReadFailureKind.RateLimited && !HasPersistentQuota(failure);

    private static bool HasPersistentQuota(ProviderHttpFailure failure) =>
        failure.Codes.Any(code => code is "dailyLimitExceeded" or "quotaExceeded");

    private static ReadFailureKind ClassifyStatus(int statusCode) => statusCode switch
    {
        400 => ReadFailureKind.InvalidRequest,
        401 => ReadFailureKind.SignInRequired,
        403 => ReadFailureKind.AccessDenied,
        404 or 410 => ReadFailureKind.ItemUnavailable,
        408 or 504 => ReadFailureKind.Timeout,
        429 => ReadFailureKind.RateLimited,
        >= 500 => ReadFailureKind.ProviderUnavailable,
        _ => ReadFailureKind.Unknown
    };

    private sealed class AccountGate
    {
        internal SemaphoreSlim Mutex { get; } = new(1, 1);
        internal DateTimeOffset NextRequestAt { get; set; }
        internal DateTimeOffset CooldownUntil { get; set; }
        internal ReadFailureKind CooldownKind { get; set; }
    }
}
