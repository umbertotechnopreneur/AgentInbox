using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailMeUp.Providers.Microsoft;
using MailMeUp.Security;
using Microsoft.Identity.Client;
using Xunit;

namespace MailMeUp.Tests;

public sealed class CredentialSessionTests : IDisposable
{
    private const string SyntheticReference = "tests/synthetic-session-example.test";
    private const string MicrosoftClientId = "00000000-0000-0000-0000-000000000001";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AgentInbox.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SeparateStoreInstancesExcludeEachOtherUntilTheLeaseIsDisposed()
    {
        var firstStore = new OsProtectedSecretStore(_directory);
        var secondStore = new OsProtectedSecretStore(_directory);
        using (await firstStore.AcquireSessionAsync(SyntheticReference))
        {
            using var cancelledWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                using var rejected = await secondStore.AcquireSessionAsync(SyntheticReference, cancelledWait.Token);
            });
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var next = await secondStore.AcquireSessionAsync(SyntheticReference, deadline.Token);
    }

    [Fact]
    public async Task CancelledWaitDoesNotReleaseAnotherCallersLease()
    {
        var store = new OsProtectedSecretStore(_directory);
        using var owner = await store.AcquireSessionAsync(SyntheticReference);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                using var rejected = await new OsProtectedSecretStore(_directory)
                    .AcquireSessionAsync(SyntheticReference, deadline.Token);
            });
        }
    }

    [Fact]
    public async Task DifferentProfilesAndReferencesHaveIndependentSessions()
    {
        var store = new OsProtectedSecretStore(_directory);
        using var held = await store.AcquireSessionAsync(SyntheticReference);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var otherReference = await store.AcquireSessionAsync("tests/other-example.test", deadline.Token);
        using var otherProfile = await new OsProtectedSecretStore(Path.Combine(_directory, "other-profile"))
            .AcquireSessionAsync(SyntheticReference, deadline.Token);
    }

    [WindowsOnlyFact]
    public async Task WindowsProtectedIoCanCompleteWhileItsSessionLeaseIsHeld()
    {
        var store = new OsProtectedSecretStore(_directory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var session = await store.AcquireSessionAsync(SyntheticReference, deadline.Token);
        var syntheticBytes = Encoding.UTF8.GetBytes("synthetic-example.test-value");
        byte[]? restored = null;
        try
        {
            await store.WriteAsync(SyntheticReference, syntheticBytes, deadline.Token);
            restored = await store.ReadAsync(SyntheticReference, deadline.Token);
            Assert.Equal(syntheticBytes, restored);
            await store.DeleteAsync(SyntheticReference, deadline.Token);
            Assert.Null(await store.ReadAsync(SyntheticReference, deadline.Token));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(syntheticBytes);
            if (restored is not null)
            {
                CryptographicOperations.ZeroMemory(restored);
            }
        }
    }

    [WindowsOnlyFact]
    public async Task WindowsSessionWaitsForALeaseHeldByAnotherProcess()
    {
        var store = new OsProtectedSecretStore(_directory);
        using (await store.AcquireSessionAsync(SyntheticReference))
        {
            // Materialize only the synthetic session's empty lock file.
        }

        var lockPath = Assert.Single(Directory.EnumerateFiles(Path.Combine(_directory, "credentials", "locks"), "*.lock"));
        var helperPath = Path.Combine(_directory, "hold-synthetic-lock.ps1");
        await File.WriteAllTextAsync(helperPath, """
            param([string]$LockPath)
            $ErrorActionPreference = 'Stop'
            $lease = [System.IO.File]::Open($LockPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            try {
                [Console]::Out.WriteLine('locked')
                [Console]::Out.Flush()
                [Console]::In.ReadLine() | Out-Null
            }
            finally {
                $lease.Dispose()
            }
            """);
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helperPath, "-LockPath", lockPath
                 })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The synthetic lock helper could not start.");
        try
        {
            var signal = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (signal is null)
            {
                var error = await process.StandardError.ReadToEndAsync();
                Assert.Fail($"The synthetic lock helper exited before signaling readiness: {error}");
            }
            Assert.Equal("locked", signal);
            using (var blockedWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(250)))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                {
                    using var rejected = await store.AcquireSessionAsync(SyntheticReference, blockedWait.Token);
                });
            }

            await process.StandardInput.WriteLineAsync("release");
            await process.StandardInput.FlushAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, process.ExitCode);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var acquired = await store.AcquireSessionAsync(SyntheticReference, deadline.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Fact]
    public async Task UnsupportedCredentialStoreFailsClosedForSessions()
    {
        ISecretStore store = new StoreWithoutSessions();
        await Assert.ThrowsAsync<SecretStoreException>(async () =>
        {
            using var unexpected = await store.AcquireSessionAsync(SyntheticReference);
        });
    }

    [Fact]
    public async Task FailedMicrosoftSessionDoesNotPersistAnAccountRemoval()
    {
        using var store = new MemorySessionStore(CreateSyntheticMicrosoftCache());
        var original = store.Snapshot();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => RunMicrosoftSessionAsync<bool>(store, async application =>
        {
            var accounts = (await application.GetAccountsAsync()).ToArray();
            Assert.Equal(2, accounts.Length);
            await application.RemoveAsync(accounts.Single(account => account.Username == "first@example.test"));
            Assert.Single(await application.GetAccountsAsync());
            throw new InvalidOperationException("Synthetic profile validation failure.");
        }));

        Assert.Equal("Synthetic profile validation failure.", failure.Message);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(0, store.DeleteCount);
        Assert.Equal(original, store.Snapshot());
        var retained = await RunMicrosoftSessionAsync(store, async application =>
            (await application.GetAccountsAsync()).Select(account => account.Username).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.Equal(["first@example.test", "second@example.test"], retained);
    }

    [Fact]
    public async Task SuccessfulMicrosoftSessionPersistsOnlyTheRequestedRemoval()
    {
        using var store = new MemorySessionStore(CreateSyntheticMicrosoftCache());
        await RunMicrosoftSessionAsync(store, async application =>
        {
            var removed = (await application.GetAccountsAsync()).Single(account => account.Username == "first@example.test");
            await application.RemoveAsync(removed);
            return true;
        });

        Assert.Equal(1, store.WriteCount);
        Assert.Equal(0, store.DeleteCount);
        var retained = await RunMicrosoftSessionAsync(store, async application =>
            (await application.GetAccountsAsync()).Select(account => account.Username).ToArray());
        Assert.Equal(["second@example.test"], retained);
    }

    private static Task<T> RunMicrosoftSessionAsync<T>(ISecretStore store, Func<IPublicClientApplication, Task<T>> operation)
    {
        var type = typeof(MicrosoftAccountConnector).Assembly.GetType("MailMeUp.Providers.Microsoft.MicrosoftIdentitySession", throwOnError: true)!;
        var method = type.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!.MakeGenericMethod(typeof(T));
        return (Task<T>)method.Invoke(null, [MicrosoftClientId, store, operation, CancellationToken.None])!;
    }

    private static byte[] CreateSyntheticMicrosoftCache()
    {
        const string tenant = "11111111-1111-1111-1111-111111111111";
        const string environment = "login.microsoftonline.com";
        var accounts = new Dictionary<string, object>(StringComparer.Ordinal);
        var refreshTokens = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var name in new[] { "first", "second" })
        {
            var homeAccountId = $"{name}.{tenant}";
            var clientInfo = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { uid = name, utid = tenant }))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            accounts[$"{homeAccountId}-{environment}-{tenant}"] = new
            {
                home_account_id = homeAccountId,
                environment,
                realm = tenant,
                local_account_id = name,
                username = $"{name}@example.test",
                authority_type = "MSSTS",
                name = $"Synthetic {name}",
                client_info = clientInfo
            };
            refreshTokens[$"{homeAccountId}-{environment}-refreshtoken-{MicrosoftClientId}--"] = new
            {
                home_account_id = homeAccountId,
                environment,
                credential_type = "RefreshToken",
                client_id = MicrosoftClientId,
                secret = $"synthetic-{name}-example.test-token"
            };
        }

        return JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["Account"] = accounts,
            ["RefreshToken"] = refreshTokens
        });
    }

    public void Dispose()
    {
        // The fixture owns only this randomly generated directory under the OS test directory.
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AgentInbox.Tests")) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(_directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The synthetic credential directory is outside the test root.");
        }

        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }

    private sealed class StoreWithoutSessions : ISecretStore
    {
        public ValueTask<byte[]?> ReadAsync(string reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<byte[]?>(null);
        public ValueTask WriteAsync(string reference, ReadOnlyMemory<byte> secret, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
        public ValueTask DeleteAsync(string reference, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class MemorySessionStore(byte[] initial) : ISecretStore, IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private byte[] _bytes = initial;
        public int WriteCount { get; private set; }
        public int DeleteCount { get; private set; }
        public byte[] Snapshot() => _bytes.ToArray();

        public async ValueTask<IDisposable> AcquireSessionAsync(string reference, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            return new Session(_gate);
        }

        public ValueTask<byte[]?> ReadAsync(string reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<byte[]?>(_bytes.ToArray());

        public ValueTask WriteAsync(string reference, ReadOnlyMemory<byte> secret, CancellationToken cancellationToken = default)
        {
            WriteCount++;
            CryptographicOperations.ZeroMemory(_bytes);
            _bytes = secret.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(string reference, CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            CryptographicOperations.ZeroMemory(_bytes);
            _bytes = [];
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(_bytes);
            _gate.Dispose();
        }

        private sealed class Session(SemaphoreSlim gate) : IDisposable
        {
            private SemaphoreSlim? _gate = gate;
            public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }
}

internal sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "This scenario requires Windows; macOS and Linux are not validated for this MVP.";
        }
    }
}
