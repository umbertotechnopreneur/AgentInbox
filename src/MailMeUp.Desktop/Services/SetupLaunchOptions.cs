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

namespace MailMeUp.Desktop.Services;

/// <summary>Identifies a setup page that can be opened explicitly from the command line.</summary>
internal enum SetupStep
{
    Welcome,
    Accounts,
    Sharing,
    Codex
}

/// <summary>Contains validated navigation options without starting any setup operation.</summary>
internal sealed record SetupLaunchOptions(SetupStep? Step = null, bool IsDemo = false)
{
    internal const string UsageError = "Invalid setup arguments. Use --step welcome|accounts|sharing|codex and optional --demo.";

    internal static bool TryParse(IReadOnlyList<string> arguments, out SetupLaunchOptions options)
    {
        SetupStep? step = null;
        var demo = false;
        options = new();
        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--step" when step is null && index + 1 < arguments.Count:
                    step = arguments[++index].ToLowerInvariant() switch
                    {
                        "welcome" => SetupStep.Welcome,
                        "accounts" => SetupStep.Accounts,
                        "sharing" => SetupStep.Sharing,
                        "codex" => SetupStep.Codex,
                        _ => null
                    };
                    if (step is null) return false;
                    break;
                case "--demo" when !demo:
                    demo = true;
                    break;
                default:
                    return false;
            }
        }

        options = new(step, demo);
        return true;
    }

    internal static bool TryParseActivation(IReadOnlyList<string> arguments, out SetupLaunchOptions options)
    {
        // AppLifecycle launch data can contain either a full Windows command line or arguments only.
        // Remove only the known executable name; never treat arbitrary positional data as an option.
        if (arguments.Count > 0)
        {
            var first = arguments[0];
            var fileName = first[(first.LastIndexOfAny(['\\', '/']) + 1)..];
            if (string.Equals(fileName, "AgentInbox.Desktop.exe", StringComparison.OrdinalIgnoreCase))
                return TryParse(arguments.Skip(1).ToArray(), out options);
        }

        return TryParse(arguments, out options);
    }
}
