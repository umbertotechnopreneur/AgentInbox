using MailMeUp.Cli;
using Xunit;

namespace MailMeUp.Tests;

public sealed class UiCliTests
{
    [Fact]
    public void UiWithoutAnExplicitStepPreservesTheCurrentPage()
    {
        var options = Parse("ui");

        Assert.Equal(CliCommand.Ui, options.Command);
        Assert.Null(options.UiStep);
        Assert.False(options.UiDemo);
        Assert.False(options.UiListSteps);
        Assert.Null(options.DesktopPath);
    }

    [Fact]
    public void StepListingSupportsJsonWithoutLaunchOptions()
    {
        var options = Parse("ui", "--list-steps", "--json");

        Assert.Equal(CliCommand.Ui, options.Command);
        Assert.True(options.UiListSteps);
        Assert.True(options.Json);
        Assert.False(options.UiDemo);
        Assert.Null(options.DesktopPath);
        Assert.Equal(new[] { "welcome", "accounts", "sharing", "codex" }, UiLauncher.Steps.Select(step => step.Id));
        Assert.All(UiLauncher.Steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Description));
            Assert.Equal($"agentinbox ui --step {step.Id}", step.Command);
        });
    }

    [Theory]
    [InlineData("welcome")]
    [InlineData("accounts")]
    [InlineData("sharing")]
    [InlineData("codex")]
    public void EveryKnownStepCanBeOpenedWithDemoAndExplicitDesktop(string step)
    {
        var executable = SyntheticPath("source preview", "AgentInbox.Desktop.exe");
        var options = Parse("ui", "--step", step, "--demo", "--desktop-path", executable);

        Assert.Equal(CliCommand.Ui, options.Command);
        Assert.Equal(step, options.UiStep);
        Assert.True(options.UiDemo);
        Assert.Equal(executable, options.DesktopPath);
    }

    [Theory]
    [InlineData("status", "--demo")]
    [InlineData("accounts", "list", "--step", "accounts")]
    [InlineData("setup", "status", "--list-steps")]
    [InlineData("--stdio", "--desktop-path", "AgentInbox.Desktop.exe")]
    [InlineData("ui", "--mail-only")]
    [InlineData("ui", "--calendar-only")]
    [InlineData("ui", "--list-steps", "--step", "welcome")]
    [InlineData("ui", "--list-steps", "--demo")]
    [InlineData("ui", "--list-steps", "--desktop-path", "AgentInbox.Desktop.exe")]
    [InlineData("ui", "--step", "unknown")]
    [InlineData("ui", "--step", "--demo")]
    [InlineData("ui", "--step")]
    [InlineData("ui", "--desktop-path")]
    [InlineData("ui", "--desktop-path", "--demo")]
    [InlineData("ui", "--demo", "--demo")]
    [InlineData("ui", "--step", "welcome", "--step", "accounts")]
    public void InvalidOrIncompatibleUiOptionsAreRejected(params string[] args)
    {
        Assert.Throws<CliUsageException>(() => Parse(args));
    }

    [Fact]
    public void PackagedDesktopIsPreferredOverAdjacentExecutable()
    {
        var cliDirectory = SyntheticPath("package", "mcp");
        var packaged = SyntheticPath("package", "AgentInbox.Desktop.exe");
        var adjacent = SyntheticPath("package", "mcp", "AgentInbox.Desktop.exe");

        var startInfo = UiLauncher.CreateStartInfo(cliDirectory, null, "accounts", false,
            path => path == packaged || path == adjacent);

        Assert.Equal(packaged, startInfo.FileName);
        Assert.Equal(Path.GetDirectoryName(packaged), startInfo.WorkingDirectory);
        Assert.Equal(new[] { "--step", "accounts" }, startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
        Assert.False(startInfo.CreateNoWindow);
    }

    [Fact]
    public void AdjacentExecutableIsUsedWhenPackagedDesktopIsAbsent()
    {
        var cliDirectory = SyntheticPath("adjacent");
        var executable = Path.Combine(cliDirectory, "AgentInbox.Desktop.exe");

        var startInfo = UiLauncher.CreateStartInfo(cliDirectory, null, "codex", false, path => path == executable);

        Assert.Equal(executable, startInfo.FileName);
    }

    [Fact]
    public void ExplicitExecutableWithSpacesUsesStructuredArguments()
    {
        var executable = SyntheticPath("preview with spaces;literal", "AgentInbox.Desktop.exe");
        var startInfo = UiLauncher.CreateStartInfo(SyntheticPath("cli"), executable, "sharing", true, path => path == executable);

        Assert.Equal(executable, startInfo.FileName);
        Assert.Equal(new[] { "--step", "sharing", "--demo" }, startInfo.ArgumentList);
        Assert.Empty(startInfo.Arguments);
        Assert.False(startInfo.UseShellExecute);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaunchWithoutAnExplicitStepDoesNotSendNavigationArguments(bool demo)
    {
        var executable = SyntheticPath("preview", "AgentInbox.Desktop.exe");
        var startInfo = UiLauncher.CreateStartInfo(SyntheticPath("cli"), executable, null, demo, path => path == executable);

        Assert.Equal(demo ? new[] { "--demo" } : [], startInfo.ArgumentList);
    }

    [Fact]
    public void MissingExplicitExecutableNeverFallsBackToInstalledApp()
    {
        var absent = SyntheticPath("missing", "AgentInbox.Desktop.exe");
        var requestedPaths = new List<string>();

        Assert.Throws<UiLaunchException>(() => UiLauncher.CreateStartInfo(SyntheticPath("cli"), absent, "welcome", true, path =>
        {
            requestedPaths.Add(path);
            return false;
        }));

        Assert.Equal(new[] { absent }, requestedPaths);
    }

    [Fact]
    public void MissingDesktopAndNonExecutablePathsFailWithoutLaunching()
    {
        Assert.Throws<UiLaunchException>(() => UiLauncher.CreateStartInfo(SyntheticPath("cli"), null, "welcome", false, _ => false));
        Assert.Throws<UiLaunchException>(() => UiLauncher.CreateStartInfo(SyntheticPath("cli"), SyntheticPath("preview.cmd"), "welcome", false, _ => true));
    }

    private static CliOptions Parse(params string[] args) => CliOptions.Parse([.. args, "--log-level", "warning"]);

    private static string SyntheticPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine([Path.GetTempPath(), "agentinbox-ui-synthetic", .. parts]));
}
