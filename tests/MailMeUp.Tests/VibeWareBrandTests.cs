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

using System.Text.RegularExpressions;
using MailMeUp.Cli;
using Spectre.Console;
using Xunit;

namespace MailMeUp.Tests;

public sealed class VibeWareBrandTests
{
    [Theory]
    [InlineData(60, false)]
    [InlineData(120, true)]
    public void Artwork_UsesRightmostColumnsOnlyWhenItFits(int width, bool showArtwork)
    {
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(output)
        });
        console.Profile.Width = width;
        console.Profile.Capabilities.Unicode = true;
        console.Profile.Capabilities.Links = false;
        console.Write(new VibeWareBrand(console, new Text("AgentInbox details")));

        var rendered = Regex.Replace(output.ToString(), "\u001b\\[[0-9;]*m", "");
        Assert.Contains("AgentInbox details", rendered);
        Assert.Contains(VibeWareBrand.ManifestoUrl, Regex.Replace(rendered, @"\s+", ""));
        var artworkLines = rendered.Split('\n').Where(line => line.Contains('▀')).ToArray();
        Assert.Equal(showArtwork, artworkLines.Length > 0);
        Assert.All(artworkLines, line => Assert.True(line.IndexOf('▀') >= width - 20));
    }
}
