// SPDX-License-Identifier: MIT
/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 * Responsibility: Supply the running build's version to the About view.
 * AI contribution: OpenAI Codex implemented the owner-approved About layout.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 *
 * VBWR E */

using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace MailMeUp.Desktop.Views;

/// <summary>Shows read-only product information without changing the configuration.</summary>
public sealed partial class AboutDialog : ContentDialog
{
    /// <summary>Initializes the About dialog with its version already resolved.</summary>
    public AboutDialog() => InitializeComponent();

    /// <summary>Displays the version of the running desktop build.</summary>
    public string VersionLabel { get; } = ReadVersionLabel();

    /// <summary>Reads the built version without exposing the optional source revision.</summary>
    private static string ReadVersionLabel()
    {
        var assembly = typeof(AboutDialog).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(version)) return $"Version {version.Split('+', 2)[0]}";

        return assembly.GetName().Version is { } fallback ? $"Version {fallback}" : "Version unavailable";
    }
}
