using System.Diagnostics;
using System.Net;
using System.Text.Json;
using MailMeUp.Core;
using Microsoft.Extensions.Logging;

namespace MailMeUp.Diagnostics;

/// <summary>Reads bounded provider responses and records allowlisted failure metadata, never response text or URLs.</summary>
public static class ProviderHttpDiagnostics
{
    private const int MaximumErrorBytes = 16 * 1024;
    private static readonly HashSet<string> AllowedCodes = new(StringComparer.Ordinal)
    {
        "badRequest", "invalidArgument", "invalidParameter", "failedPrecondition", "conditionNotMet",
        "authError", "forbidden", "insufficientPermissions", "accessNotConfigured", "domainPolicy",
        "notFound", "rateLimitExceeded", "userRateLimitExceeded", "dailyLimitExceeded", "quotaExceeded",
        "backendError", "internalError", "INVALID_ARGUMENT", "FAILED_PRECONDITION", "UNAUTHENTICATED",
        "PERMISSION_DENIED", "RESOURCE_EXHAUSTED", "NOT_FOUND", "INTERNAL", "UNAVAILABLE",
        "BadRequest", "Request_BadRequest", "ErrorInvalidRequest", "ErrorInvalidUrlQueryFilter",
        "InefficientFilter", "SearchQueryTooLong", "ErrorInvalidSearchQuery", "InvalidAuthenticationToken",
        "ErrorAccessDenied", "Authorization_RequestDenied", "ErrorItemNotFound", "ResourceNotFound",
        "TooManyRequests", "ErrorTooManyObjectsOpened", "ErrorServerBusy", "ServiceUnavailable",
        "MailboxNotEnabledForRESTAPI", "ErrorMailboxNotEnabledForRESTAPI", "ErrorInvalidIdMalformed"
    };

    /// <summary>Sends a read request and parses bounded JSON; list adapters may explicitly normalize HTTP 204 to an empty object.</summary>
    public static async Task<JsonDocument> ReadJsonAsync(
        HttpClient client, HttpRequestMessage request, ILogger logger, string endpoint,
        int maximumBytes, Func<int, ReadFailureKind> classify, CancellationToken cancellationToken,
        bool allowNoContent = false)
    {
        var started = Stopwatch.GetTimestamp();
        var phase = "http_send";
        int? status = null;
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            status = (int)response.StatusCode;
            phase = "http_status";
            await EnsureSuccessAsync(response, logger, endpoint, classify, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                phase = "response_empty";
                if (!allowNoContent)
                    throw new ProviderReadException("The provider returned no content for a required response.", ReadFailureKind.ProviderUnavailable);

                logger.LogInformation("Provider HTTP {Endpoint} returned an empty list: status={HttpStatus}; elapsedMs={ElapsedMs}",
                    endpoint, status, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                return JsonDocument.Parse("{}");
            }
            phase = "response_read";
            if (response.Content.Headers.ContentLength > maximumBytes)
            {
                phase = "response_size_limit";
                throw new ProviderReadException("The provider response is too large.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[32 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, maximumBytes - buffer.Length + 1)), cancellationToken);
                if (read == 0)
                {
                    break;
                }
                if (buffer.Length + read > maximumBytes)
                {
                    phase = "response_size_limit";
                    throw new ProviderReadException("The provider response is too large.");
                }
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            phase = "response_parse";
            buffer.Position = 0;
            var document = await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new JsonException("The provider response is not a JSON object.");
            }
            logger.LogDebug("Provider HTTP {Endpoint} completed: status={HttpStatus}; elapsedMs={ElapsedMs}",
                endpoint, status, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return document;
        }
        catch (Exception exception)
        {
            // Preserve exception/cancellation behavior; callers still choose the user-facing category.
            logger.LogWarning("Provider HTTP {Endpoint} stopped at {Phase}: status={HttpStatus}; type={ErrorType}; elapsedMs={ElapsedMs}",
                endpoint, phase, status, exception.GetType().Name, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }

    /// <summary>Logs only status, allowlisted error codes and a GUID request ID before returning a sanitized failure.</summary>
    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response, ILogger logger, string endpoint,
        Func<int, ReadFailureKind> classify, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var (code, bodyState) = await ReadErrorCodeAsync(response, cancellationToken);
        var status = (int)response.StatusCode;
        logger.LogWarning(
            "Provider HTTP {Endpoint} rejected: status={HttpStatus}; category={FailureCategory}; code={ProviderErrorCode}; errorBody={ErrorBodyState}; requestId={ProviderRequestId}",
            endpoint, status, classify(status), code, bodyState, RequestId(response));
        cancellationToken.ThrowIfCancellationRequested();
        throw new ProviderReadException("The provider returned an unsuccessful response.", classify(status));
    }

    private static async Task<(string Code, string State)> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Reading an error body must not hide its HTTP status or consume the entire account deadline.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            if (response.Content.Headers.ContentLength > MaximumErrorBytes)
            {
                return ("unavailable", "too_large");
            }
            await using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[MaximumErrorBytes + 1];
            while (buffer.Length <= MaximumErrorBytes)
            {
                var read = await source.ReadAsync(chunk.AsMemory(0, MaximumErrorBytes + 1 - (int)buffer.Length), deadline.Token);
                if (read == 0)
                {
                    break;
                }
                buffer.Write(chunk, 0, read);
            }
            if (buffer.Length > MaximumErrorBytes)
            {
                return ("unavailable", "too_large");
            }

            using var document = JsonDocument.Parse(buffer.ToArray());
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            {
                return ("unrecognized", "parsed");
            }
            var codes = new List<string>();
            AddCode(error, "code", codes);
            AddCode(error, "status", codes);
            if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in errors.EnumerateArray().Take(4))
                {
                    AddCode(entry, "reason", codes);
                }
            }
            return (codes.Count == 0 ? "unrecognized" : string.Join(",", codes.Distinct()), "parsed");
        }
        catch (OperationCanceledException)
        {
            return ("unavailable", "cancelled_or_timeout");
        }
        catch (Exception)
        {
            return ("unavailable", "unreadable");
        }
    }

    private static void AddCode(JsonElement element, string name, List<string> codes)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String && property.GetString() is { } code && AllowedCodes.Contains(code))
        {
            codes.Add(code);
        }
    }

    private static string RequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("request-id", out var values) &&
        Guid.TryParseExact(values.FirstOrDefault(), "D", out var id) ? id.ToString("D") : "unavailable";
}
