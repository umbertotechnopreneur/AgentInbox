using System.Text.Json;
using System.Text.Json.Serialization;
using MailMeUp.Core;

namespace MailMeUp.Storage;

/// <summary>Shares non-secret read counters and provider pauses across processes in one local profile.</summary>
public sealed class FileReadGuardrails : ReadGuardrails, IReadGuardrailManagement
{
    private const int MaximumStateBytes = 8 * 1024 * 1024;
    private const int MaximumSettingsBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12
    };
    private readonly string _dataDirectory;
    private readonly string _directory;
    private readonly string _statePath;
    private readonly string _settingsPath;

    /// <summary>Reads optional bounded settings without creating state, with optional deterministic clock seams.</summary>
    public FileReadGuardrails(string dataDirectory, Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null) : base(ReadLimits(dataDirectory), utcNow, delay)
    {
        try
        {
            _dataDirectory = Path.GetFullPath(dataDirectory);
            _directory = Path.Combine(_dataDirectory, "read-guardrails");
            _statePath = Path.Combine(_directory, "ledger.json");
            _settingsPath = Path.Combine(_dataDirectory, "read-guardrails.json");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw InvalidState();
        }
    }

    /// <inheritdoc />
    public async Task<ReadGuardrailStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var savedLimits = ReadLimits(_dataDirectory);
            var state = await ReadStateAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new ReadGuardrailStatus(Limits, savedLimits, CaptureUsage(state));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw InvalidState();
        }
    }

    /// <inheritdoc />
    public async Task<ReadGuardrailStatus> SaveLimitsAsync(ReadGuardrailLimits limits, ReadGuardrailLimits expectedLimits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(expectedLimits);
        limits.Validate();
        expectedLimits.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(TimeSpan.FromSeconds(Limits.LeaseWaitSeconds));
        try
        {
            Directory.CreateDirectory(_directory);
            await using var settingsLease = await OpenExclusiveAsync(Path.Combine(_directory, "settings.lock"), wait.Token);
            var savedLimits = ReadLimits(_dataDirectory);
            if (savedLimits != expectedLimits)
                throw new InvalidOperationException("The saved read limits changed. Reload them before saving again.");
            // A damaged ledger must not be hidden by changing settings or resetting usage.
            var state = await ReadStateAsync(wait.Token);
            var status = new ReadGuardrailStatus(Limits, limits, CaptureUsage(state));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(limits, JsonOptions);
            if (bytes.Length > MaximumSettingsBytes) throw InvalidState();
            await WriteAtomicAsync(_dataDirectory, _settingsPath, bytes, wait.Token);
            // The atomic rename commits the save. Do not report a later read or cancellation as a failed save.
            return status;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Exhausted();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw InvalidState();
        }
    }

    private static ReadGuardrailLimits ReadLimits(string dataDirectory)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
            var path = Path.Combine(Path.GetFullPath(dataDirectory), "read-guardrails.json");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > MaximumSettingsBytes) throw InvalidState();
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 4 });
            RequireObject(document.RootElement);
            var limits = document.RootElement.Deserialize<ReadGuardrailLimits>(JsonOptions) ?? throw InvalidState();
            limits.Validate();
            return limits;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (Exception exception) when (exception is ArgumentException or JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw InvalidState();
        }
    }

    /// <inheritdoc />
    protected override async Task<IAsyncDisposable> LockScopeAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var scopes = Path.Combine(_directory, "scopes");
            Directory.CreateDirectory(scopes);
            return await OpenExclusiveAsync(Path.Combine(scopes, key + ".lock"), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw InvalidState();
        }
    }

    /// <inheritdoc />
    protected override async Task<T> UpdateStateAsync<T>(Func<GuardrailState, (T Result, bool Save)> update,
        CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(TimeSpan.FromSeconds(Limits.LeaseWaitSeconds));
        try
        {
            Directory.CreateDirectory(_directory);
            await using var stateLease = await OpenExclusiveAsync(Path.Combine(_directory, "ledger.lock"), wait.Token);
            var state = await ReadStateAsync(wait.Token);
            var transaction = update(state);
            if (transaction.Save) await WriteStateAsync(state, wait.Token);
            return transaction.Result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Exhausted();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw InvalidState();
        }
    }

    private async Task<GuardrailState> ReadStateAsync(CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(_statePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                4096, FileOptions.Asynchronous);
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        await using var ownedStream = stream;
        if (stream.Length > MaximumStateBytes) throw InvalidState();
        using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 12 }, cancellationToken);
        var root = document.RootElement;
        RequireObject(root, "version", "providerAttempts", "contentReads", "detailReads", "output", "scopes");
        var scopes = root.GetProperty("scopes");
        RequireObject(scopes);
        foreach (var scope in scopes.EnumerateObject())
            RequireObject(scope.Value, "attempts", "lastAttempt", "cooldownUntil", "cooldownKind");
        var output = root.GetProperty("output");
        if (output.ValueKind != JsonValueKind.Array) throw InvalidState();
        foreach (var charge in output.EnumerateArray()) RequireObject(charge, "at", "bytes");
        var state = root.Deserialize<GuardrailState>(JsonOptions) ?? throw InvalidState();
        ValidateState(state);
        return state;
    }

    private static void RequireObject(JsonElement element, params string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidState();
        var properties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!properties.Add(property.Name)) throw InvalidState();
        if (required.Any(name => !properties.Contains(name))) throw InvalidState();
    }

    private async Task WriteStateAsync(GuardrailState state, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        if (bytes.Length > MaximumStateBytes) throw Exhausted();
        await WriteAtomicAsync(_directory, _statePath, bytes, cancellationToken);
    }

    private static async Task WriteAtomicAsync(string directory, string targetPath, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, targetPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<FileStream> OpenExclusiveAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    1, FileOptions.Asynchronous);
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33 or 11)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }
    }
}
