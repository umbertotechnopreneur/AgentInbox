// SPDX-License-Identifier: MIT

/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailMeUp.Core;
using MailMeUp.Security;

namespace MailMeUp.Providers.Google;

/// <summary>Imports a Google desktop OAuth client while moving its secret into OS-protected storage.</summary>
public sealed class GoogleProviderSetupService(
    IProviderConfigurationStore configurations,
    ISecretStore secrets,
    IAccountStore accounts) : IProviderSetupService
{
    private const int MaximumClientFileBytes = 128 * 1024;

    /// <inheritdoc />
    public string ProviderId => "google";

    /// <inheritdoc />
    public async Task<ProviderSetupStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await configurations.GetAsync(ProviderId, cancellationToken);
        return CreateStatus(configuration);
    }

    /// <inheritdoc />
    public async Task<ProviderSetupResult> ConfigureAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!Path.IsPathFullyQualified(source))
        {
            throw new ArgumentException("The Google client configuration path must be absolute.", nameof(source));
        }

        var path = Path.GetFullPath(source);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > MaximumClientFileBytes)
        {
            throw new ArgumentException("The Google client configuration file is missing, empty, or too large.", nameof(source));
        }

        var jsonBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        byte[]? secretBytes = null;
        byte[]? persistedBytes = null;
        string? newReference = null;
        var newReferenceCommitted = false;
        try
        {
            using var document = JsonDocument.Parse(jsonBytes);
            if (!document.RootElement.TryGetProperty("installed", out var installed))
            {
                throw new ArgumentException("The Google configuration must be a Desktop app client.", nameof(source));
            }

            var clientId = GetRequiredString(installed, "client_id", source);
            var clientSecret = GetRequiredString(installed, "client_secret", source);
            ValidateClientId(clientId, source);
            ValidateEndpoint(installed, "auth_uri", "accounts.google.com", source);
            ValidateEndpoint(installed, "token_uri", "oauth2.googleapis.com", source);
            ValidateLoopbackRedirect(installed, source);

            var previous = await configurations.GetAsync(ProviderId, cancellationToken);
            if (previous is not null &&
                !string.Equals(previous.ClientId, clientId, StringComparison.Ordinal) &&
                (await accounts.ListAsync(cancellationToken)).Any(account =>
                    string.Equals(account.Provider, ProviderId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Remove connected Google accounts before changing the Desktop app registration.");
            }

            newReference = $"providers/google/client-secret/{Guid.NewGuid():N}";
            secretBytes = Encoding.UTF8.GetBytes(clientSecret);
            await secrets.WriteAsync(newReference, secretBytes, cancellationToken);
            persistedBytes = await secrets.ReadAsync(newReference, cancellationToken);
            if (persistedBytes is null || !CryptographicOperations.FixedTimeEquals(secretBytes, persistedBytes))
            {
                throw new SecretStoreException("The operating system credential store did not preserve the Google client credential.");
            }

            var configuration = new ProviderConfiguration(ProviderId, clientId, newReference);
            await configurations.SaveAsync(configuration, cancellationToken);
            newReferenceCommitted = true;

            if (!string.IsNullOrWhiteSpace(previous?.ClientSecretReference) &&
                !string.Equals(previous.ClientSecretReference, newReference, StringComparison.Ordinal))
            {
                await TryDeleteAsync(previous.ClientSecretReference);
            }

            return new ProviderSetupResult(CreateStatus(configuration), SourceRetained: true);
        }
        catch
        {
            if (newReference is not null && !newReferenceCommitted)
            {
                await TryDeleteAsync(newReference);
            }

            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(jsonBytes);
            if (secretBytes is not null)
            {
                CryptographicOperations.ZeroMemory(secretBytes);
            }

            if (persistedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(persistedBytes);
            }
        }
    }

    private async ValueTask TryDeleteAsync(string reference)
    {
        try
        {
            await secrets.DeleteAsync(reference, CancellationToken.None);
        }
        catch (Exception exception) when (exception is SecretStoreException or ArgumentException)
        {
            // Preserve the original setup failure. The opaque orphan cannot be located from public settings.
        }
    }

    private static ProviderSetupStatus CreateStatus(ProviderConfiguration? configuration) => new(
        "google",
        configuration is not null && !string.IsNullOrWhiteSpace(configuration.ClientSecretReference),
        configuration is null ? null : CreateClientIdHint(configuration.ClientId),
        !string.IsNullOrWhiteSpace(configuration?.ClientSecretReference));

    private static string CreateClientIdHint(string clientId) => clientId.Length <= 12 ? clientId : $"…{clientId[^12..]}";

    private static string GetRequiredString(JsonElement parent, string propertyName, string source)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new ArgumentException("The Google client configuration is incomplete.", nameof(source));
        }

        return property.GetString()!;
    }

    private static void ValidateClientId(string clientId, string source)
    {
        if (!clientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) || clientId.Length > 256)
        {
            throw new ArgumentException("The Google client identifier is invalid.", nameof(source));
        }
    }

    private static void ValidateEndpoint(JsonElement installed, string propertyName, string expectedHost, string source)
    {
        var value = GetRequiredString(installed, propertyName, source);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, expectedHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The Google client configuration contains an unexpected OAuth endpoint.", nameof(source));
        }
    }

    private static void ValidateLoopbackRedirect(JsonElement installed, string source)
    {
        if (!installed.TryGetProperty("redirect_uris", out var redirects) || redirects.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("The Google desktop client has no loopback redirect.", nameof(source));
        }

        var valid = redirects.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.String &&
            Uri.TryCreate(item.GetString(), UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttp &&
            (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1"));
        if (!valid)
        {
            throw new ArgumentException("The Google desktop client has no supported loopback redirect.", nameof(source));
        }
    }
}
