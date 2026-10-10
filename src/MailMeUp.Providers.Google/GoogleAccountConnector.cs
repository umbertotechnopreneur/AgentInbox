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

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using MailMeUp.Core;
using MailMeUp.Security;

namespace MailMeUp.Providers.Google;

/// <summary>Connects multiple Google accounts with read-only OAuth scopes and protected token slots.</summary>
public sealed class GoogleAccountConnector(IProviderConfigurationStore configurations, ISecretStore secrets) : IAccountConnector
{
    private const string MailScope = "https://www.googleapis.com/auth/gmail.readonly";
    private const string CalendarListScope = "https://www.googleapis.com/auth/calendar.calendarlist.readonly";
    private const string CalendarEventsScope = "https://www.googleapis.com/auth/calendar.events.readonly";
    private static readonly HttpClient HttpClient = new();

    /// <inheritdoc />
    public string ProviderId => "google";

    /// <inheritdoc />
    public async Task<AccountConnectionResult> ConnectAsync(
        AccountConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var configuration = await configurations.GetAsync(ProviderId, cancellationToken)
            ?? throw new ProviderAuthenticationException("Configure the Google Desktop app before connecting an account.");
        if (string.IsNullOrWhiteSpace(configuration.ClientSecretReference))
        {
            throw new ProviderAuthenticationException("The protected Google client credential is missing.");
        }

        var clientSecretBytes = await secrets.ReadAsync(configuration.ClientSecretReference, cancellationToken)
            ?? throw new ProviderAuthenticationException("The protected Google client credential is missing.");
        var temporarySlot = $"pending-{Guid.NewGuid():N}";
        var temporaryStore = new ProtectedGoogleTokenStore(secrets, temporarySlot, cancellationToken);
        try
        {
            var clientSecret = Encoding.UTF8.GetString(clientSecretBytes);
            var scopes = CreateScopes(options);
            var initializer = new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = configuration.ClientId,
                    ClientSecret = clientSecret
                },
                Prompt = "consent select_account"
            };

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                initializer,
                scopes,
                temporarySlot,
                usePkce: true,
                taskCancellationToken: cancellationToken,
                dataStore: temporaryStore);
            if (string.IsNullOrWhiteSpace(credential.Token.RefreshToken))
            {
                throw new ProviderAuthenticationException("Google did not grant offline access. Connect the account again and approve the requested access.");
            }

            var profile = await ReadProfileAsync(credential.Token, cancellationToken);
            var accountId = $"google:{profile.Subject}";
            var grantedScopes = ParseScopes(credential.Token.Scope);
            var account = new Account(
                accountId,
                ProviderId,
                profile.DisplayName,
                profile.EmailAddress,
                options.IncludeMail && IsGranted(grantedScopes, MailScope),
                options.IncludeCalendar && IsGranted(grantedScopes, CalendarListScope) && IsGranted(grantedScopes, CalendarEventsScope));
            if (!account.MailReadEnabled && !account.CalendarReadEnabled)
            {
                throw new ProviderAuthenticationException("Google did not grant any requested mail or calendar read access. The existing local connection was preserved.");
            }

            var stableStore = new ProtectedGoogleTokenStore(secrets, accountId, cancellationToken);
            using (await GoogleCredentialSession.AcquireAsync(secrets, accountId, cancellationToken))
            {
                await stableStore.StoreAsync(accountId, credential.Token);
            }

            await TryClearAsync(temporaryStore);
            return new AccountConnectionResult(account);
        }
        catch (OperationCanceledException)
        {
            await TryClearAsync(temporaryStore);
            throw;
        }
        catch (ProviderAuthenticationException)
        {
            await TryClearAsync(temporaryStore);
            throw;
        }
        catch (Exception)
        {
            await TryClearAsync(temporaryStore);
            throw new ProviderAuthenticationException("Google sign-in could not be completed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clientSecretBytes);
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(Account account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!string.Equals(account.Provider, ProviderId, StringComparison.Ordinal) ||
            !account.Id.StartsWith("google:", StringComparison.Ordinal))
        {
            throw new ArgumentException("The account does not belong to Google.", nameof(account));
        }

        try
        {
            // Refresh uses the same cross-process lease and cannot restore a token after local removal.
            using var session = await GoogleCredentialSession.AcquireAsync(secrets, account.Id, cancellationToken);
            await new ProtectedGoogleTokenStore(secrets, account.Id, cancellationToken).DeleteAsync<TokenResponse>(account.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ProviderAuthenticationException("The local Google credential could not be removed.");
        }
    }

    private static IReadOnlyList<string> CreateScopes(AccountConnectionOptions options)
    {
        var scopes = new List<string> { "openid", "email", "profile" };
        if (options.IncludeMail)
        {
            scopes.Add(MailScope);
        }

        if (options.IncludeCalendar)
        {
            scopes.Add(CalendarListScope);
            scopes.Add(CalendarEventsScope);
        }

        return scopes;
    }

    private static async Task<GoogleProfile> ReadProfileAsync(TokenResponse token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new ProviderAuthenticationException("Google did not return an access token.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderAuthenticationException("Google account identity could not be read.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var subject = GetRequiredProfileValue(document.RootElement, "sub");
        var email = GetRequiredProfileValue(document.RootElement, "email");
        var name = document.RootElement.TryGetProperty("name", out var nameProperty) &&
                   nameProperty.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(nameProperty.GetString())
            ? nameProperty.GetString()!
            : email;
        return new GoogleProfile(subject, email, name);
    }

    private static string GetRequiredProfileValue(JsonElement profile, string propertyName)
    {
        if (!profile.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new ProviderAuthenticationException("Google returned an incomplete account identity.");
        }

        return property.GetString()!;
    }

    private static HashSet<string>? ParseScopes(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private static bool IsGranted(IReadOnlySet<string>? grantedScopes, string scope) =>
        grantedScopes is null || grantedScopes.Contains(scope);

    private static async Task TryClearAsync(ProtectedGoogleTokenStore store)
    {
        try
        {
            // Cleanup must still run after sign-in cancellation; protected-store lock waits are bounded.
            await store.ClearAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is SecretStoreException or ArgumentException)
        {
            // Best-effort cleanup preserves the original sign-in result or failure.
        }
    }

    private sealed record GoogleProfile(string Subject, string EmailAddress, string DisplayName);
}
