/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Shipped Microsoft public-client registration settings.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.Text.Json;

namespace MailMeUp.Desktop.Services;

/// <summary>Loads the public Microsoft application identity shipped with Windows setup.</summary>
/// <param name="MicrosoftClientId">Public-client ID shipped with the Windows app.</param>
internal sealed record DesktopSettings(string MicrosoftClientId)
{
    /// <summary>Rejects missing or invalid configuration before the account editor opens.</summary>
    internal static DesktopSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var clientIdText = document.RootElement.GetProperty("MicrosoftClientId").GetString();
        if (!Guid.TryParseExact(clientIdText, "D", out var clientId) || clientId == Guid.Empty)
            throw new InvalidOperationException("appsettings.json must contain a valid MicrosoftClientId.");

        return new DesktopSettings(clientId.ToString("D"));
    }
}
