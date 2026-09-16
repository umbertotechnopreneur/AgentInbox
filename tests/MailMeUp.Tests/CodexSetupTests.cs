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
    [InlineData("[{\"name\":\"MailMeUp\",\"enabled\":true}]")]
    [InlineData("[{\"name\":\"custom-mail\",\"enabled\":true,\"transport\":{\"command\":\"mailmeup.exe\"}}]")]
    [InlineData("[{\"name\":\"custom-mail\",\"enabled\":true,\"transport\":{\"command\":\"mailmeup\"}}]")]
    [InlineData("[{\"name\":\"custom-mail\",\"enabled\":true,\"transport\":{\"command\":\"dotnet\",\"args\":[\"mailmeup.dll\",\"--stdio\"]}}]")]
    public void DirectRegistrationCanBeRecognizedByNameExecutableOrAssembly(string json)
    {
        Assert.True(CodexSetupService.TryReadDirectRegistration(json, out var count, out var enabled));
        Assert.Equal(1, count);
        Assert.Equal(true, enabled);
        Assert.True(CodexSetupService.IsDirectConfigurationReady(count, enabled, true, false, false));
    }

    [Fact]
    public void MatchingNameAndExecutableCountAsOneRegistration()
    {
        const string json = "[{\"name\":\"mailmeup\",\"enabled\":true,\"transport\":{\"command\":\"mailmeup.exe\"}}]";
        Assert.True(CodexSetupService.TryReadDirectRegistration(json, out var count, out var enabled));
        Assert.Equal(1, count);
        Assert.Equal(true, enabled);
    }

    [Theory]
    [InlineData("[{\"name\":\"mailmeup\",\"enabled\":false}]", false)]
    [InlineData("[{\"name\":\"mailmeup\"}]", null)]
    [InlineData("[{\"name\":\"mailmeup\",\"enabled\":null}]", null)]
    [InlineData("[{\"name\":\"mailmeup\",\"enabled\":\"true\"}]", null)]
    [InlineData("[{\"name\":\"mailmeup\",\"enabled\":1}]", null)]
    public void DisabledOrUnknownDirectRegistrationCannotCompleteSetup(string json, bool? expectedEnabled)
    {
        Assert.True(CodexSetupService.TryReadDirectRegistration(json, out var count, out var enabled));
        Assert.Equal(1, count);
        Assert.Equal(expectedEnabled, enabled);
        Assert.False(CodexSetupService.IsDirectConfigurationReady(count, enabled, true, false, false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MultipleDirectRegistrationsRemainUnresolvedEvenIfOnlyOneIsEnabled(bool secondEnabled)
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new { name = "mailmeup", enabled = true, transport = new { command = "mailmeup.exe" } },
            new { name = "custom-mail", enabled = secondEnabled, transport = new { command = "mailmeup.exe" } }
        });
        Assert.True(CodexSetupService.TryReadDirectRegistration(json, out var count, out var enabled));
        Assert.Equal(2, count);
        Assert.Null(enabled);
        Assert.False(CodexSetupService.IsDirectConfigurationReady(count, enabled, true, false, false));
    }

    [Fact]
    public void UnrelatedDirectEntriesDoNotAffectMailMeUpState()
    {
        const string json = "[{\"name\":\"other-server\"},{\"name\":\"mailmeup\",\"enabled\":true}]";
        Assert.True(CodexSetupService.TryReadDirectRegistration(json, out var count, out var enabled));
        Assert.Equal(1, count);
        Assert.Equal(true, enabled);
        Assert.True(CodexSetupService.TryReadDirectRegistration("[]", out count, out enabled));
        Assert.Equal(0, count);
        Assert.Null(enabled);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{\"enabled\":true}]")]
    [InlineData("[42]")]
    public void UnsupportedDirectResponsesCannotBeTreatedAsNoRegistration(string json)
    {
        Assert.False(CodexSetupService.TryReadDirectRegistration(json, out _, out _));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void DirectCompletionRequiresKnownPluginsWithNoEnabledMailMeUpPlugin(bool pluginsKnown, bool localEnabled, bool otherEnabled)
    {
        Assert.False(CodexSetupService.IsDirectConfigurationReady(1, true, pluginsKnown, localEnabled, otherEnabled));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void DirectCompletionRequiresExactlyOneExplicitlyEnabledRegistration(int? count, bool? enabled)
    {
        Assert.False(CodexSetupService.IsDirectConfigurationReady(count, enabled, true, false, false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OtherSourcePluginEnabledStateControlsDirectCompletion(bool otherEnabled)
    {
        var json = JsonSerializer.Serialize(new
        {
            installed = new[]
            {
                new { pluginId = "mailmeup@mailmeup-local", name = "mailmeup", enabled = false },
                new { pluginId = "mailmeup@other-source", name = "mailmeup", enabled = otherEnabled }
            }
        });
        Assert.True(CodexSetupService.TryReadPluginState(json, out var installed, out var enabled, out var other, out var observedOtherEnabled));
        Assert.True(installed);
        Assert.False(enabled);
        Assert.True(other);
        Assert.Equal(otherEnabled, observedOtherEnabled);
        Assert.Equal(!otherEnabled, CodexSetupService.IsDirectConfigurationReady(1, true, true, enabled, observedOtherEnabled));
    }

    [Fact]
    public void UnknownOtherSourcePluginStateCannotCompleteDirectSetup()
    {
        const string json = "{\"installed\":[{\"pluginId\":\"mailmeup@other-source\",\"name\":\"mailmeup\"}]}";
        var known = CodexSetupService.TryReadPluginState(json, out _, out var enabled, out _, out var otherEnabled);
        Assert.False(known);
        Assert.False(CodexSetupService.IsDirectConfigurationReady(1, true, known, enabled, otherEnabled));
    }

    [Fact]
    public void DisabledDuplicatePluginEntriesCannotHideAnEnabledPlugin()
    {
        var json = JsonSerializer.Serialize(new
        {
            installed = new[]
            {
                new { pluginId = "mailmeup@mailmeup-local", name = "mailmeup", enabled = true },
                new { pluginId = "mailmeup@mailmeup-local", name = "mailmeup", enabled = false },
                new { pluginId = "mailmeup@other-source", name = "mailmeup", enabled = true },
                new { pluginId = "mailmeup@other-source", name = "mailmeup", enabled = false }
            }
        });
        Assert.True(CodexSetupService.TryReadPluginState(json, out var installed, out var enabled, out var other, out var otherEnabled));
        Assert.True(installed);
        Assert.True(enabled);
        Assert.True(other);
        Assert.True(otherEnabled);
        Assert.False(CodexSetupService.IsDirectConfigurationReady(1, true, true, enabled, otherEnabled));
    }

    [Theory]
    [InlineData(false, "Direct connection needs review")]
    [InlineData(true, "Two connection entries found")]
    public void DirectConnectionGetsActionableGuidance(bool pluginConfigured, string title)
    {
        var view = CodexSetupPresentation.FromStatus(new("DirectRegistrationExists", "Synthetic finding", false, pluginConfigured, true));
        Assert.Equal(title, view.Title);
        Assert.Equal("Review connections", view.HelpLabel);
        Assert.Contains(view.Steps, step => step.Contains("keep", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(view.Steps, step => step.Contains("does not remove", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("PluginConfigured", true, null, null, true)]
    [InlineData("PluginConfigured", false, null, null, false)]
    [InlineData("DirectConfigured", false, 1, true, true)]
    [InlineData("DirectConfigured", true, 1, true, false)]
    [InlineData("DirectConfigured", false, 1, false, false)]
    [InlineData("DirectConfigured", false, 1, null, false)]
    [InlineData("DirectConfigured", false, 2, true, false)]
    [InlineData("DirectConfigured", false, null, null, false)]
    [InlineData("DirectRegistrationExists", false, 1, true, false)]
    [InlineData("ReadyToInstall", false, null, null, false)]
    public void PresentationOnlyCompletesConfirmedLocalConfiguration(string code, bool pluginConfigured,
        int? directCount, bool? directEnabled, bool expectedReady)
    {
        var status = new CodexSetupStatus(code, "Synthetic finding", false, pluginConfigured, directCount.GetValueOrDefault() > 0)
        {
            DirectRegistrationCount = directCount,
            IsDirectRegistrationEnabled = directEnabled
        };
        Assert.Equal(expectedReady, CodexSetupPresentation.IsLocalSetupReady(status));
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
