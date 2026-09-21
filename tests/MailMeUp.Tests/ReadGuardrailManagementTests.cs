using System.Text.Json;
using MailMeUp.Core;
using MailMeUp.Storage;
using Xunit;

namespace MailMeUp.Tests;

public sealed class ReadGuardrailManagementTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AgentInbox-guardrail-management-tests", Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
    private string SettingsPath => Path.Combine(_directory, "read-guardrails.json");
    private string LedgerPath => Path.Combine(_directory, "read-guardrails", "ledger.json");

    [Fact]
    public async Task FirstRunStatusDoesNotCreateAnyProfileFilesOrDirectories()
    {
        IReadGuardrailManagement management = Create();

        var status = await management.GetStatusAsync();

        Assert.Equal(new ReadGuardrailLimits(), status.ActiveLimits);
        Assert.Equal(status.ActiveLimits, status.SavedLimits);
        Assert.False(status.RequiresRestart);
        Assert.Equal(_now, status.Usage.CapturedAt);
        Assert.Equal(0, status.Usage.ProviderAttemptsInMinute);
        Assert.Equal(0, status.Usage.ContentReadsInWindow);
        Assert.Equal(0, status.Usage.DetailReadsInWindow);
        Assert.Equal(0L, status.Usage.OutputBytesInWindow);
        Assert.Equal(0, status.Usage.ActiveCooldowns);
        Assert.Null(status.Usage.NextProviderCapacityAt);
        Assert.Null(status.Usage.NextContentCapacityAt);
        Assert.Null(status.Usage.NextDetailCapacityAt);
        Assert.Null(status.Usage.NextOutputCapacityAt);
        Assert.Null(status.Usage.LatestCooldownEndsAt);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task StatusAggregatesRollingChargesWithoutPersistingPruningOrExposingScopes()
    {
        var guardrails = Create();
        var startedAt = _now;
        const string account = "private-account@example.test";
        await using (var lease = await guardrails.AcquireAsync("gmail", account, false))
            await lease.SetCooldownAsync(TimeSpan.FromSeconds(30), ReadFailureKind.RateLimited);
        await using (var lease = await guardrails.AcquireAsync("microsoft-outlook", account, false))
            await lease.SetCooldownAsync(TimeSpan.FromSeconds(90), ReadFailureKind.ProviderUnavailable);
        await guardrails.AdmitReadAsync(true);
        await guardrails.ChargeOutputAsync(1024);
        _now += TimeSpan.FromSeconds(10);
        await using (await guardrails.AcquireAsync("google-calendar", account, false)) { }
        await guardrails.AdmitReadAsync(false);
        await guardrails.ChargeOutputAsync(2048);
        var ledgerBefore = await File.ReadAllTextAsync(LedgerPath);
        var filesBefore = Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).Order().ToArray();

        var status = await guardrails.GetStatusAsync();

        Assert.Equal(_now, status.Usage.CapturedAt);
        Assert.Equal(3, status.Usage.ProviderAttemptsInMinute);
        Assert.Equal(2, status.Usage.ContentReadsInWindow);
        Assert.Equal(1, status.Usage.DetailReadsInWindow);
        Assert.Equal(3072L, status.Usage.OutputBytesInWindow);
        Assert.Equal(2, status.Usage.ActiveCooldowns);
        Assert.Equal(startedAt.AddSeconds(60), status.Usage.NextProviderCapacityAt);
        Assert.Equal(startedAt.AddSeconds(900), status.Usage.NextContentCapacityAt);
        Assert.Equal(startedAt.AddSeconds(900), status.Usage.NextDetailCapacityAt);
        Assert.Equal(startedAt.AddSeconds(900), status.Usage.NextOutputCapacityAt);
        Assert.Equal(startedAt.AddSeconds(90), status.Usage.LatestCooldownEndsAt);
        var serialized = JsonSerializer.Serialize(status);
        Assert.DoesNotContain(account, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("gmail", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(_directory, serialized, StringComparison.Ordinal);
        using (var ledger = JsonDocument.Parse(ledgerBefore))
            foreach (var scope in ledger.RootElement.GetProperty("scopes").EnumerateObject())
                Assert.DoesNotContain(scope.Name, serialized, StringComparison.Ordinal);

        _now = startedAt.AddSeconds(60);
        var minute = await guardrails.GetStatusAsync();
        Assert.Equal(1, minute.Usage.ProviderAttemptsInMinute);
        Assert.Equal(startedAt.AddSeconds(70), minute.Usage.NextProviderCapacityAt);
        Assert.Equal(1, minute.Usage.ActiveCooldowns);

        _now = startedAt.AddSeconds(900);
        var window = await guardrails.GetStatusAsync();
        Assert.Equal(0, window.Usage.ProviderAttemptsInMinute);
        Assert.Null(window.Usage.NextProviderCapacityAt);
        Assert.Equal(1, window.Usage.ContentReadsInWindow);
        Assert.Equal(0, window.Usage.DetailReadsInWindow);
        Assert.Equal(2048L, window.Usage.OutputBytesInWindow);
        Assert.Equal(0, window.Usage.ActiveCooldowns);
        Assert.Equal(startedAt.AddSeconds(910), window.Usage.NextContentCapacityAt);
        Assert.Null(window.Usage.NextDetailCapacityAt);
        Assert.Equal(startedAt.AddSeconds(910), window.Usage.NextOutputCapacityAt);
        Assert.Null(window.Usage.LatestCooldownEndsAt);

        _now = startedAt.AddSeconds(910);
        var expired = await guardrails.GetStatusAsync();
        Assert.Equal(0, expired.Usage.ContentReadsInWindow);
        Assert.Equal(0L, expired.Usage.OutputBytesInWindow);
        Assert.Null(expired.Usage.NextContentCapacityAt);
        Assert.Null(expired.Usage.NextOutputCapacityAt);
        Assert.Equal(ledgerBefore, await File.ReadAllTextAsync(LedgerPath));
        Assert.Equal(filesBefore, Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [Fact]
    public async Task SavingLimitsKeepsActiveLimitsAndUsageUntilEveryProcessRestarts()
    {
        var first = Create();
        var second = Create();
        await first.AdmitReadAsync(true);
        await first.ChargeOutputAsync(1024);
        var ledgerBefore = await File.ReadAllTextAsync(LedgerPath);
        var changed = first.Limits with { DetailReadsPerWindow = 1, ReadWindowSeconds = 60 };

        var saved = await first.SaveLimitsAsync(changed, first.Limits);
        var otherProcess = await second.GetStatusAsync();
        var restarted = await Create().GetStatusAsync();

        Assert.True(saved.RequiresRestart);
        Assert.Equal(first.Limits, saved.ActiveLimits);
        Assert.Equal(changed, saved.SavedLimits);
        Assert.True(otherProcess.RequiresRestart);
        Assert.Equal(changed, otherProcess.SavedLimits);
        Assert.False(restarted.RequiresRestart);
        Assert.Equal(changed, restarted.ActiveLimits);
        Assert.Equal(1, restarted.Usage.DetailReadsInWindow);
        Assert.Equal(1024L, restarted.Usage.OutputBytesInWindow);
        Assert.Equal(ledgerBefore, await File.ReadAllTextAsync(LedgerPath));
        var failure = await Assert.ThrowsAsync<ProviderReadException>(() => Create().AdmitReadAsync(true));
        Assert.Equal(ReadFailureKind.BudgetExceeded, failure.Kind);

        _now += TimeSpan.FromSeconds(61);
        Assert.Equal(1, (await first.GetStatusAsync()).Usage.DetailReadsInWindow);
        Assert.Equal(0, (await Create().GetStatusAsync()).Usage.DetailReadsInWindow);
        Assert.Equal(ledgerBefore, await File.ReadAllTextAsync(LedgerPath));
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal(1, settings.RootElement.GetProperty("detailReadsPerWindow").GetInt32());
        Assert.False(settings.RootElement.TryGetProperty("DetailReadsPerWindow", out _));
    }

    [Fact]
    public async Task MovingTheClockBackDoesNotHideChargesThatStillBlockAdmission()
    {
        var initial = Create();
        await initial.SaveLimitsAsync(initial.Limits with { DetailReadsPerWindow = 1 }, initial.Limits);
        var guardrails = Create();
        var chargedAt = _now;
        await guardrails.AdmitReadAsync(true);
        await guardrails.ChargeOutputAsync(1024);
        _now -= TimeSpan.FromMinutes(1);

        var status = await guardrails.GetStatusAsync();
        var failure = await Assert.ThrowsAsync<ProviderReadException>(() => guardrails.AdmitReadAsync(true));

        Assert.Equal(1, status.Usage.ContentReadsInWindow);
        Assert.Equal(1, status.Usage.DetailReadsInWindow);
        Assert.Equal(1024L, status.Usage.OutputBytesInWindow);
        Assert.Equal(chargedAt.AddSeconds(900), status.Usage.NextDetailCapacityAt);
        Assert.Equal(ReadFailureKind.BudgetExceeded, failure.Kind);
    }

    [Fact]
    public async Task StatusCanReadAnAtomicLedgerWhileAnotherProcessHoldsItsWriteLease()
    {
        var guardrails = Create();
        await guardrails.AdmitReadAsync(true);
        var ledgerBefore = await File.ReadAllTextAsync(LedgerPath);
        using var heldLease = new FileStream(Path.Combine(_directory, "read-guardrails", "ledger.lock"), FileMode.Open,
            FileAccess.ReadWrite, FileShare.None);

        var status = await guardrails.GetStatusAsync();

        Assert.Equal(1, status.Usage.DetailReadsInWindow);
        Assert.Equal(ledgerBefore, await File.ReadAllTextAsync(LedgerPath));
    }

    [Fact]
    public async Task AStaleSettingsPageCannotOverwriteChangesSavedByAnotherProcess()
    {
        var first = Create();
        var second = Create();
        var original = (await first.GetStatusAsync()).SavedLimits;
        var changed = original with { DetailReadsPerWindow = 10 };
        await first.SaveLimitsAsync(changed, original);
        var settingsBefore = await File.ReadAllTextAsync(SettingsPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            second.SaveLimitsAsync(original with { DetailReadsPerWindow = 5 }, original));

        Assert.Equal(settingsBefore, await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal(changed, (await second.GetStatusAsync()).SavedLimits);
        Assert.False(File.Exists(LedgerPath));
    }

    [Fact]
    public async Task ConcurrentSettingsSavesAdmitOnlyOneWriterForTheDisplayedVersion()
    {
        var first = Create();
        var second = Create();
        var original = first.Limits;

        var saved = await Task.WhenAll(new[] { first, second }.Select(async (guardrails, index) =>
        {
            try
            {
                await guardrails.SaveLimitsAsync(original with { DetailReadsPerWindow = index + 1 }, original);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }));

        Assert.Equal(1, saved.Count(value => value));
        Assert.Contains((await first.GetStatusAsync()).SavedLimits.DetailReadsPerWindow, new[] { 1, 2 });
        Assert.False(File.Exists(LedgerPath));
    }

    [Fact]
    public async Task PreCancelledManagementCallsCreateNothing()
    {
        var guardrails = Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guardrails.GetStatusAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            guardrails.SaveLimitsAsync(guardrails.Limits, guardrails.Limits, cancellation.Token));

        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task WaitingForAnotherSettingsWriterCanBeCancelledWithoutChangingSettings()
    {
        var guardrails = Create();
        var ledgerDirectory = Path.Combine(_directory, "read-guardrails");
        Directory.CreateDirectory(ledgerDirectory);
        using var heldLease = new FileStream(Path.Combine(ledgerDirectory, "settings.lock"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None);
        using var cancellation = new CancellationTokenSource();
        var pending = guardrails.SaveLimitsAsync(guardrails.Limits with { DetailReadsPerWindow = 1 }, guardrails.Limits,
            cancellation.Token);
        Assert.False(pending.IsCompleted);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(File.Exists(SettingsPath));
        Assert.False(File.Exists(LedgerPath));
    }

    [Fact]
    public async Task SettingsLeaseWaitEndsAtTheActiveTimeoutWithoutChangingSettings()
    {
        var initial = Create();
        await initial.SaveLimitsAsync(initial.Limits with { LeaseWaitSeconds = 1 }, initial.Limits);
        var guardrails = Create();
        var settingsBefore = await File.ReadAllTextAsync(SettingsPath);
        using var heldLease = new FileStream(Path.Combine(_directory, "read-guardrails", "settings.lock"), FileMode.Open,
            FileAccess.ReadWrite, FileShare.None);

        var failure = await Assert.ThrowsAsync<ProviderReadException>(() =>
            guardrails.SaveLimitsAsync(guardrails.Limits with { DetailReadsPerWindow = 1 }, guardrails.Limits));

        Assert.Equal(ReadFailureKind.BudgetExceeded, failure.Kind);
        Assert.Equal(settingsBefore, await File.ReadAllTextAsync(SettingsPath));
        Assert.False(File.Exists(LedgerPath));
    }

    [Fact]
    public async Task InvalidNewOrExpectedLimitsCannotWriteSettings()
    {
        var guardrails = Create();
        var invalid = guardrails.Limits with { ContentReadsPerWindow = 0 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => guardrails.SaveLimitsAsync(invalid, guardrails.Limits));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => guardrails.SaveLimitsAsync(guardrails.Limits, invalid));

        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{\"unknownSetting\":1}")]
    [InlineData("{\"contentReadsPerWindow\":120,\"contentReadsPerWindow\":1000}")]
    [InlineData("{\"detailReadsPerWindow\":0}")]
    public async Task InvalidSettingsDetectedAfterStartupCannotBeOverwritten(string damagedSettings)
    {
        var guardrails = Create();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, damagedSettings);

        var readFailure = await Assert.ThrowsAsync<ProviderReadException>(() => guardrails.GetStatusAsync());
        var saveFailure = await Assert.ThrowsAsync<ProviderReadException>(() =>
            guardrails.SaveLimitsAsync(guardrails.Limits, guardrails.Limits));

        Assert.Equal(ReadFailureKind.LocalConfiguration, readFailure.Kind);
        Assert.Equal(ReadFailureKind.LocalConfiguration, saveFailure.Kind);
        Assert.Equal(damagedSettings, await File.ReadAllTextAsync(SettingsPath));
        Assert.False(File.Exists(LedgerPath));
    }

    [Fact]
    public async Task OversizedSettingsFailClosedWithoutCreatingUsageState()
    {
        var guardrails = Create();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, "{" + new string(' ', 16 * 1024) + "}");

        var failure = await Assert.ThrowsAsync<ProviderReadException>(() => guardrails.GetStatusAsync());

        Assert.Equal(ReadFailureKind.LocalConfiguration, failure.Kind);
        Assert.False(Directory.Exists(Path.Combine(_directory, "read-guardrails")));
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("{\"version\":1,\"providerAttempts\":[],\"contentReads\":[],\"detailReads\":[],\"output\":[],\"scopes\":null}")]
    [InlineData("{\"version\":1,\"providerAttempts\":[],\"contentReads\":[],\"detailReads\":[],\"output\":[],\"output\":[],\"scopes\":{}}")]
    public async Task CorruptLedgerBlocksStatusAndSavingWithoutResettingTheLedger(string damagedLedger)
    {
        var guardrails = Create();
        await guardrails.AdmitReadAsync(false);
        await File.WriteAllTextAsync(LedgerPath, damagedLedger);

        var readFailure = await Assert.ThrowsAsync<ProviderReadException>(() => guardrails.GetStatusAsync());
        var saveFailure = await Assert.ThrowsAsync<ProviderReadException>(() =>
            guardrails.SaveLimitsAsync(guardrails.Limits with { DetailReadsPerWindow = 1 }, guardrails.Limits));

        Assert.Equal(ReadFailureKind.LocalConfiguration, readFailure.Kind);
        Assert.Equal(ReadFailureKind.LocalConfiguration, saveFailure.Kind);
        Assert.Equal(damagedLedger, await File.ReadAllTextAsync(LedgerPath));
        Assert.False(File.Exists(SettingsPath));
    }

    private FileReadGuardrails Create() => new(_directory, () => _now, (delay, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        _now += delay;
        return Task.CompletedTask;
    });

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
