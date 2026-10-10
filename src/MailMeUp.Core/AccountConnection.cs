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

/// <summary>Selects the read-only data categories requested during account consent.</summary>
public sealed record AccountConnectionOptions(bool IncludeMail = true, bool IncludeCalendar = true, bool ShareWithAssistant = true)
{
    /// <summary>Ensures that the sign-in requests at least one useful read capability.</summary>
    public void Validate()
    {
        if (!IncludeMail && !IncludeCalendar)
        {
            throw new ArgumentException("Select mail, calendars, or both.");
        }
    }
}

/// <summary>Result of a completed interactive account connection.</summary>
public sealed record AccountConnectionResult(Account Account);

/// <summary>Result of removing an account and its local credential cache.</summary>
public sealed record AccountRemovalResult(string AccountId, bool Removed);

/// <summary>Connects and disconnects accounts for one provider outside the MCP transport.</summary>
public interface IAccountConnector
{
    /// <summary>Gets the stable provider identifier.</summary>
    string ProviderId { get; }

    /// <summary>Runs interactive sign-in and persists only protected credential material.</summary>
    Task<AccountConnectionResult> ConnectAsync(AccountConnectionOptions options, CancellationToken cancellationToken = default);

    /// <summary>Removes the local credential cache for an account without changing provider data.</summary>
    Task DisconnectAsync(Account account, CancellationToken cancellationToken = default);
}

/// <summary>Indicates that a provider sign-in could not complete without exposing provider details.</summary>
public sealed class ProviderAuthenticationException : Exception
{
    /// <summary>Creates a sanitized provider authentication failure.</summary>
    public ProviderAuthenticationException(string message)
        : base(message)
    {
    }
}
