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
using System.Text.Json;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using MailMeUp.Security;

namespace MailMeUp.Providers.Google;

internal sealed class ProtectedGoogleTokenStore(
    ISecretStore secrets,
    string slot,
    CancellationToken cancellationToken = default) : IDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _slot = ValidateSlot(slot);
    private readonly string _reference = CreateReference(slot);

    public async Task StoreAsync<T>(string key, T value)
    {
        ValidateOperation<T>(key);
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try
        {
            await secrets.WriteAsync(_reference, bytes, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public async Task DeleteAsync<T>(string key)
    {
        ValidateOperation<T>(key);
        await secrets.DeleteAsync(_reference, cancellationToken);
    }

    public async Task<T> GetAsync<T>(string key)
    {
        ValidateOperation<T>(key);
        var bytes = await secrets.ReadAsync(_reference, cancellationToken);
        if (bytes is null)
        {
            return default!;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new SecretStoreException("The protected Google token cache is invalid.");
        }
        catch (JsonException exception)
        {
            throw new SecretStoreException("The protected Google token cache is invalid.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public Task ClearAsync() => ClearAsync(cancellationToken);

    internal Task ClearAsync(CancellationToken cleanupCancellationToken) =>
        secrets.DeleteAsync(_reference, cleanupCancellationToken).AsTask();

    internal static string CreateReference(string slot) => $"providers/google/token-cache/{ValidateSlot(slot)}";

    private static string ValidateSlot(string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        if (slot.Length > 512)
        {
            throw new ArgumentException("Google token slots cannot exceed 512 characters.", nameof(slot));
        }

        return slot;
    }

    private void ValidateOperation<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!string.Equals(key, _slot, StringComparison.Ordinal))
        {
            throw new ArgumentException("The Google token key does not match its protected slot.", nameof(key));
        }

        if (typeof(T) != typeof(TokenResponse))
        {
            throw new NotSupportedException("The Google token store accepts token responses only.");
        }
    }
}
