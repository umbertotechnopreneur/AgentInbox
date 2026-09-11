using System.Net;
using MailMeUp.Core;
using MailMeUp.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailMeUp.Tests;

public sealed class ProviderDiagnosticsTests
{
    [Fact]
    public async Task ExplicitNoContentListIsEmptyAndDoesNotAttemptJsonParsing()
    {
        var logger = new CaptureLogger();
        using var client = new HttpClient(new ResponseHandler(string.Empty, HttpStatusCode.NoContent));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.example.test/messages");
        using var document = await ProviderHttpDiagnostics.ReadJsonAsync(
            client, request, logger, "gmail.messages.list", 1024, _ => ReadFailureKind.Unknown,
            CancellationToken.None, allowNoContent: true);

        Assert.Empty(document.RootElement.EnumerateObject());
        Assert.Contains("empty list: status=204", logger.Text);
        Assert.DoesNotContain("response_parse", logger.Text);
        Assert.DoesNotContain("JsonReaderException", logger.Text);
    }

    [Fact]
    public async Task NoContentIsNotAcceptedForARequiredDetailResponse()
    {
        var logger = new CaptureLogger();
        using var client = new HttpClient(new ResponseHandler(string.Empty, HttpStatusCode.NoContent));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.example.test/messages/item");
        var error = await Assert.ThrowsAsync<ProviderReadException>(() => ProviderHttpDiagnostics.ReadJsonAsync(
            client, request, logger, "gmail.messages.get", 1024, _ => ReadFailureKind.Unknown, CancellationToken.None));

        Assert.Equal(ReadFailureKind.ProviderUnavailable, error.Kind);
        Assert.Contains("response_empty", logger.Text);
        Assert.DoesNotContain("returned an empty list", logger.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    public async Task EmptyListCompatibilityDoesNotHideInvalidHttp200Bodies(string body)
    {
        var logger = new CaptureLogger();
        using var client = new HttpClient(new ResponseHandler(body));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.example.test/messages");
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => ProviderHttpDiagnostics.ReadJsonAsync(
            client, request, logger, "gmail.messages.list", 1024, _ => ReadFailureKind.Unknown,
            CancellationToken.None, allowNoContent: true));

        Assert.Contains("response_parse", logger.Text);
        Assert.DoesNotContain("returned an empty list", logger.Text);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ReadFailureKind.InvalidRequest)]
    [InlineData(HttpStatusCode.Unauthorized, ReadFailureKind.SignInRequired)]
    [InlineData(HttpStatusCode.Forbidden, ReadFailureKind.AccessDenied)]
    [InlineData(HttpStatusCode.TooManyRequests, ReadFailureKind.ProviderUnavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ReadFailureKind.ProviderUnavailable)]
    public async Task EmptyListCompatibilityPreservesHttpFailures(HttpStatusCode status, ReadFailureKind kind)
    {
        var logger = new CaptureLogger();
        using var client = new HttpClient(new ResponseHandler("{}", status));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.example.test/messages");
        var error = await Assert.ThrowsAsync<ProviderReadException>(() => ProviderHttpDiagnostics.ReadJsonAsync(
            client, request, logger, "gmail.messages.list", 1024, _ => kind,
            CancellationToken.None, allowNoContent: true));

        Assert.Equal(kind, error.Kind);
        Assert.DoesNotContain("returned an empty list", logger.Text);
    }

