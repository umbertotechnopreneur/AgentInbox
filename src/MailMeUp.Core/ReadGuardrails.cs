using System.Security.Cryptography;
using System.Text;

namespace MailMeUp.Core;

/// <summary>Bounds provider attempts and assistant output independently of provider quotas.</summary>
public sealed record ReadGuardrailLimits
{
    /// <summary>Maximum attempts for one account and provider service in a rolling minute.</summary>
    public int ProviderAttemptsPerAccountServicePerMinute { get; init; } = 60;
    /// <summary>Maximum provider attempts across this local profile in a rolling minute.</summary>
    public int ProviderAttemptsPerProfilePerMinute { get; init; } = 180;
    /// <summary>Maximum content tool admissions in the rolling read window.</summary>
    public int ContentReadsPerWindow { get; init; } = 120;
    /// <summary>Maximum detail tool admissions in the rolling read window.</summary>
    public int DetailReadsPerWindow { get; init; } = 20;
    /// <summary>Duration of the content and output budget window in seconds.</summary>
    public int ReadWindowSeconds { get; init; } = 900;
    /// <summary>Maximum cumulative serialized MCP response bytes in the read window.</summary>
    public int OutputBytesPerWindow { get; init; } = 256 * 1024;
    /// <summary>Maximum serialized bytes in one MCP response.</summary>
    public int ResponseBytes { get; init; } = 64 * 1024;
    /// <summary>Minimum spacing before an account/service metadata attempt.</summary>
    public int MetadataIntervalMilliseconds { get; init; } = 200;
    /// <summary>Minimum spacing before an account/service detail attempt.</summary>
    public int DetailIntervalMilliseconds { get; init; } = 1000;
    /// <summary>Maximum wait for an exclusive request or state lease.</summary>
    public int LeaseWaitSeconds { get; init; } = 30;

    /// <summary>Rejects settings that disable protection or allow unbounded state.</summary>
    public void Validate()
    {
        Range(ProviderAttemptsPerAccountServicePerMinute, 1, 10_000);
        Range(ProviderAttemptsPerProfilePerMinute, 1, 10_000);
        Range(ContentReadsPerWindow, 1, 10_000);
        Range(DetailReadsPerWindow, 1, ContentReadsPerWindow);
        Range(ReadWindowSeconds, 60, 3600);
        Range(OutputBytesPerWindow, 1024, 16 * 1024 * 1024);
        Range(ResponseBytes, 1024, Math.Min(OutputBytesPerWindow, 1024 * 1024));
        Range(MetadataIntervalMilliseconds, 1, 60_000);
        Range(DetailIntervalMilliseconds, MetadataIntervalMilliseconds, 60_000);
        Range(LeaseWaitSeconds, 1, 120);
    }

    private static void Range(int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(nameof(value), "Read guardrail settings are outside their supported ranges.");
    }
}

/// <summary>Admits every HTTP attempt under an exclusive account/service lease.</summary>
public interface IProviderRequestGovernor
{
    /// <summary>Reserves an attempt or fails before provider work starts.</summary>
    Task<IProviderRequestLease> AcquireAsync(string service, string accountId, bool detail, CancellationToken cancellationToken = default);
}

/// <summary>Holds provider serialization through the response and records shared cooldowns.</summary>
public interface IProviderRequestLease : IAsyncDisposable
{
    /// <summary>Extends the service pause without disclosing provider response content.</summary>
    Task SetCooldownAsync(TimeSpan delay, ReadFailureKind kind, CancellationToken cancellationToken = default);
}

/// <summary>Enforces rolling tool admission and serialized-output budgets.</summary>
public interface IReadBudget
{
    /// <summary>Gets the immutable limits active in this process.</summary>
    ReadGuardrailLimits Limits { get; }
    /// <summary>Charges an admission before content work, including failed reads.</summary>
    Task AdmitReadAsync(bool detail, CancellationToken cancellationToken = default);
    /// <summary>Atomically charges one complete serialized response before delivery.</summary>
    Task ChargeOutputAsync(int bytes, CancellationToken cancellationToken = default);
}

