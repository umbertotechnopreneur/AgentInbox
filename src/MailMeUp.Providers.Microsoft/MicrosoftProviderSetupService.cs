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

using MailMeUp.Core;

namespace MailMeUp.Providers.Microsoft;

/// <summary>Saves the public client identifier for a Microsoft desktop app registration.</summary>
public sealed class MicrosoftProviderSetupService(
    IProviderConfigurationStore configurations,
    IAccountStore accounts) : IProviderSetupService
{
    /// <inheritdoc />
    public string ProviderId => "microsoft";

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
        var clientId = source.Trim();
        if (!Guid.TryParseExact(clientId, "D", out var parsedClientId))
        {
            throw new ArgumentException("The Microsoft Application (client) ID must be a GUID.", nameof(source));
        }

        var canonicalClientId = parsedClientId.ToString("D");
        var previous = await configurations.GetAsync(ProviderId, cancellationToken);
        if (previous is not null &&
            !string.Equals(previous.ClientId, canonicalClientId, StringComparison.Ordinal) &&
            (await accounts.ListAsync(cancellationToken)).Any(account =>
                string.Equals(account.Provider, ProviderId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Remove connected Microsoft accounts before changing the app registration.");
        }

        var configuration = new ProviderConfiguration(ProviderId, canonicalClientId, ClientSecretReference: null);
        await configurations.SaveAsync(configuration, cancellationToken);
        return new ProviderSetupResult(CreateStatus(configuration), SourceRetained: false);
    }

    private static ProviderSetupStatus CreateStatus(ProviderConfiguration? configuration) => new(
        "microsoft",
        configuration is not null,
        configuration is null ? null : CreateClientIdHint(configuration.ClientId),
        ProtectedSecretConfigured: false);

    private static string CreateClientIdHint(string clientId) => clientId.Length <= 12 ? clientId : $"…{clientId[^12..]}";
}
