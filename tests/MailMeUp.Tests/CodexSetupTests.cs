using System.Text.Json;
using MailMeUp.Desktop.Services;
using Xunit;

namespace MailMeUp.Tests;

public sealed class CodexSetupTests
{
    [Fact]
    public void PendingChecksDoNotClaimThatComponentsAreMissingOrReady()
    {
        var checks = CodexSetupCheck.Pending();
        Assert.Equal(5, checks.Count);
        Assert.All(checks, check => Assert.Equal("Not checked", check.Result));
    }

    [Fact]
    public void AddedMarketplaceDoesNotMeanPluginInstalled()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mailmeup-synthetic-marketplace"));
        var json = JsonSerializer.Serialize(new { marketplaces = new[] { new { name = "mailmeup-local", root } } });
        Assert.True(CodexSetupService.TryReadMarketplaceState(json, root, out var collision, out var registered));
        Assert.True(registered);
        Assert.False(collision);
        Assert.True(CodexSetupService.TryReadPluginState("{\"installed\":[]}", out var installed, out var enabled, out var other));
        Assert.False(installed);
        Assert.False(enabled);
        Assert.False(other);
    }

    [Fact]
    public void MissingMarketplaceIsDifferentFromUnsupportedResponse()
    {
        Assert.True(CodexSetupService.TryReadMarketplaceState("{\"marketplaces\":[]}", Path.GetTempPath(), out var collision, out var registered));
        Assert.False(collision);
        Assert.False(registered);
        Assert.False(CodexSetupService.TryReadMarketplaceState("{}", Path.GetTempPath(), out _, out _));
    }

    [Fact]
    public void ConflictingMarketplaceCannotBeOverriddenByAnotherEntry()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mailmeup-synthetic-marketplace"));
        var json = JsonSerializer.Serialize(new
        {
            marketplaces = new[]
            {
                new { name = "mailmeup-local", root = root + "-different" },
                new { name = "mailmeup-local", root }
            }
        });
        Assert.True(CodexSetupService.TryReadMarketplaceState(json, root, out var collision, out var registered));
        Assert.True(registered);
        Assert.True(collision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InstalledPluginPreservesEnabledState(bool expectedEnabled)
    {
        var json = JsonSerializer.Serialize(new
        {
            installed = new[] { new { pluginId = "mailmeup@mailmeup-local", name = "mailmeup", enabled = expectedEnabled } }
        });
        Assert.True(CodexSetupService.TryReadPluginState(json, out var installed, out var enabled, out var other));
        Assert.True(installed);
        Assert.Equal(expectedEnabled, enabled);
        Assert.False(other);
    }

    [Fact]
    public void MissingPluginEnabledFlagIsNotASuccess()
    {
        Assert.False(CodexSetupService.TryReadPluginState(
            "{\"installed\":[{\"pluginId\":\"mailmeup@mailmeup-local\",\"name\":\"mailmeup\"}]}", out _, out _, out _));
    }

    [Theory]
    [InlineData(false, "Direct MCP setup found")]
    [InlineData(true, "Two connection methods found")]
    public void DirectConnectionGetsActionableGuidance(bool pluginConfigured, string title)
    {
        var view = CodexSetupPresentation.FromStatus(new("DirectRegistrationExists", "Synthetic finding", false, pluginConfigured, true));
        Assert.Equal(title, view.Title);
        Assert.Equal("Review connections", view.HelpLabel);
        Assert.Contains(view.Steps, step => step.Contains("keep", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(view.Steps, step => step.Contains("does not remove", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ConfigurationUnknown")]
    [InlineData("PluginStatusUnknown")]
    [InlineData("MarketplaceStatusUnknown")]
    [InlineData("CheckInterrupted")]
    [InlineData("InstallationIncomplete")]
    [InlineData("FutureStatus")]
    public void UnknownAndFailedStatesPreserveReasonAndNeverClaimSuccess(string code)
    {
        var view = CodexSetupPresentation.FromStatus(new(code, "Synthetic failure reason", false, false, false));
        Assert.Equal("Setup check needs attention", view.Title);
        Assert.Contains("Synthetic failure reason", view.Steps);
        Assert.Contains(view.Steps, step => step.Contains("No successful connection is assumed", StringComparison.Ordinal));
    }
}
