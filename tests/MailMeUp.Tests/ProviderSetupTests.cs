using MailMeUp.Core;
using MailMeUp.Providers.Google;
using MailMeUp.Providers.Microsoft;
using MailMeUp.Security;
using Xunit;

namespace MailMeUp.Tests;

public sealed class ProviderSetupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AgentInbox.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GoogleDesktopConfigurationStoresItsSecretBehindAnOpaqueReference()
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, "client_secret.json");
        await File.WriteAllTextAsync(source, """
            {
              "installed": {
                "client_id": "123456789.apps.googleusercontent.com",
                "client_secret": "synthetic-secret",
                "auth_uri": "https://accounts.google.com/o/oauth2/auth",
                "token_uri": "https://oauth2.googleapis.com/token",
                "redirect_uris": ["http://localhost"]
              }
            }
            """);
        var configurations = new MemoryProviderConfigurationStore();
        var secrets = new MemorySecretStore();
        var service = new GoogleProviderSetupService(configurations, secrets, new MemoryAccountStore());

        var result = await service.ConfigureAsync(source);

        Assert.True(result.Status.Configured);
        Assert.True(result.Status.ProtectedSecretConfigured);
        Assert.True(result.SourceRetained);
        var configuration = Assert.Single(await configurations.ListAsync());
        Assert.Equal("123456789.apps.googleusercontent.com", configuration.ClientId);
        Assert.NotNull(configuration.ClientSecretReference);
        Assert.DoesNotContain("synthetic-secret", configuration.ClientSecretReference, StringComparison.Ordinal);
        var protectedValue = await secrets.ReadAsync(configuration.ClientSecretReference);
        Assert.NotNull(protectedValue);
        Assert.Equal("synthetic-secret", System.Text.Encoding.UTF8.GetString(protectedValue));
    }

    [Fact]
    public async Task MicrosoftConfigurationStoresOnlyTheCanonicalPublicClientId()
    {
        var configurations = new MemoryProviderConfigurationStore();
        var service = new MicrosoftProviderSetupService(configurations, new MemoryAccountStore());

        var result = await service.ConfigureAsync("B4D25C28-C785-4D02-BAD3-CAD165EA1E96");

        Assert.True(result.Status.Configured);
        Assert.False(result.Status.ProtectedSecretConfigured);
        Assert.False(result.SourceRetained);
        var configuration = Assert.Single(await configurations.ListAsync());
        Assert.Equal("b4d25c28-c785-4d02-bad3-cad165ea1e96", configuration.ClientId);
        Assert.Null(configuration.ClientSecretReference);
    }

    [Fact]
    public async Task WindowsProtectedStoreRoundTripsWithoutWritingPlaintext()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protectedDirectory = Path.Combine(_directory, "protected-store");
        var store = new OsProtectedSecretStore(protectedDirectory);
        const string reference = "tests/synthetic-token";
        const string value = "synthetic-token-value-example.test";
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        try
        {
            await store.WriteAsync(reference, bytes);
            var restored = await store.ReadAsync(reference);
            Assert.NotNull(restored);
            Assert.Equal(value, System.Text.Encoding.UTF8.GetString(restored));
            Assert.All(
                Directory.EnumerateFiles(protectedDirectory, "*", SearchOption.AllDirectories),
                file => Assert.DoesNotContain(value, System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)), StringComparison.Ordinal));

            await store.DeleteAsync(reference);
            Assert.Null(await store.ReadAsync(reference));
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class MemoryProviderConfigurationStore : IProviderConfigurationStore
    {
        private readonly Dictionary<string, ProviderConfiguration> _items = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<ProviderConfiguration>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderConfiguration>>(_items.Values.ToArray());

        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.GetValueOrDefault(providerId));

        public Task SaveAsync(ProviderConfiguration configuration, CancellationToken cancellationToken = default)
        {
            _items[configuration.ProviderId] = configuration;
            return Task.CompletedTask;
        }
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, byte[]> _items = new(StringComparer.Ordinal);

        public ValueTask<byte[]?> ReadAsync(string reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<byte[]?>(_items.TryGetValue(reference, out var value) ? value.ToArray() : null);

        public ValueTask WriteAsync(
            string reference,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken = default)
        {
            _items[reference] = secret.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(string reference, CancellationToken cancellationToken = default)
        {
            _items.Remove(reference);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MemoryAccountStore : IAccountStore
    {
        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Account>>([]);

        public Task SaveAsync(Account account, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
