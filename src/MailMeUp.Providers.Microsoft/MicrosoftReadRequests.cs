using System.Net.Http.Headers;
using System.Text.Json;
using MailMeUp.Core;
using MailMeUp.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MailMeUp.Providers.Microsoft;

/// <summary>Applies one mailbox request policy to Microsoft mail, folders and calendars.</summary>
internal sealed class MicrosoftReadRequests
{
    internal const string Service = "microsoft-outlook";
    private const int MaximumRetries = 3;
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(30);
    private readonly HttpClient _client;
    private readonly IProviderRequestGovernor _governor;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    internal MicrosoftReadRequests(
        HttpClient client,
        IProviderRequestGovernor? governor = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _client = client;
        _governor = governor ?? InMemoryReadGuardrails.Shared;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    internal async Task<JsonDocument> GetJsonAsync(
        Account account, string url, string accessToken, ILogger logger, string endpoint,
        int maximumBytes, bool preferText, CancellationToken cancellationToken, bool preferUtc = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestBudget);
        var token = deadline.Token;
        var started = _utcNow();
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            TimeSpan retryDelay;
            await using (var lease = await _governor.AcquireAsync(Service, account.Id,
                endpoint is "graph.messages.get" or "graph.events.get", token))
            {
                ProviderHttpFailure? failure = null;
                try
                {
                    // Every attempt gets a fresh request, including authorization and representation headers.
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    if (preferUtc)
                        request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");
                    else
                        request.Headers.TryAddWithoutValidation("ConsistencyLevel", "eventual");
                    if (preferText)
                        request.Headers.TryAddWithoutValidation("Prefer", "outlook.body-content-type=\"text\"");

                    return await ProviderHttpDiagnostics.ReadJsonAsync(
                        _client, request, logger, endpoint, maximumBytes, ClassifyStatus, token,
                        classifyFailure: details =>
                        {
                            failure = details;
                            return ClassifyStatus(details.StatusCode);
                        });
                }
                catch (ProviderReadException exception) when (failure is not null && IsTransient(failure.StatusCode))
                {
                    retryDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt)) +
                        TimeSpan.FromMilliseconds(Random.Shared.Next(0, 251));
                    if (failure.RetryAfter is { } retryAfter && retryAfter > retryDelay)
                        retryDelay = retryAfter;

                    // Persist before releasing the lease, including the last attempt or an expiring request.
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await lease.SetCooldownAsync(retryDelay, exception.Kind, cleanup.Token);
                    if (attempt >= MaximumRetries || retryDelay >= RequestBudget - (_utcNow() - started))
                        throw;

                    logger.LogInformation(
                        "Provider HTTP {Endpoint} will retry: retry={Retry}; category={FailureCategory}; delayMs={DelayMs}",
                        endpoint, attempt + 1, exception.Kind, (long)retryDelay.TotalMilliseconds);
                }
            }

            // Other requests see the same cooldown while this caller waits without holding the lease.
            await _delay(retryDelay, token);
        }
    }

    internal static ReadFailureKind ClassifyStatus(int statusCode) => statusCode switch
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

    private static bool IsTransient(int statusCode) => statusCode is 408 or 429 or 500 or 502 or 503 or 504;
}

/// <summary>Caches only verified exclusion-folder identities and coalesces concurrent refreshes.</summary>
internal sealed class MicrosoftExcludedFolderCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly object _sync = new();
    private readonly Dictionary<(string ClientId, string AccountId), Entry> _entries = new();
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly int _capacity;

    internal MicrosoftExcludedFolderCache(Func<DateTimeOffset>? utcNow = null, int capacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _capacity = capacity;
    }

    internal async Task<IReadOnlyList<string>> GetAsync(
        string clientId, string accountId,
        Func<CancellationToken, Task<IReadOnlyList<string>>> read,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (clientId, accountId);
        Entry? entry;
        var refresh = false;
        lock (_sync)
        {
            var now = _utcNow();
            if (_entries.TryGetValue(key, out entry) && entry.Result.Task.IsCompleted && entry.ExpiresAt <= now)
            {
                _entries.Remove(key);
                entry = null;
            }

            if (entry is null)
            {
                if (_entries.Count >= _capacity)
                {
                    var oldest = _entries.Where(pair => pair.Value.Result.Task.IsCompleted)
                        .OrderBy(pair => pair.Value.ExpiresAt).FirstOrDefault();
                    if (oldest.Value is not null) _entries.Remove(oldest.Key);
                }

                if (_entries.Count < _capacity)
                {
                    entry = new Entry();
                    _entries.Add(key, entry);
                    refresh = true;
                }
            }
        }

        // A full cache of active refreshes must not grow or prevent an uncached, governed request.
        if (entry is null) return await read(cancellationToken);
        if (refresh)
        {
            try
            {
                var ids = Array.AsReadOnly((await read(cancellationToken)).ToArray());
                cancellationToken.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    entry.ExpiresAt = _utcNow() + Lifetime;
                    entry.Result.TrySetResult(ids);
                }
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    _entries.Remove(key);
                    if (exception is OperationCanceledException cancelled)
                        entry.Result.TrySetCanceled(cancelled.CancellationToken);
                    else
                        entry.Result.TrySetException(exception);
                }
            }
        }

        return await entry.Result.Task.WaitAsync(cancellationToken);
    }

    private sealed class Entry
    {
        internal TaskCompletionSource<IReadOnlyList<string>> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal DateTimeOffset ExpiresAt { get; set; }
    }
}
