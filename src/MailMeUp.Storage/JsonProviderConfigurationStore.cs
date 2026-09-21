using System.Text.Json;
using MailMeUp.Core;

namespace MailMeUp.Storage;

/// <summary>Stores non-secret provider app identities in a small per-user JSON file.</summary>
public sealed class JsonProviderConfigurationStore(string directory) : IProviderConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private readonly string _filePath = Path.Combine(DataDirectory.Resolve(directory), "provider-settings.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderConfiguration>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnsafeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken = default)
    {
        ValidateProviderId(providerId);
        var configurations = await ListAsync(cancellationToken);
        return configurations.SingleOrDefault(item => string.Equals(item.ProviderId, providerId, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async Task SaveAsync(ProviderConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateProviderId(configuration.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.ClientId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var configurations = (await ReadUnsafeAsync(cancellationToken)).ToList();
            configurations.RemoveAll(item => string.Equals(item.ProviderId, configuration.ProviderId, StringComparison.Ordinal));
            configurations.Add(configuration);
            configurations.Sort((left, right) => StringComparer.Ordinal.Compare(left.ProviderId, right.ProviderId));

            var parent = Path.GetDirectoryName(_filePath)!;
            CreatePrivateDirectory(parent);
            var temporaryPath = Path.Combine(parent, $"provider-settings.{Guid.NewGuid():N}.tmp");
            try
            {
                var document = new ConfigurationDocument(1, configurations);
                await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(document, JsonOptions), cancellationToken);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                File.Move(temporaryPath, _filePath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ProviderConfiguration>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var document = await JsonSerializer.DeserializeAsync<ConfigurationDocument>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Provider settings are empty or invalid.");
        if (document.SchemaVersion != 1)
        {
            throw new InvalidOperationException("Unsupported provider settings version. Upgrade AgentInbox or select a new data directory.");
        }

        if (document.Providers.Select(item => item.ProviderId).Distinct(StringComparer.Ordinal).Count() != document.Providers.Count)
        {
            throw new InvalidOperationException("Provider settings contain duplicate provider identifiers.");
        }

        return document.Providers.OrderBy(item => item.ProviderId, StringComparer.Ordinal).ToArray();
    }

    private static void ValidateProviderId(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (providerId.Any(character => !char.IsAsciiLetterLower(character)))
        {
            throw new ArgumentException("Provider identifiers must contain lowercase ASCII letters only.", nameof(providerId));
        }
    }

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

    private sealed record ConfigurationDocument(int SchemaVersion, List<ProviderConfiguration> Providers);
}
