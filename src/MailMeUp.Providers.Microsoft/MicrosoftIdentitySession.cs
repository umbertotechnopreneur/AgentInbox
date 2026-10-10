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
using MailMeUp.Security;
using Microsoft.Identity.Client;

namespace MailMeUp.Providers.Microsoft;

internal static class MicrosoftIdentitySession
{
    private const string TokenCacheReference = "providers/microsoft/token-cache";

    public static async Task<T> RunAsync<T>(
        string clientId,
        ISecretStore secrets,
        Func<IPublicClientApplication, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var session = await secrets.AcquireSessionAsync(TokenCacheReference, cancellationToken);
        byte[]? cachedBytes = null;
        byte[]? updatedBytes = null;
        try
        {
            var application = PublicClientApplicationBuilder.Create(clientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
                .WithRedirectUri("http://localhost")
                .Build();
            cachedBytes = await secrets.ReadAsync(TokenCacheReference, cancellationToken);
            var cacheLoaded = false;
            application.UserTokenCache.SetBeforeAccessAsync(notification =>
            {
                if (!cacheLoaded)
                {
                    if (cachedBytes is not null)
                    {
                        notification.TokenCache.DeserializeMsalV3(cachedBytes, shouldClearExistingCache: true);
                    }

                    cacheLoaded = true;
                }

                return Task.CompletedTask;
            });
            application.UserTokenCache.SetAfterAccessAsync(notification =>
            {
                if (notification.HasStateChanged)
                {
                    if (updatedBytes is not null)
                    {
                        CryptographicOperations.ZeroMemory(updatedBytes);
                    }

                    updatedBytes = notification.TokenCache.SerializeMsalV3();
                }

                return Task.CompletedTask;
            });

            // Interactive sign-in can change the cache before account identity is validated.
            // Persist only a successful operation so a failed reconnect preserves existing accounts.
            var result = await operation(application);
            cancellationToken.ThrowIfCancellationRequested();
            if (updatedBytes is not null)
            {
                if (updatedBytes.Length == 0)
                {
                    await secrets.DeleteAsync(TokenCacheReference, cancellationToken);
                }
                else
                {
                    await secrets.WriteAsync(TokenCacheReference, updatedBytes, cancellationToken);
                }
            }

            return result;
        }
        finally
        {
            if (cachedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(cachedBytes);
            }

            if (updatedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(updatedBytes);
            }
        }
    }
}
