using MailMeUp.Application;
using MailMeUp.Core;
using MailMeUp.Desktop.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailMeUp.Tests;

public sealed class ReadGuardrailApplicationTests
{
    [Fact]
    public async Task SettingsSavePreservesActiveLimitsAndReturnsSharedUsageWithoutReadingAccounts()
    {
        var management = new MemoryManagement();
        var application = CreateApplication(management);
        using var cancellation = new CancellationTokenSource();
        var original = Assert.IsType<ReadGuardrailStatus>(await application.GetReadGuardrailStatusAsync(cancellation.Token));
        var requested = original.SavedLimits with { DetailReadsPerWindow = 10 };

        var saved = await application.SaveReadGuardrailLimitsAsync(requested, original.SavedLimits, cancellation.Token);

        Assert.Equal(original.ActiveLimits, saved.ActiveLimits);
        Assert.Equal(requested, saved.SavedLimits);
        Assert.Equal(original.Usage, saved.Usage);
        Assert.True(saved.RequiresRestart);
        Assert.Equal(cancellation.Token, management.LastToken);
        Assert.Equal(1, management.SaveCalls);
        Assert.Equal(0, management.ContentAdmissions);
        Assert.Equal(0, management.OutputCharges);
    }

    [Fact]
    public async Task ManagementCanBeResolvedFromTheSameInjectedReadBudget()
    {
        var shared = new MemoryManagement();
        var application = new MailMeUpApplication(new UnusedAccountStore(), [], [], [], [], [], readBudget: shared);

        var status = Assert.IsType<ReadGuardrailStatus>(await application.GetReadGuardrailStatusAsync());
        await application.SaveReadGuardrailLimitsAsync(status.SavedLimits with { DetailReadsPerWindow = 8 }, status.SavedLimits);

        Assert.Equal(1, shared.SaveCalls);
        Assert.Equal(8, (await shared.GetStatusAsync()).SavedLimits.DetailReadsPerWindow);
        Assert.Equal(20, shared.Limits.DetailReadsPerWindow);
    }

