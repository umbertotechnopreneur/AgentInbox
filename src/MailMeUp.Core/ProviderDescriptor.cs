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

/// <summary>Reports a provider's actual implementation readiness.</summary>
public sealed record ProviderDescriptor(string Id, string DisplayName, bool AuthenticationAvailable, bool MailReadAvailable, bool CalendarReadAvailable);

/// <summary>Exposes provider readiness without pretending that mail operations are implemented.</summary>
public interface IProviderModule
{
    /// <summary>Gets the module's public capabilities.</summary>
    ProviderDescriptor Descriptor { get; }
}
