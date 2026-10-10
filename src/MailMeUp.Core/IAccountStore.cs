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

namespace MailMeUp.Core;

/// <summary>Persists local account metadata independently of provider credentials.</summary>
public interface IAccountStore
{
    /// <summary>Lists accounts in stable local identifier order.</summary>
    Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves metadata for an account after the authentication layer establishes its identity.</summary>
    Task SaveAsync(Account account, CancellationToken cancellationToken = default);

    /// <summary>Removes one local account record and returns whether it existed.</summary>
    Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default);
}