/// <summary>Shares admission rules between in-memory and persistent local guardrails.</summary>
public abstract class ReadGuardrails : IProviderRequestGovernor, IReadBudget
{
    private const int MaximumLedgerEntries = 20_000;
    private const int MaximumScopes = 4096;
    private const long MaximumTimestamp = 253402300799999;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Initializes validated limits and optional deterministic clock seams.</summary>
    protected ReadGuardrails(ReadGuardrailLimits limits, Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        limits.Validate();
        Limits = limits;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
    }

    /// <inheritdoc />
    public ReadGuardrailLimits Limits { get; }

    /// <inheritdoc />
    public async Task<IProviderRequestLease> AcquireAsync(string service, string accountId, bool detail,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (service is not ("gmail" or "google-calendar" or "microsoft-outlook"))
            throw new ArgumentException("The provider service is not supported.", nameof(service));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(service + "\0" + accountId)));
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(TimeSpan.FromSeconds(Limits.LeaseWaitSeconds));
        IAsyncDisposable? scopeLease = null;
        try
        {
            scopeLease = await LockScopeAsync(key, wait.Token);
            while (true)
            {
                var delay = await UpdateStateAsync(state =>
                {
                    var now = _utcNow().ToUnixTimeMilliseconds();
                    Prune(state, now);
                    var scope = state.Scopes.GetValueOrDefault(key) ?? new ProviderScope();
                    if (scope.CooldownUntil > now)
                        throw new ProviderReadException("Provider reads are paused. Wait before trying again.", scope.CooldownKind);
                    if (state.ProviderAttempts.Count >= Limits.ProviderAttemptsPerProfilePerMinute ||
                        scope.Attempts.Count >= Limits.ProviderAttemptsPerAccountServicePerMinute)
                        throw Exhausted();
                    var interval = detail ? Limits.DetailIntervalMilliseconds : Limits.MetadataIntervalMilliseconds;
                    var remaining = scope.Attempts.Count == 0 ? 0 : scope.LastAttempt + interval - now;
                    if (remaining > 0) return (remaining, false);
                    if (!state.Scopes.ContainsKey(key) && state.Scopes.Count >= MaximumScopes)
                        throw Exhausted();
                    scope.LastAttempt = now;
                    scope.Attempts.Add(now);
                    state.Scopes[key] = scope;
                    state.ProviderAttempts.Add(now);
                    return (0L, true);
                }, wait.Token);
                if (delay == 0) break;
                await _delay(TimeSpan.FromMilliseconds(delay), wait.Token);
            }

            var lease = new ProviderLease(this, key, scopeLease);
            scopeLease = null;
            return lease;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Exhausted();
        }
        finally
        {
            if (scopeLease is not null) await scopeLease.DisposeAsync();
        }
    }

    /// <inheritdoc />
    public async Task AdmitReadAsync(bool detail, CancellationToken cancellationToken = default)
    {
        await UpdateStateAsync(state =>
        {
            var now = _utcNow().ToUnixTimeMilliseconds();
            Prune(state, now);
            if (state.ContentReads.Count >= Limits.ContentReadsPerWindow ||
                detail && state.DetailReads.Count >= Limits.DetailReadsPerWindow ||
                state.Output.Sum(item => (long)item.Bytes) >= Limits.OutputBytesPerWindow)
                throw Exhausted();
            state.ContentReads.Add(now);
            if (detail) state.DetailReads.Add(now);
            return (true, true);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ChargeOutputAsync(int bytes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (bytes > Limits.ResponseBytes) throw Exhausted();
        if (bytes == 0) return;
        await UpdateStateAsync(state =>
        {
            var now = _utcNow().ToUnixTimeMilliseconds();
            Prune(state, now);
            if (state.Output.Count >= MaximumLedgerEntries ||
                state.Output.Sum(item => (long)item.Bytes) + bytes > Limits.OutputBytesPerWindow)
                throw Exhausted();
            state.Output.Add(new OutputCharge { At = now, Bytes = bytes });
            return (true, true);
        }, cancellationToken);
    }

    private Task SetCooldownAsync(string key, TimeSpan delay, ReadFailureKind kind, CancellationToken cancellationToken)
    {
        if (delay < TimeSpan.Zero || !ValidCooldownKind(kind))
            throw new ArgumentException("The provider cooldown is outside its supported range.", nameof(delay));
        return UpdateStateAsync(state =>
        {
            var now = _utcNow().ToUnixTimeMilliseconds();
            Prune(state, now);
            if (!state.Scopes.TryGetValue(key, out var scope))
            {
                if (state.Scopes.Count >= MaximumScopes) throw Exhausted();
                state.Scopes[key] = scope = new ProviderScope();
            }
            var milliseconds = Math.Ceiling(delay.TotalMilliseconds);
            var until = milliseconds >= MaximumTimestamp - now ? MaximumTimestamp : now + (long)milliseconds;
            if (until > scope.CooldownUntil)
            {
                scope.CooldownUntil = until;
                scope.CooldownKind = kind;
            }
            return (true, true);
        }, cancellationToken);
    }

    /// <summary>Serializes a provider scope until the returned lease is disposed.</summary>
    protected abstract Task<IAsyncDisposable> LockScopeAsync(string key, CancellationToken cancellationToken);

    /// <summary>Applies an atomic ledger transaction; persists only admitted changes.</summary>
    protected abstract Task<T> UpdateStateAsync<T>(Func<GuardrailState, (T Result, bool Save)> update, CancellationToken cancellationToken);

    /// <summary>Validates deserialized state before it can authorize further reads.</summary>
    protected static void ValidateState(GuardrailState state)
    {
        if (state.Version != 1 || state.ProviderAttempts is null || state.ContentReads is null ||
            state.DetailReads is null || state.Output is null || state.Scopes is null || state.Scopes.Count > MaximumScopes ||
            !ValidTimes(state.ProviderAttempts) || !ValidTimes(state.ContentReads) || !ValidTimes(state.DetailReads) ||
            state.Output.Count > MaximumLedgerEntries || state.Output.Any(item => item is null || !ValidTime(item.At) || item.Bytes is < 1 or > 1024 * 1024))
            throw InvalidState();
        foreach (var pair in state.Scopes)
        {
            var scope = pair.Value;
            if (pair.Key.Length != 64 || pair.Key.Any(character => !char.IsAsciiHexDigit(character)) || scope is null ||
                scope.Attempts is null || !ValidTimes(scope.Attempts) || !ValidTime(scope.LastAttempt) || !ValidTime(scope.CooldownUntil) ||
                !ValidCooldownKind(scope.CooldownKind))
                throw InvalidState();
        }
    }

    private static bool ValidTimes(List<long> timestamps) =>
        timestamps.Count <= MaximumLedgerEntries && timestamps.All(ValidTime);
    private static bool ValidTime(long value) => value is >= 0 and <= MaximumTimestamp;
    private static bool ValidCooldownKind(ReadFailureKind kind) =>
        kind is ReadFailureKind.RateLimited or ReadFailureKind.BudgetExceeded or ReadFailureKind.ProviderUnavailable or ReadFailureKind.Timeout or ReadFailureKind.Network;

    private void Prune(GuardrailState state, long now)
    {
        state.ProviderAttempts.RemoveAll(value => value <= now - 60_000);
        var readCutoff = now - (long)Limits.ReadWindowSeconds * 1000;
        state.ContentReads.RemoveAll(value => value <= readCutoff);
        state.DetailReads.RemoveAll(value => value <= readCutoff);
        state.Output.RemoveAll(item => item.At <= readCutoff);
        foreach (var pair in state.Scopes.ToArray())
        {
            pair.Value.Attempts.RemoveAll(value => value <= now - 60_000);
            if (pair.Value.Attempts.Count == 0 && pair.Value.CooldownUntil <= now && pair.Value.LastAttempt <= now - 60_000)
                state.Scopes.Remove(pair.Key);
        }
    }

    /// <summary>Creates a safe local-budget failure.</summary>
    protected static ProviderReadException Exhausted() =>
        new("The local read budget is exhausted or busy. Wait before requesting more information.", ReadFailureKind.BudgetExceeded);

    /// <summary>Creates a safe error when persisted controls cannot be trusted.</summary>
    protected static ProviderReadException InvalidState() =>
        new("Local read guardrails are unavailable. Check the local configuration before retrying.", ReadFailureKind.LocalConfiguration);

    /// <summary>Contains only non-secret usage counters and hashed provider scopes.</summary>
    protected sealed class GuardrailState
    {
        /// <summary>Creates an empty ledger.</summary>
        public GuardrailState() { }
        /// <summary>Gets or sets the ledger schema version.</summary>
        public int Version { get; set; } = 1;
        /// <summary>Gets or sets provider attempt timestamps.</summary>
        public List<long> ProviderAttempts { get; set; } = [];
        /// <summary>Gets or sets content admission timestamps.</summary>
        public List<long> ContentReads { get; set; } = [];
        /// <summary>Gets or sets detail admission timestamps.</summary>
        public List<long> DetailReads { get; set; } = [];
        /// <summary>Gets or sets serialized output charges.</summary>
        public List<OutputCharge> Output { get; set; } = [];
        /// <summary>Gets or sets scopes identified only by SHA-256 keys.</summary>
        public Dictionary<string, ProviderScope> Scopes { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>Records serialized output byte count without storing any response.</summary>
    protected sealed class OutputCharge
    {
        /// <summary>Creates one output charge.</summary>
        public OutputCharge() { }
        /// <summary>Gets or sets the admission timestamp.</summary>
        public long At { get; set; }
        /// <summary>Gets or sets the serialized response size.</summary>
        public int Bytes { get; set; }
    }

    /// <summary>Tracks one hashed account/service scope without identity data.</summary>
    protected sealed class ProviderScope
    {
        /// <summary>Creates one provider scope.</summary>
        public ProviderScope() { }
        /// <summary>Gets or sets attempt timestamps for this scope.</summary>
        public List<long> Attempts { get; set; } = [];
        /// <summary>Gets or sets the last admitted attempt timestamp.</summary>
        public long LastAttempt { get; set; }
        /// <summary>Gets or sets the end of a provider pause.</summary>
        public long CooldownUntil { get; set; }
        /// <summary>Gets or sets a safe category for the active pause.</summary>
        public ReadFailureKind CooldownKind { get; set; } = ReadFailureKind.RateLimited;
    }

    private sealed class ProviderLease(ReadGuardrails owner, string key, IAsyncDisposable inner) : IProviderRequestLease
    {
        private IAsyncDisposable? _inner = inner;
        public Task SetCooldownAsync(TimeSpan delay, ReadFailureKind kind, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_inner is null, this);
            return owner.SetCooldownAsync(key, delay, kind, cancellationToken);
        }
        public async ValueTask DisposeAsync()
        {
            var lease = Interlocked.Exchange(ref _inner, null);
            if (lease is not null) await lease.DisposeAsync();
        }
    }
}

/// <summary>Uses the same guardrail rules when persistent profile storage is not supplied.</summary>
public sealed class InMemoryReadGuardrails : ReadGuardrails
{
    private readonly Dictionary<string, ScopeGate> _scopes = new(StringComparer.Ordinal);
    private readonly object _scopeGate = new();
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly GuardrailState _state = new();

    /// <summary>Gets process-wide fallback controls for independently constructed adapters.</summary>
    public static InMemoryReadGuardrails Shared { get; } = new();

    /// <summary>Creates isolated controls with optional clock and delay seams for synthetic tests.</summary>
    public InMemoryReadGuardrails(ReadGuardrailLimits? limits = null, Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null) : base(limits ?? new(), utcNow, delay) { }

    /// <inheritdoc />
    protected override async Task<IAsyncDisposable> LockScopeAsync(string key, CancellationToken cancellationToken)
    {
        ScopeGate gate;
        lock (_scopeGate)
        {
            if (!_scopes.TryGetValue(key, out gate!)) _scopes.Add(key, gate = new());
            gate.Users++;
        }
        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken);
            return new GateLease(this, key, gate);
        }
        catch
        {
            ReleaseScope(key, gate, held: false);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async Task<T> UpdateStateAsync<T>(Func<GuardrailState, (T Result, bool Save)> update, CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try { return update(_state).Result; }
        finally { _stateGate.Release(); }
    }

    private void ReleaseScope(string key, ScopeGate gate, bool held)
    {
        lock (_scopeGate)
        {
            if (held) gate.Semaphore.Release();
            if (--gate.Users != 0) return;
            _scopes.Remove(key);
            gate.Semaphore.Dispose();
        }
    }

    private sealed class ScopeGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users { get; set; }
    }

    private sealed class GateLease(InMemoryReadGuardrails owner, string key, ScopeGate gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { owner.ReleaseScope(key, gate, held: true); return ValueTask.CompletedTask; }
    }
}
