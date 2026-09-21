using MailMeUp.Desktop.Services;
using Xunit;

namespace MailMeUp.Tests;

public sealed class SetupLaunchOptionsTests
{
    [Fact]
    public void DefaultLaunchDoesNotRequestNavigationOrDemoData()
    {
        Assert.True(SetupLaunchOptions.TryParse([], out var options));

        Assert.Null(options.Step);
        Assert.False(options.IsDemo);
    }

    [Theory]
    [InlineData("welcome", 0)]
    [InlineData("accounts", 1)]
    [InlineData("sharing", 2)]
    [InlineData("codex", 3)]
    [InlineData("SHARING", 2)]
    public void ExplicitStepIsPreservedAlongsideDemoMode(string step, int expected)
    {
        Assert.True(SetupLaunchOptions.TryParse(["--step", step, "--demo"], out var options));

        Assert.Equal((SetupStep)expected, options.Step);
        Assert.True(options.IsDemo);
    }

    [Fact]
    public void DemoModeDoesNotImplicitlyForceWelcomeOnAnExistingWindow()
    {
        Assert.True(SetupLaunchOptions.TryParse(["--demo"], out var options));

        Assert.Null(options.Step);
        Assert.True(options.IsDemo);
    }

    [Fact]
    public void DemoFlagCanPrecedeTheRequestedStep()
    {
        Assert.True(SetupLaunchOptions.TryParse(["--demo", "--step", "accounts"], out var options));

        Assert.Equal(SetupStep.Accounts, options.Step);
        Assert.True(options.IsDemo);
    }

    public static IEnumerable<object[]> InvalidArguments()
    {
        yield return [new[] { "--step" }];
        yield return [new[] { "--step", "" }];
        yield return [new[] { "--step", "4" }];
        yield return [new[] { "--step", "mailbox" }];
        yield return [new[] { "--demo", "--demo" }];
        yield return [new[] { "--step", "sharing", "--step", "welcome" }];
        yield return [new[] { "--step", "--demo" }];
        yield return [new[] { "--demo", "false" }];
        yield return [new[] { "--demo", "--unknown" }];
        yield return [new[] { "--desktop-path", "private-path" }];
        yield return [new[] { "accounts" }];
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void InvalidArgumentsFailWithoutRetainingPartialOptions(string[] arguments)
    {
        Assert.False(SetupLaunchOptions.TryParse(arguments, out var options));

        Assert.Null(options.Step);
        Assert.False(options.IsDemo);
    }

    [Theory]
    [InlineData("AgentInbox.Desktop.exe")]
    [InlineData("C:\\Program Files\\AgentInbox\\AgentInbox.Desktop.exe")]
    [InlineData("C:/Program Files/AgentInbox/AGENTINBOX.DESKTOP.EXE")]
    public void RedirectedFullCommandLineAcceptsTheDesktopExecutable(string executable)
    {
        Assert.True(SetupLaunchOptions.TryParseActivation([executable, "--step", "codex", "--demo"], out var options));

        Assert.Equal(SetupStep.Codex, options.Step);
        Assert.True(options.IsDemo);
    }

    [Fact]
    public void RedirectedArgumentsOnlyPreserveAnExplicitStep()
    {
        Assert.True(SetupLaunchOptions.TryParseActivation(["--step", "sharing"], out var options));

        Assert.Equal(SetupStep.Sharing, options.Step);
        Assert.False(options.IsDemo);
    }

    [Fact]
    public void RedirectedDefaultLaunchPreservesTheCurrentPage()
    {
        Assert.True(SetupLaunchOptions.TryParseActivation(["C:\\Program Files\\AgentInbox\\AgentInbox.Desktop.exe"], out var options));

        Assert.Null(options.Step);
    }

    [Fact]
    public void RedirectedLaunchDoesNotIgnoreUnrecognizedPositionalArguments()
    {
        Assert.False(SetupLaunchOptions.TryParseActivation(["unexpected.exe", "--step", "accounts"], out _));
    }
}
