using MailMeUp.Core;
using MailMeUp.Storage;
using Xunit;

namespace MailMeUp.Tests;

public sealed class FileReadGuardrailsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MailMeUp-guardrail-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ConstructingDefaultControlsDoesNotCreateRuntimeDirectories()
    {
        var guardrails = new FileReadGuardrails(_directory);

        Assert.Equal(60, guardrails.Limits.ProviderAttemptsPerAccountServicePerMinute);
        Assert.Equal(64 * 1024, guardrails.Limits.ResponseBytes);
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{\"contentReadsPerWindow\":0}")]
    [InlineData("{\"unknownSetting\":1}")]
    [InlineData("{\"contentReadsPerWindow\":120,\"contentReadsPerWindow\":1000}")]
    public void InvalidSettingsFailClosedWithoutCreatingTheLedger(string settings)
    {
        WriteSettings(settings);

        var failure = Assert.Throws<ProviderReadException>(() => new FileReadGuardrails(_directory));

        Assert.Equal(ReadFailureKind.LocalConfiguration, failure.Kind);
        Assert.False(Directory.Exists(Path.Combine(_directory, "read-guardrails")));
    }

    [Fact]
    public async Task SeparateInstancesSharePersistedReadAndOutputBudgets()
    {
        WriteSettings("{\"contentReadsPerWindow\":1,\"detailReadsPerWindow\":1,\"responseBytes\":1024,\"outputBytesPerWindow\":1024}");
        var first = new FileReadGuardrails(_directory);
        var second = new FileReadGuardrails(_directory);
        await first.AdmitReadAsync(true);
        var readFailure = await Assert.ThrowsAsync<ProviderReadException>(() => second.AdmitReadAsync(false));
        Assert.Equal(ReadFailureKind.BudgetExceeded, readFailure.Kind);
        await first.ChargeOutputAsync(1024);
        var outputFailure = await Assert.ThrowsAsync<ProviderReadException>(() => second.ChargeOutputAsync(1));
        Assert.Equal(ReadFailureKind.BudgetExceeded, outputFailure.Kind);
    }

    [Fact]
    public async Task SeparateInstancesShareCooldownsWithoutPersistingAccountIdentity()
    {
        var first = new FileReadGuardrails(_directory);
        var second = new FileReadGuardrails(_directory);
        const string accountId = "private-account@example.test";
        await using (var lease = await first.AcquireAsync("gmail", accountId, false))
            await lease.SetCooldownAsync(TimeSpan.FromDays(2), ReadFailureKind.ProviderUnavailable);

        var failure = await Assert.ThrowsAsync<ProviderReadException>(() => second.AcquireAsync("gmail", accountId, false));
        Assert.Equal(ReadFailureKind.ProviderUnavailable, failure.Kind);
        var ledger = await File.ReadAllTextAsync(Path.Combine(_directory, "read-guardrails", "ledger.json"));
        Assert.DoesNotContain(accountId, ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("gmail", ledger, StringComparison.Ordinal);
        Assert.All(Directory.EnumerateFiles(Path.Combine(_directory, "read-guardrails", "scopes")), path =>
            Assert.Equal(64, Path.GetFileNameWithoutExtension(path).Length));
    }

    [Fact]
    public async Task ASecondInstanceCannotAcquireTheSameScopeBeforeTheHttpLeaseIsReleased()
    {
        var first = new FileReadGuardrails(_directory);
        var second = new FileReadGuardrails(_directory);
        await using var lease = await first.AcquireAsync("gmail", "account@example.test", false);
        using var cancellation = new CancellationTokenSource();
        var pending = second.AcquireAsync("gmail", "account@example.test", false, cancellation.Token);
        Assert.False(pending.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("{\"version\":1,\"providerAttempts\":[],\"contentReads\":[],\"detailReads\":[],\"output\":[],\"scopes\":null}")]
    public async Task CorruptExistingStateCannotResetUsage(string damagedState)
    {
        var guardrails = new FileReadGuardrails(_directory);
        await guardrails.AdmitReadAsync(false);
        await File.WriteAllTextAsync(Path.Combine(_directory, "read-guardrails", "ledger.json"), damagedState);

        var failure = await Assert.ThrowsAsync<ProviderReadException>(() => guardrails.AdmitReadAsync(false));

        Assert.Equal(ReadFailureKind.LocalConfiguration, failure.Kind);
    }

    private void WriteSettings(string settings)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "read-guardrails.json"), settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
