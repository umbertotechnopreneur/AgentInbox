using System.Text.RegularExpressions;
using MailMeUp.Cli;
using Spectre.Console;

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
