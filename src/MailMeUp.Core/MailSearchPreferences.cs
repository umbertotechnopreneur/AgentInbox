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

/// <summary>Local defaults for mail searches that do not specify a date.</summary>
public sealed record MailSearchPreferences(int DefaultLookbackDays = MailSearchPreferences.DefaultDays)
{
    /// <summary>The initial search period, in days, when no preference has been saved.</summary>
    public const int DefaultDays = 14;

    /// <summary>The shortest configurable default search period.</summary>
    public const int MinimumDays = 1;

    /// <summary>The longest configurable default search period; explicit dates can cover older mail.</summary>
    public const int MaximumDays = 365;

    /// <summary>Rejects invalid default periods before saving or searching.</summary>
    public void Validate()
    {
        if (DefaultLookbackDays is < MinimumDays or > MaximumDays)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultLookbackDays),
                $"The default mail search period must be between {MinimumDays} and {MaximumDays} days.");
        }
    }
}

/// <summary>Persists non-secret global mail search defaults across local UI and MCP processes.</summary>
public interface IMailSearchPreferencesStore
{
    /// <summary>Reloads saved defaults, or returns the initial two-week period when no file exists.</summary>
    Task<MailSearchPreferences> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces the local default period without writing provider data.</summary>
    Task SaveAsync(MailSearchPreferences preferences, CancellationToken cancellationToken = default);
}
