using System.Text;
using System.Text.Json;
using MailMeUp.Application;
using MailMeUp.Core;
using MailMeUp.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailMeUp.Tests;

public sealed class ReadOutputBudgetTests
{
    private const string ContentMarker = "SYNTHETIC_MAIL_CONTENT";
    private const string PrivateDiagnostic = "private-response-body secret-token-example";
    private const string Start = "2026-09-12T00:00:00Z";
    private const string End = "2026-09-13T00:00:00Z";

    [Fact]
    public async Task ChargeCoversStructuredAndTextRepresentationsWithUnicodeAndEscaping()
    {
        var application = new SampleApplication { Body = string.Concat(Enumerable.Repeat("Résumé 界 📬 \"quoted\"\n", 40)) };
        var budget = new RecordingBudget(new());

        var result = await new MailTools(application, budget).ReadMailAsync("mail-ref");

        Assert.False(result.IsError);
        var structured = Payload(result);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal(application.Body, structured.GetProperty("text").GetString());
        Assert.Equal(structured.GetRawText(), text.Text);

        var structuredBytes = Encoding.UTF8.GetByteCount(structured.GetRawText());
        var serializedTextBytes = JsonSerializer.SerializeToUtf8Bytes(text.Text).Length;
        var charged = Assert.Single(budget.Charges);
        Assert.True(charged > structuredBytes + serializedTextBytes,
            "The charge must include both representations and the MCP envelope, including escaped text.");
        Assert.True(charged >= JsonSerializer.SerializeToUtf8Bytes(result, McpJsonUtilities.DefaultOptions).Length,
            "The complete public MCP result must not be larger than the charged byte count.");
    }

