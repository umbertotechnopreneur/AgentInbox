using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Identity.Client.Extensions.Msal;
using ProtectedStorage = Microsoft.Identity.Client.Extensions.Msal.Storage;

namespace MailMeUp.Security;

/// <summary>Protects credential blobs with DPAPI, macOS Keychain, or Linux Secret Service.</summary>
public sealed class OsProtectedSecretStore(string directory) : ISecretStore
{
    private const string KeychainService = "com.agentinbox.credentials";
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
    private readonly string _directory = Path.Combine(Path.GetFullPath(directory), "credentials");
    private readonly string _profileId = Hash(Path.GetFullPath(directory))[..16];

    /// <inheritdoc />
    public async ValueTask<IDisposable> AcquireSessionAsync(string reference, CancellationToken cancellationToken = default)
    {
        var key = ValidateAndHash(reference);
        try
        {
            CreatePrivateDirectory(_directory);
            // Session leases must use another file than individual protected-store operations.
            // Otherwise a session would deadlock when it reads or writes its own credential.
            return await AcquireFileLockAsync($"session-{key}", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SecretStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not ArgumentException)
        {
            throw new SecretStoreException("The protected credential session could not be acquired.", exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> ReadAsync(string reference, CancellationToken cancellationToken = default)
    {
        var key = ValidateAndHash(reference);
        return await ExecuteAsync(key, storage =>
        {
            var data = storage.ReadData();
            return data is null || data.Length == 0 ? null : data;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(string reference, ReadOnlyMemory<byte> secret, CancellationToken cancellationToken = default)
    {
        if (secret.IsEmpty)
        {
            throw new ArgumentException("Credential material cannot be empty.", nameof(secret));
        }

        var key = ValidateAndHash(reference);
        var copy = secret.ToArray();
        try
        {
            await ExecuteAsync(key, storage =>
            {
                storage.WriteData(copy);
                return true;
            }, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copy);
        }
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        var key = ValidateAndHash(reference);
        await ExecuteAsync(key, storage =>
        {
            storage.Clear();
            return true;
        }, cancellationToken);
    }

    private async ValueTask<T> ExecuteAsync<T>(string key, Func<ProtectedStorage, T> operation, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd($"{_profileId}:{key}", static _ => new SemaphoreSlim(1, 1));
        var wait = Stopwatch.StartNew();
        if (!await gate.WaitAsync(LockWaitTimeout, cancellationToken))
        {
            throw new SecretStoreException("The protected credential store is busy or unavailable. Try again later.");
        }

        try
        {
            CreatePrivateDirectory(_directory);
            await using var fileLock = await AcquireFileLockAsync(key, cancellationToken, LockWaitTimeout - wait.Elapsed);
            var trace = new TraceSource("AgentInbox.ProtectedStorage", SourceLevels.Off);
            try
            {
                trace.Listeners.Clear();
                var storage = ProtectedStorage.Create(CreateProperties(key), trace);
                return operation(storage);
            }
            finally
            {
                trace.Close();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SecretStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not ArgumentException)
        {
            throw new SecretStoreException("The operating system credential store is unavailable.", exception);
        }
        finally
        {
            gate.Release();
        }
    }

    private StorageCreationProperties CreateProperties(string key) =>
        new StorageCreationPropertiesBuilder($"{key}.bin", _directory)
            .WithMacKeyChain(KeychainService, $"{_profileId}:{key}")
            .WithLinuxKeyring(
                KeychainService,
                "default",
                "AgentInbox protected credentials",
                new KeyValuePair<string, string>("application", "AgentInbox"),
                new KeyValuePair<string, string>("profile", $"{_profileId}:{key}"))
            .Build();

    private async ValueTask<FileStream> AcquireFileLockAsync(
        string key,
        CancellationToken cancellationToken,
        TimeSpan? maximumWait = null)
    {
        var wait = Stopwatch.StartNew();
        var timeout = maximumWait ?? LockWaitTimeout;
        var lockDirectory = Path.Combine(_directory, "locks");
        CreatePrivateDirectory(lockDirectory);
        var lockPath = Path.Combine(lockDirectory, $"{key}.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (wait.Elapsed >= timeout)
            {
                throw new SecretStoreException("The protected credential store is busy or unavailable. Try again later.");
            }

            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                var remaining = timeout - wait.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100), cancellationToken);
                }
            }
        }
    }

    private static string ValidateAndHash(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (reference.Length > 512)
        {
            throw new ArgumentException("Credential references cannot exceed 512 characters.", nameof(reference));
        }

        return Hash(reference);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
