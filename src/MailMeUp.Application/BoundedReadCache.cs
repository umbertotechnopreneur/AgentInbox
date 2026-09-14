using System.Security.Cryptography;
using System.Text;
using MailMeUp.Core;

namespace MailMeUp.Application;

/// <summary>Coalesces short-lived provider detail reads in bounded process memory.</summary>
internal sealed class BoundedReadCache<T>(TimeProvider timeProvider, Func<T, long> measureBytes) where T : class
{
    private const int MaximumEntries = 64;
    private const int MaximumPending = 16;
    private const long MaximumEntryBytes = 256 * 1024;
    private const long MaximumBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<T>> _pending = new(StringComparer.Ordinal);
    private long _bytes;

    internal async Task<T> GetAsync(string key, Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        TaskCompletionSource<T>? owner = null;
        Task<T> task;
        lock (_sync)
        {
            Prune();
            if (_entries.TryGetValue(cacheKey, out var cached)) return cached.Value;
            if (!_pending.TryGetValue(cacheKey, out var pending))
            {
                if (_pending.Count >= MaximumPending)
                    throw new ProviderReadException("Too many detail reads are waiting.", ReadFailureKind.BudgetExceeded);
                owner = pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending.Add(cacheKey, pending);
            }
            task = pending.Task;
        }

        // The first caller owns provider cancellation. Other callers can leave without cancelling that read.
        if (owner is not null) _ = PopulateAsync(cacheKey, owner, read, cancellationToken);
        return await task.WaitAsync(cancellationToken);
    }

    private async Task PopulateAsync(string key, TaskCompletionSource<T> completion,
        Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        try
        {
            var value = await read(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = measureBytes(value);
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Prune();
                if (bytes is >= 0 and <= MaximumEntryBytes)
                {
                    while (_entries.Count >= MaximumEntries || _bytes + bytes > MaximumBytes)
                        Remove(_entries.MinBy(pair => pair.Value.ExpiresAt).Key);
                    _entries[key] = new(value, timeProvider.GetUtcNow() + Lifetime, bytes);
                    _bytes += bytes;
                }
                _pending.Remove(key);
            }
            completion.TrySetResult(value);
        }
        catch (OperationCanceledException exception)
        {
            lock (_sync) _pending.Remove(key);
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            lock (_sync) _pending.Remove(key);
            completion.TrySetException(exception);
        }
    }

    private void Prune()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var key in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
            Remove(key);
    }

    private void Remove(string key)
    {
        if (_entries.Remove(key, out var entry)) _bytes -= entry.Bytes;
    }

    private sealed record Entry(T Value, DateTimeOffset ExpiresAt, long Bytes);
}

internal static class ProviderContentSize
{
    internal static long Mail(ProviderMailMessage value) => 2L *
        (value.ProviderMessageId.Length + (long)value.Subject.Length + value.Sender.Length + value.PlainText.Length +
         value.To.Sum(item => (long)item.Length) + value.Cc.Sum(item => (long)item.Length));

    internal static long Event(ProviderEvent value) => 2L *
        (value.ProviderEventId.Length + (long)value.Title.Length + value.Start.Length + value.End.Length +
         value.Location.Length + value.Description.Length + (value.MeetingLink?.Length ?? 0) +
         value.Attendees.Sum(item => (long)item.Length));
}
