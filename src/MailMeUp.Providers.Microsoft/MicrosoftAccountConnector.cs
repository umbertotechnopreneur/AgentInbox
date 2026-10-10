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
using System.Text.Json;
using MailMeUp.Core;
using MailMeUp.Security;
using Microsoft.Identity.Client;

namespace MailMeUp.Providers.Microsoft;

/// <summary>Connects multiple Microsoft accounts with delegated read-only scopes and a protected MSAL cache.</summary>
public sealed class MicrosoftAccountConnector(IProviderConfigurationStore configurations, ISecretStore secrets) : IAccountConnector
{
    private const string AccountIdPrefix = "microsoft:";
    private const string MailScope = "Mail.Read";
    private const string CalendarScope = "Calendars.Read";
    private static readonly HttpClient HttpClient = new();

    /// <inheritdoc />
    public string ProviderId => "microsoft";

    /// <inheritdoc />
    public async Task<AccountConnectionResult> ConnectAsync(
        AccountConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var configuration = await configurations.GetAsync(ProviderId, cancellationToken)
            ?? throw new ProviderAuthenticationException("Configure the Microsoft desktop app before connecting an account.");

        try
        {
            return await MicrosoftIdentitySession.RunAsync(
                configuration.ClientId,
                secrets,
                async application =>
                {
                    var result = await application.AcquireTokenInteractive(CreateScopes(options))
                        .WithPrompt(Prompt.SelectAccount)
                        .WithUseEmbeddedWebView(false)
                        .ExecuteAsync(cancellationToken);
                    var profile = await ReadProfileAsync(result.AccessToken, cancellationToken);
                    var homeAccountId = result.Account?.HomeAccountId?.Identifier;
                    if (string.IsNullOrWhiteSpace(homeAccountId))
                    {
                        throw new ProviderAuthenticationException("Microsoft returned an incomplete account identity.");
                    }

                    var grantedScopes = result.Scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var account = new Account(
                        $"{AccountIdPrefix}{homeAccountId}",
                        ProviderId,
                        profile.DisplayName,
                        profile.EmailAddress,
                        options.IncludeMail && grantedScopes.Contains(MailScope),
                        options.IncludeCalendar && grantedScopes.Contains(CalendarScope));
                    if (!account.MailReadEnabled && !account.CalendarReadEnabled)
                    {
                        throw new ProviderAuthenticationException("Microsoft did not grant any requested mail or calendar read access. The existing local connection was preserved.");
                    }

                    return new AccountConnectionResult(account);
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProviderAuthenticationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ProviderAuthenticationException("Microsoft sign-in could not be completed.");
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(Account account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!string.Equals(account.Provider, ProviderId, StringComparison.Ordinal) ||
            !account.Id.StartsWith(AccountIdPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("The account does not belong to Microsoft.", nameof(account));
        }

        var configuration = await configurations.GetAsync(ProviderId, cancellationToken)
            ?? throw new ProviderAuthenticationException("The Microsoft app configuration is missing.");
        try
        {
            await MicrosoftIdentitySession.RunAsync(
                configuration.ClientId,
                secrets,
                async application =>
                {
                    var homeAccountId = account.Id[AccountIdPrefix.Length..];
                    var msalAccount = (await application.GetAccountsAsync())
                        .SingleOrDefault(item => string.Equals(item.HomeAccountId?.Identifier, homeAccountId, StringComparison.Ordinal));
                    if (msalAccount is not null)
                    {
                        await application.RemoveAsync(msalAccount);
                    }

                    return true;
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ProviderAuthenticationException("The local Microsoft credential could not be removed.");
        }
    }

    private static IReadOnlyList<string> CreateScopes(AccountConnectionOptions options)
    {
        var scopes = new List<string> { "User.Read" };
        if (options.IncludeMail)
        {
            scopes.Add(MailScope);
        }

        if (options.IncludeCalendar)
        {
            scopes.Add(CalendarScope);
        }

        return scopes;
    }

    private static async Task<MicrosoftProfile> ReadProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ProviderAuthenticationException("Microsoft did not return an access token.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://graph.microsoft.com/v1.0/me?$select=displayName,mail,userPrincipalName");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderAuthenticationException("Microsoft account identity could not be read.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var displayName = GetOptionalString(document.RootElement, "displayName");
        var email = GetOptionalString(document.RootElement, "mail") ?? GetOptionalString(document.RootElement, "userPrincipalName");
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ProviderAuthenticationException("Microsoft returned an incomplete account identity.");
        }

        return new MicrosoftProfile(email, string.IsNullOrWhiteSpace(displayName) ? email : displayName);
    }

    private static string? GetOptionalString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()
            : null;

    private sealed record MicrosoftProfile(string EmailAddress, string DisplayName);
}