    [Fact]
    public async Task UnsupportedManagementReportsUnavailableInsteadOfInventingZeroUsage()
    {
        var application = new MailMeUpApplication(new UnusedAccountStore(), []);

        Assert.Null(await application.GetReadGuardrailStatusAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => application.SaveReadGuardrailLimitsAsync(new(), new()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidDraftOrExpectedSettingsFailBeforeTheStorageWrite(bool invalidExpected)
    {
        var management = new MemoryManagement();
        var application = CreateApplication(management);
        var valid = new ReadGuardrailLimits();
        var invalid = valid with { DetailReadsPerWindow = valid.ContentReadsPerWindow + 1 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => application.SaveReadGuardrailLimitsAsync(
            invalidExpected ? valid : invalid, invalidExpected ? invalid : valid));

        Assert.Equal(0, management.SaveCalls);
        Assert.Equal(valid, (await management.GetStatusAsync()).SavedLimits);
    }

    [Fact]
    public async Task CancelledRequestsDoNotReachSettingsStorage()
    {
        var management = new MemoryManagement();
        var application = CreateApplication(management);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => application.GetReadGuardrailStatusAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => application.SaveReadGuardrailLimitsAsync(new(), new(), cancellation.Token));

        Assert.Equal(0, management.StatusCalls);
        Assert.Equal(0, management.SaveCalls);
    }

    [Fact]
    public async Task StaleEditorCannotOverwriteANewerSettingsSave()
    {
        var management = new MemoryManagement();
        var application = CreateApplication(management);
        var original = Assert.IsType<ReadGuardrailStatus>(await application.GetReadGuardrailStatusAsync());
        var firstDraft = original.SavedLimits with { DetailReadsPerWindow = 9 };
        var staleDraft = original.SavedLimits with { DetailReadsPerWindow = 7 };
        await application.SaveReadGuardrailLimitsAsync(firstDraft, original.SavedLimits);

        await Assert.ThrowsAsync<InvalidOperationException>(() => application.SaveReadGuardrailLimitsAsync(staleDraft, original.SavedLimits));

        Assert.Equal(firstDraft, (await application.GetReadGuardrailStatusAsync())!.SavedLimits);
        Assert.Equal(7, staleDraft.DetailReadsPerWindow);
    }

    [Fact]
    public async Task LoggingDecoratorDelegatesSettingsAndDoesNotLogTheirValues()
    {
        var management = new MemoryManagement();
        var logger = new CaptureLogger();
        IMailMeUpApplication application = new LoggingMailMeUpApplication(CreateApplication(management), logger);
        var original = Assert.IsType<ReadGuardrailStatus>(await application.GetReadGuardrailStatusAsync());
        var requested = original.SavedLimits with { OutputBytesPerWindow = 7654321 };

        var saved = await application.SaveReadGuardrailLimitsAsync(requested, original.SavedLimits);

        Assert.Equal(requested, saved.SavedLimits);
        Assert.Contains(logger.Messages, message => message.Contains("get_read_guardrail_status", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("save_read_guardrail_limits", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("7654321", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DemoSettingsAndIllustrativeUsageStayInsideOneSession()
    {
        var changed = new DemoMailMeUpApplication();
        var other = new DemoMailMeUpApplication();
        var original = Assert.IsType<ReadGuardrailStatus>(await changed.GetReadGuardrailStatusAsync());
        var requested = original.SavedLimits with { DetailReadsPerWindow = 8 };

        var saved = await changed.SaveReadGuardrailLimitsAsync(requested, original.SavedLimits);
        var untouched = Assert.IsType<ReadGuardrailStatus>(await other.GetReadGuardrailStatusAsync());
        var reopened = Assert.IsType<ReadGuardrailStatus>(await new DemoMailMeUpApplication().GetReadGuardrailStatusAsync());

        Assert.True(changed.IsDemo);
        Assert.True(saved.RequiresRestart);
        Assert.Equal(original.ActiveLimits, saved.ActiveLimits);
        Assert.Equal(requested, saved.SavedLimits);
        Assert.Equal(original.Usage, saved.Usage);
        Assert.Equal(original, untouched);
        Assert.Equal(original, reopened);
        Assert.Equal(saved, await changed.GetReadGuardrailStatusAsync());
    }

    [Fact]
    public async Task InvalidStaleAndCancelledDemoSavesPreserveTheSavedDraft()
    {
        var application = new DemoMailMeUpApplication();
        var original = Assert.IsType<ReadGuardrailStatus>(await application.GetReadGuardrailStatusAsync());
        var requested = original.SavedLimits with { DetailReadsPerWindow = 8 };
        await application.SaveReadGuardrailLimitsAsync(requested, original.SavedLimits);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => application.SaveReadGuardrailLimitsAsync(
            requested with { DetailReadsPerWindow = 0 }, requested));
        await Assert.ThrowsAsync<InvalidOperationException>(() => application.SaveReadGuardrailLimitsAsync(
            requested with { DetailReadsPerWindow = 6 }, original.SavedLimits));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => application.SaveReadGuardrailLimitsAsync(
            requested with { DetailReadsPerWindow = 6 }, requested, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => application.GetReadGuardrailStatusAsync(cancellation.Token));

        Assert.Equal(requested, (await application.GetReadGuardrailStatusAsync())!.SavedLimits);
    }

    private static MailMeUpApplication CreateApplication(IReadGuardrailManagement management) =>
        new(new UnusedAccountStore(), [], [], [], [], [], readGuardrailManagement: management);

    private sealed class UnusedAccountStore : IAccountStore
    {
        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Guardrail settings must not read account metadata.");
        public Task SaveAsync(Account account, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Guardrail settings must not save accounts.");
        public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Guardrail settings must not remove accounts.");
    }

    private sealed class MemoryManagement : IReadGuardrailManagement, IReadBudget
    {
        private ReadGuardrailLimits _savedLimits = new();
        private readonly ReadGuardrailUsage _usage = new(
            new DateTimeOffset(2026, 9, 13, 10, 0, 0, TimeSpan.Zero),
            17, 9, 3, 12345, 0, null, null, null, null, null);
        public ReadGuardrailLimits Limits { get; } = new();
        public int SaveCalls { get; private set; }
        public int StatusCalls { get; private set; }
        public int ContentAdmissions { get; private set; }
        public int OutputCharges { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<ReadGuardrailStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatusCalls++;
            LastToken = cancellationToken;
            return Task.FromResult(new ReadGuardrailStatus(Limits, _savedLimits, _usage));
        }

        public Task<ReadGuardrailStatus> SaveLimitsAsync(ReadGuardrailLimits limits, ReadGuardrailLimits expectedLimits,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            LastToken = cancellationToken;
            if (_savedLimits != expectedLimits)
                throw new InvalidOperationException("Settings changed while editing.");
            _savedLimits = limits;
            return Task.FromResult(new ReadGuardrailStatus(Limits, _savedLimits, _usage));
        }

        public Task AdmitReadAsync(bool detail, CancellationToken cancellationToken = default)
        {
            ContentAdmissions++;
            return Task.CompletedTask;
        }

        public Task ChargeOutputAsync(int bytes, CancellationToken cancellationToken = default)
        {
            OutputCharges++;
            return Task.CompletedTask;
        }
    }

    private sealed class CaptureLogger : ILogger<LoggingMailMeUpApplication>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
