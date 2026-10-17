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

using MailMeUp.Security;

namespace MailMeUp.Providers.Google;

/// <summary>Acquires the protected credential-store session associated with a Google account.</summary>
internal static class GoogleCredentialSession
{
    /// <summary>Acquires exclusive credential access for a validated Google account.</summary>
    public static ValueTask<IDisposable> AcquireAsync(
        ISecretStore secrets,
        string accountId,
        CancellationToken cancellationToken) =>
        secrets.AcquireSessionAsync(ProtectedGoogleTokenStore.CreateReference(accountId), cancellationToken);
}