    [Fact]
    public async Task ResponseIsRejectedWhenSinglePayloadFitsButBothRepresentationsDoNot()
    {
        var application = new SampleApplication { Body = ContentMarker + new string('a', 900) };
        var probeBudget = new RecordingBudget(new());
        var probe = await new MailTools(application, probeBudget).ReadMailAsync("mail-ref");
        var singlePayloadBytes = Encoding.UTF8.GetByteCount(Payload(probe).GetRawText());
        var responseLimit = Math.Max(1024, singlePayloadBytes + 64);
        Assert.True(Assert.Single(probeBudget.Charges) > responseLimit);
        var limitedBudget = new RecordingBudget(new() { ResponseBytes = responseLimit });

        var result = await new MailTools(application, limitedBudget).ReadMailAsync("mail-ref");

        AssertBudgetStop(result);
        Assert.Empty(limitedBudget.AttemptedCharges);
        Assert.DoesNotContain(ContentMarker, Payload(result).GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameCharacterCountCanPassForAsciiAndExceedLimitForUnicode()
    {
        var ascii = new SampleApplication { Body = new string('a', 400) };
        var unicode = new SampleApplication { Body = new string('界', 400) };
        Assert.Equal(ascii.Body.Length, unicode.Body.Length);
        var probeBudget = new RecordingBudget(new());
        Assert.False((await new MailTools(ascii, probeBudget).ReadMailAsync("mail-ref")).IsError);
        var limit = Assert.Single(probeBudget.Charges);
        var asciiBudget = new RecordingBudget(new() { ResponseBytes = limit });
        var unicodeBudget = new RecordingBudget(new() { ResponseBytes = limit });

        var asciiResult = await new MailTools(ascii, asciiBudget).ReadMailAsync("mail-ref");
        var unicodeResult = await new MailTools(unicode, unicodeBudget).ReadMailAsync("mail-ref");

        Assert.False(asciiResult.IsError);
        AssertBudgetStop(unicodeResult);
        Assert.Equal(limit, Assert.Single(asciiBudget.Charges));
        Assert.Empty(unicodeBudget.Charges);
    }

    [Fact]
    public async Task ResponseAtExactByteBoundarySucceedsAndOneByteLessIsRejected()
    {
        var application = new SampleApplication { Body = new string('x', 600) };
        var probeBudget = new RecordingBudget(new());
        await new MailTools(application, probeBudget).ReadMailAsync("mail-ref");
        var bytes = Assert.Single(probeBudget.Charges);

        var exact = await new MailTools(application, new RecordingBudget(new() { ResponseBytes = bytes })).ReadMailAsync("mail-ref");
        var tooSmall = await new MailTools(application, new RecordingBudget(new() { ResponseBytes = bytes - 1 })).ReadMailAsync("mail-ref");

        Assert.False(exact.IsError);
        AssertBudgetStop(tooSmall);
    }

    [Fact]
    public async Task CumulativeBudgetIsSharedAcrossToolInstancesAndResetsAfterItsWindow()
    {
        var application = new SampleApplication { Body = ContentMarker + new string('x', 600) };
        var probeBudget = new RecordingBudget(new());
        await new MailTools(application, probeBudget).ReadMailAsync("mail-ref");
        var bytes = Assert.Single(probeBudget.Charges);
        var now = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        var guardrails = new InMemoryReadGuardrails(new()
        {
            ResponseBytes = bytes,
            OutputBytesPerWindow = 2 * bytes,
            ReadWindowSeconds = 60
        }, utcNow: () => now);
        var firstTools = new MailTools(application, guardrails);
        var secondTools = new MailTools(application, guardrails);

        Assert.False((await firstTools.ReadMailAsync("mail-ref")).IsError);
        Assert.False((await secondTools.ReadMailAsync("mail-ref")).IsError);
        var stopped = await firstTools.ReadMailAsync("mail-ref");
        AssertBudgetStop(stopped);
        Assert.DoesNotContain(ContentMarker, Payload(stopped).GetRawText(), StringComparison.Ordinal);

        now += TimeSpan.FromSeconds(61);
        Assert.False((await secondTools.ReadMailAsync("mail-ref")).IsError);
    }

    [Fact]
    public async Task StatusAndEmptyDiscoveryResultsDoNotConsumeOutputBudget()
    {
        var budget = new RecordingBudget(new()) { RejectCharges = true };
        var tools = new MailTools(new SampleApplication(), budget);

        var results = new[]
        {
            await tools.GetStatusAsync(),
            await tools.ListAccountsAsync(),
            await tools.SearchMailAsync("sample"),
            await tools.ListCalendarsAsync(),
            await tools.SearchEventsAsync(Start, End)
        };

        Assert.All(results, result => Assert.False(result.IsError));
        Assert.Empty(budget.AttemptedCharges);
        Assert.True(Payload(results[0]).TryGetProperty("read_guardrails", out _));
    }

    [Fact]
    public async Task BudgetRefusalStillReturnsSafeStopAdviceWithoutChargingTheNotification()
    {
        var budget = new RecordingBudget(new()) { RejectCharges = true };
        var tools = new MailTools(new SampleApplication { Body = ContentMarker }, budget);

        var result = await tools.ReadMailAsync("mail-ref");

        AssertBudgetStop(result);
        Assert.Single(budget.AttemptedCharges);
        Assert.Empty(budget.Charges);
        var encoded = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        Assert.DoesNotContain(ContentMarker, encoded, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateDiagnostic, encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token-example", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventDescriptionsAreChargedAndCanExhaustTheSameOutputWindow()
    {
        var budget = new RecordingBudget(new()) { RejectCharges = true };
        var tools = new MailTools(new SampleApplication { Body = ContentMarker }, budget);

        var result = await tools.ReadEventAsync("event-ref");

        AssertBudgetStop(result);
        Assert.Single(budget.AttemptedCharges);
        Assert.DoesNotContain(ContentMarker, Payload(result).GetRawText(), StringComparison.Ordinal);
    }

    private static JsonElement Payload(CallToolResult result)
    {
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal(structured.GetRawText(), text.Text);
        return structured;
    }

    private static void AssertBudgetStop(CallToolResult result)
    {
        Assert.True(result.IsError);
        var payload = Payload(result);
        var error = payload.GetProperty("error");
        Assert.Equal("read_budget_exceeded", error.GetProperty("code").GetString());
        Assert.Contains("incomplete", error.GetProperty("explanation").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stop", error.GetProperty("action").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not retry immediately or reconnect", error.GetProperty("action").GetString(), StringComparison.Ordinal);
        var notification = payload.GetProperty("user_notification");
        Assert.True(notification.GetProperty("required").GetBoolean());
        Assert.Contains("Do not describe this failure as an empty inbox or an empty calendar",
            notification.GetProperty("instruction").GetString(), StringComparison.Ordinal);
    }

    private sealed class RecordingBudget : IReadBudget
    {
        internal RecordingBudget(ReadGuardrailLimits limits) { limits.Validate(); Limits = limits; }
        public ReadGuardrailLimits Limits { get; }
        internal bool RejectCharges { get; init; }
        internal List<int> AttemptedCharges { get; } = [];
        internal List<int> Charges { get; } = [];
        public Task AdmitReadAsync(bool detail, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Application admission is outside these MCP output tests.");
        public Task ChargeOutputAsync(int bytes, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttemptedCharges.Add(bytes);
            if (RejectCharges) throw new ProviderReadException(PrivateDiagnostic, ReadFailureKind.BudgetExceeded);
            Charges.Add(bytes);
            return Task.CompletedTask;
        }
    }

    private sealed class SampleApplication : IMailMeUpApplication
    {
        internal string Body { get; init; } = "Synthetic message";
        public ApplicationStatus GetStatus() => new("synthetic", "stdio", true, false, []);
        public Task<MailSearchPreferences> GetMailSearchPreferencesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new MailSearchPreferences());
        public Task<IReadOnlyList<Account>> ListSharedAccountsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Account>>([]);
        public Task<MailSearchResult> SearchMailAsync(MailSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MailSearchResult([], [], [], true, null));
        public Task<CalendarListResult> ListCalendarsAsync(CalendarListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CalendarListResult([], [], [], true));
        public Task<EventSearchResult> SearchEventsAsync(EventSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EventSearchResult([], [], [], true, null));
        public Task<MailMessageResult> ReadMailAsync(MailReadRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MailMessageResult("mail-ref", "microsoft:sample", "Synthetic subject", "sender@example.test",
                ["recipient@example.test"], [], new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), Body, 0, false));
        public Task<EventResult> ReadEventAsync(EventReadRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EventResult("event-ref", "calendar-ref", "microsoft:sample", "Synthetic appointment", Start, End,
                false, false, "Synthetic room", Body, false, ["attendee@example.test"], null));

        public Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountSharingSettings>> ListAccountSharingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountSharingSettings> SaveAccountSharingAsync(AccountSharingSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MailSearchPreferences> SaveMailSearchPreferencesAsync(MailSearchPreferences preferences, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderCalendar>> ListAvailableCalendarsAsync(string accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountConnectionCheckResult> CheckConnectionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderSetupStatus>> ListProviderSetupAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderSetupResult> ConfigureProviderAsync(string providerId, string source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountConnectionResult> ConnectAccountAsync(string providerId, AccountConnectionOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountRemovalResult> RemoveAccountAsync(string accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