    [Fact]
    public async Task NoContentDoesNotTurnCallerCancellationIntoAnEmptyResult()
    {
        var logger = new CaptureLogger();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var client = new HttpClient(new ResponseHandler(string.Empty, HttpStatusCode.NoContent));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.example.test/messages");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProviderHttpDiagnostics.ReadJsonAsync(
            client, request, logger, "gmail.messages.list", 1024, _ => ReadFailureKind.Unknown,
            cancellation.Token, allowNoContent: true));

        Assert.DoesNotContain("returned an empty list", logger.Text);
    }

    [Fact]
    public async Task HttpFailureLogsOnlyAllowlistedMetadata()
    {
        var logger = new CaptureLogger();
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""
                {"error":{"code":400,"message":"secret-token one@example.test private query",
                "status":"INVALID_ARGUMENT","errors":[{"reason":"badRequest","message":"secret-token"}]}}
                """)
        };
        response.Headers.TryAddWithoutValidation("request-id", "11111111-2222-3333-4444-555555555555");
        var error = await Assert.ThrowsAsync<ProviderReadException>(() => ProviderHttpDiagnostics.EnsureSuccessAsync(
            response, logger, "gmail.messages.list", _ => ReadFailureKind.InvalidRequest, CancellationToken.None));

        Assert.Equal(ReadFailureKind.InvalidRequest, error.Kind);
        Assert.Contains("status=400", logger.Text);
        Assert.Contains("INVALID_ARGUMENT,badRequest", logger.Text);
        Assert.Contains("11111111-2222-3333-4444-555555555555", logger.Text);
        Assert.DoesNotContain("secret-token", logger.Text);
        Assert.DoesNotContain("example.test", logger.Text);
        Assert.DoesNotContain("private query", logger.Text);
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":\"sensitive_opaque_token\"}}", "parsed")]
    [InlineData("<html>sensitive_opaque_token</html>", "unreadable")]
    public async Task ArbitraryCodesAndMalformedErrorBodiesAreNeverLogged(string body, string bodyState)
    {
        var logger = new CaptureLogger();
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(body) };
        response.Headers.TryAddWithoutValidation("request-id", "sensitive_opaque_token");
        await Assert.ThrowsAsync<ProviderReadException>(() => ProviderHttpDiagnostics.EnsureSuccessAsync(
            response, logger, "graph.messages.list", _ => ReadFailureKind.AccessDenied, CancellationToken.None));
        Assert.Contains("status=403", logger.Text);
        Assert.Contains("errorBody=" + bodyState, logger.Text);
        Assert.DoesNotContain("sensitive_opaque_token", logger.Text);
    }

    [Fact]
    public async Task DetailedClassificationReceivesOnlyAllowlistedCodesAndValidRetryDelay()
    {
        var logger = new CaptureLogger();
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""
                {"error":{"status":"PERMISSION_DENIED","message":"private-token reader@example.test",
                "errors":[{"reason":"rateLimitExceeded"},{"reason":"private-token"}]}}
                """)
        };
        response.Headers.TryAddWithoutValidation("Retry-After", "5");
        ProviderHttpFailure? captured = null;

        var error = await Assert.ThrowsAsync<ProviderReadException>(() => ProviderHttpDiagnostics.EnsureSuccessAsync(
            response, logger, "gmail.messages.get", _ => ReadFailureKind.AccessDenied, CancellationToken.None,
            classifyFailure: failure =>
            {
                captured = failure;
                return ReadFailureKind.RateLimited;
            }));

        Assert.Equal(ReadFailureKind.RateLimited, error.Kind);
        Assert.NotNull(captured);
        Assert.Equal(403, captured.StatusCode);
        Assert.Equal(new[] { "PERMISSION_DENIED", "rateLimitExceeded" }, captured.Codes);
        Assert.Equal(TimeSpan.FromSeconds(5), captured.RetryAfter);
        Assert.Contains("category=RateLimited", logger.Text);
        Assert.DoesNotContain("private-token", logger.Text);
        Assert.DoesNotContain("example.test", logger.Text);
    }

    [Fact]
    public async Task MalformedRetryAfterDoesNotHideTheOriginalHttpFailure()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") };
        response.Headers.TryAddWithoutValidation("Retry-After", "invalid-provider-value");
        var error = await Assert.ThrowsAsync<ProviderReadException>(() => ProviderHttpDiagnostics.EnsureSuccessAsync(
            response, new CaptureLogger(), "gmail.messages.get", _ => ReadFailureKind.AccessDenied, CancellationToken.None,
            classifyFailure: failure =>
            {
                Assert.Null(failure.RetryAfter);
                return ReadFailureKind.AccessDenied;
            }));
        Assert.Equal(ReadFailureKind.AccessDenied, error.Kind);
    }

    [Fact]
    public async Task OversizedErrorBodyDoesNotHideHttpFailure()
    {
        var logger = new CaptureLogger();
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(new string('x', 20_000))
        };
        await Assert.ThrowsAsync<ProviderReadException>(() => ProviderHttpDiagnostics.EnsureSuccessAsync(
            response, logger, "gmail.messages.list", _ => ReadFailureKind.ProviderUnavailable, CancellationToken.None));
        Assert.Contains("status=502", logger.Text);
        Assert.Contains("errorBody=too_large", logger.Text);
    }

    [Theory]
    [InlineData("not-json", 1024, "response_parse")]
    [InlineData("{\"private\":\"private-content\"}", 8, "response_size_limit")]
    public async Task SuccessfulHttpWithUnreadablePayloadLogsTheActualPhase(string body, int maximumBytes, string phase)
    {
        var logger = new CaptureLogger();
        using var client = new HttpClient(new ResponseHandler(body));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.example.test/private-query");
        await Assert.ThrowsAnyAsync<Exception>(() => ProviderHttpDiagnostics.ReadJsonAsync(
            client, request, logger, "gmail.messages.metadata", maximumBytes, _ => ReadFailureKind.Unknown, CancellationToken.None));
        Assert.Contains(phase, logger.Text);
        Assert.Contains("status=200", logger.Text);
        Assert.DoesNotContain("private-content", logger.Text);
        Assert.DoesNotContain("private-query", logger.Text);
    }

    [Fact]
    public void AccountScopesArePseudonymousAndOriginalExceptionMessagesAreExcluded()
    {
        var logger = new CaptureLogger();
        var account = new Account("google:synthetic-id", "google", "Private Name", "one@example.test");
        using (ReadDiagnostics.Begin(logger, account, "search_mail"))
            ReadDiagnostics.Failure(logger, new InvalidOperationException("private-token one@example.test"), "provider_operation");
        Assert.Contains(ReadDiagnostics.AccountKey(account.Id), logger.Text);
        Assert.Contains("InvalidOperationException", logger.Text);
        Assert.DoesNotContain("synthetic-id", logger.Text);
        Assert.DoesNotContain("private-token", logger.Text);
        Assert.DoesNotContain("example.test", logger.Text);
        Assert.DoesNotContain("Private Name", logger.Text);
    }

    [Fact]
    public async Task ConcurrentAccountScopesDoNotBleedIntoEachOther()
    {
        var logger = new CaptureLogger();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task Write(string id)
        {
            using var scope = ReadDiagnostics.Begin(logger, new Account(id, "google", "Sample", "one@example.test"), "search_mail");
            if (Interlocked.Increment(ref arrivals) == 2)
                ready.SetResult();
            await ready.Task;
            logger.LogWarning("Completed {ExpectedKey}", ReadDiagnostics.AccountKey(id));
        }
        await Task.WhenAll(Write("one"), Write("two"));
        var lines = logger.Lines.Where(line => line.Contains("Completed", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.False(line.Contains(ReadDiagnostics.AccountKey("one"), StringComparison.Ordinal) &&
            line.Contains(ReadDiagnostics.AccountKey("two"), StringComparison.Ordinal)));
    }

    private sealed class ResponseHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class CaptureLogger : ILogger
    {
        private readonly LoggerExternalScopeProvider _scopes = new();
        public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();
        public string Text => string.Join('\n', Lines);
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => _scopes.Push(state);
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            var parts = new List<string> { formatter(state, exception) };
            _scopes.ForEachScope((scope, output) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> properties)
                    output.AddRange(properties.Select(pair => $"{pair.Key}={pair.Value}"));
            }, parts);
            Lines.Enqueue(string.Join(' ', parts));
        }
    }
}
