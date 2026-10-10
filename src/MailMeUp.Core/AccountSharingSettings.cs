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

/// <summary>Local assistant access choices, independent of the permissions granted to a provider.</summary>
public sealed record AccountSharingSettings(
    string AccountId,
    bool Enabled = true,
    bool ShareMail = true,
    bool ShareCalendars = true,
    IReadOnlyList<string>? CalendarIds = null)
{
    /// <summary>Reports whether this calendar is selected; null selects all calendars and an empty list selects none.</summary>
    public bool AllowsCalendar(string providerCalendarId) =>
        Enabled && ShareCalendars && (CalendarIds is null || CalendarIds.Contains(providerCalendarId, StringComparer.Ordinal));

    /// <summary>Validates bounded local settings before they are persisted or used for authorization.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AccountId);
        if (AccountId.Length > 1_024 || CalendarIds is { Count: > 100 } ||
            CalendarIds?.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 2_048) == true)
        {
            throw new ArgumentException("Account sharing settings contain an invalid account or calendar selection.");
        }
    }
}

/// <summary>Persists local sharing choices and reloads them across independent UI and MCP processes.</summary>
public interface IAccountSharingStore
{
    /// <summary>Reads one account's explicit choices; null preserves the behavior of accounts connected before onboarding.</summary>
    Task<AccountSharingSettings?> GetAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces one account's sharing choices without writing provider data.</summary>
    Task SaveAsync(AccountSharingSettings settings, CancellationToken cancellationToken = default);
}
