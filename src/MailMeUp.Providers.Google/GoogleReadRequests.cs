using System.Net.Http.Headers;
using System.Text.Json;
using MailMeUp.Core;
using MailMeUp.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MailMeUp.Providers.Google;

/// <summary>Applies shared request guardrails and bounded retries to Google mail and calendar reads.</summary>
internal sealed class GoogleReadRequests
{
    private const int MaximumRetries = 3;
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(30);
    private readonly HttpClient _client;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly IProviderRequestGovernor _governor;

    internal static GoogleReadRequests Shared { get; } = new(new HttpClient());

    internal GoogleReadRequests(
        HttpClient client,
        Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IProviderRequestGovernor? governor = null)
    {
        _client = client;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        _governor = governor ?? (utcNow is null && delay is null
            ? InMemoryReadGuardrails.Shared
            : new InMemoryReadGuardrails(utcNow: _utcNow, delay: _delay));
    }

    internal async Task<JsonDocument> GetJsonAsync(
        Account account, string url, string accessToken, ILogger logger, string endpoint,
        int maximumBytes, CancellationToken cancellationToken, bool allowNoContent = false,
        IProviderRequestGovernor? governor = null)
    {
        // Queueing, pacing, response reads and all retries share the caller's remaining budget.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestBudget);
        var token = deadline.Token;
        var started = _utcNow();
        var service = endpoint.StartsWith("gmail.", StringComparison.Ordinal) ? "gmail" : "google-calendar";
        var detail = endpoint is "gmail.messages.get" or "google.events.get";
        var requests = governor ?? _governor;
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            TimeSpan retryDelay;
            await using (var lease = await requests.AcquireAsync(service, account.Id, detail, token))
            {
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
                    retryDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt)) +
                        TimeSpan.FromMilliseconds(Random.Shared.Next(0, 251));
                    if (HasPersistentQuota(failure))
                        retryDelay = TimeSpan.FromMinutes(1);
                    if (failure.RetryAfter is { } retryAfter && retryAfter > retryDelay)
                        retryDelay = retryAfter;

                    // Publish the service cooldown before releasing this attempt's cross-process lease.
                    using (var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                        await lease.SetCooldownAsync(retryDelay, exception.Kind, cleanup.Token);
                    if (attempt >= MaximumRetries || HasPersistentQuota(failure) ||
                        retryDelay >= RequestBudget - (_utcNow() - started))
                        throw;

                    logger.LogInformation(
                        "Provider HTTP {Endpoint} will retry: retry={Retry}; category={FailureCategory}; delayMs={DelayMs}",
                        endpoint, attempt + 1, exception.Kind, (long)retryDelay.TotalMilliseconds);
                }
            }
            // Other accounts and APIs may proceed while this request waits to acquire its next attempt.
            await _delay(retryDelay, token);
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

}
