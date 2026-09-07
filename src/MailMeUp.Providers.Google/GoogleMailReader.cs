using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MailMeUp.Core;
using MailMeUp.Diagnostics;
using MailMeUp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailMeUp.Providers.Google;

/// <summary>Reads Gmail search results and selected messages without changing mailbox state.</summary>
public sealed class GoogleMailReader : IMailReader
{
    private const int MaximumJsonBytes = 12 * 1024 * 1024;
    private static readonly HttpClient HttpClient = new();
    private readonly ILogger<GoogleMailReader> _logger;
    private readonly GoogleAccessTokenProvider _tokens;

    /// <summary>Creates a Gmail reader backed by protected Google account tokens.</summary>
    public GoogleMailReader(IProviderConfigurationStore configurations, ISecretStore secrets, ILogger<GoogleMailReader>? logger = null)
    {
        _logger = logger ?? NullLogger<GoogleMailReader>.Instance;
        _tokens = new GoogleAccessTokenProvider(configurations, secrets, _logger);
    }

    /// <inheritdoc />
    public string ProviderId => "google";

    /// <inheritdoc />
    public async Task<ProviderMailSearchPage> SearchAsync(
        Account account,
        ProviderMailQuery query,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        ValidateMailAccount(account);
        using var diagnostics = ReadDiagnostics.Begin(_logger, account, "search_mail");
        ArgumentNullException.ThrowIfNull(query);
        _logger.LogDebug("Mail request shape: text={HasText}; sender={HasSender}; recipient={HasRecipient}; start={HasStart}; end={HasEnd}; unread={UnreadOnly}; attachments={HasAttachmentFilter}; continuation={HasContinuation}; limit={Limit}",
            !string.IsNullOrWhiteSpace(query.Text), query.Sender is not null, query.RecipientContains is not null,
            query.Start.HasValue, query.End.HasValue, query.UnreadOnly, query.HasAttachments.HasValue, cursor is not null, limit);
        if (query.Text.Length > 500 || query.Text.Any(char.IsControl) || limit is < 1 or > 50 || cursor is { Length: > 4_096 })
        {
            throw new ArgumentException("The Gmail search page is invalid.");
        }

        try
        {
            var accessToken = await _tokens.GetAsync(account, cancellationToken);
            var url = new StringBuilder("https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=")
                .Append(limit.ToString(CultureInfo.InvariantCulture))
                .Append("&q=").Append(Uri.EscapeDataString(CreateProviderQuery(query)))
                .Append("&includeSpamTrash=false")
                .Append("&fields=messages(id%2CthreadId)%2CnextPageToken%2CresultSizeEstimate")
                .ToString();
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                url += "&pageToken=" + Uri.EscapeDataString(cursor);
            }

            using var page = await GetJsonAsync(url, accessToken, "gmail.messages.list", cancellationToken);
            var summaries = new List<ProviderMailSummary>();
            if (page.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in messages.EnumerateArray().Take(limit))
                {
                    if (!item.TryGetProperty("id", out var idProperty) || idProperty.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var id = idProperty.GetString();
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    summaries.Add(await ReadSummaryAsync(id, accessToken, cancellationToken));
                }
            }

            var next = GetOptionalString(page.RootElement, "nextPageToken");
            return new ProviderMailSearchPage(summaries, next);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProviderReadException exception)
        {
            ReadDiagnostics.Failure(_logger, exception, "provider_operation");
            throw;
        }
        catch (HttpRequestException exception)
        {
            ReadDiagnostics.Failure(_logger, exception, "provider_operation");
            throw new ProviderReadException("The provider could not be reached.", ReadFailureKind.Network);
        }
        catch (Exception exception)
        {
            ReadDiagnostics.Failure(_logger, exception, "provider_operation");
            throw new ProviderReadException("Gmail search failed.");
        }
    }

    /// <inheritdoc />
    public async Task<ProviderMailMessage> ReadAsync(
        Account account,
        string providerMessageId,
        CancellationToken cancellationToken = default)
    {
        ValidateMailAccount(account);
        using var diagnostics = ReadDiagnostics.Begin(_logger, account, "read_mail");
        ValidateMessageId(providerMessageId);
        try
        {
            var accessToken = await _tokens.GetAsync(account, cancellationToken);
            var url = $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(providerMessageId)}?format=full";
            using var document = await GetJsonAsync(url, accessToken, "gmail.messages.get", cancellationToken);
            var root = document.RootElement;
            var headers = root.TryGetProperty("payload", out var payload)
                ? ReadHeaders(payload)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var text = root.TryGetProperty("payload", out payload) ? ReadBody(payload) : string.Empty;
            return new ProviderMailMessage(
                providerMessageId,
                GetHeader(headers, "Subject", "(no subject)"),
                GetHeader(headers, "From", string.Empty),
                AsHeaderList(headers, "To"),
                AsHeaderList(headers, "Cc"),
                ReadInternalDate(root),
                text,
                IsRead: !HasLabel(root, "UNREAD"),
                HasAttachments: payload.ValueKind != JsonValueKind.Undefined && HasAttachmentPart(payload));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProviderReadException exception)
        {
            ReadDiagnostics.Failure(_logger, exception, "provider_operation");
            throw;
        }
        catch (HttpRequestException exception)
        {
            ReadDiagnostics.Failure(_logger, exception, "provider_operation");
            throw new ProviderReadException("The provider could not be reached.", ReadFailureKind.Network);
        }
        catch (Exception exception)
        {
            ReadDiagnostics.Failure(_logger, exception, "provider_operation");
            throw new ProviderReadException("The Gmail message could not be read.");
        }
    }

    private async Task<ProviderMailSummary> ReadSummaryAsync(
        string messageId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        ValidateMessageId(messageId);
        var url = $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}" +
                  "?format=metadata&metadataHeaders=Subject&metadataHeaders=From" +
                  "&metadataHeaders=To&metadataHeaders=Cc" +
                  "&fields=id%2CinternalDate%2Csnippet%2ClabelIds%2Cpayload%2Fheaders%2Cpayload%2Fparts";
        using var document = await GetJsonAsync(url, accessToken, "gmail.messages.metadata", cancellationToken);
        var root = document.RootElement;
        var headers = root.TryGetProperty("payload", out var payload)
            ? ReadHeaders(payload)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var recipients = AsHeaderList(headers, "To")
            .Concat(AsHeaderList(headers, "Cc"))
            .ToArray();
        return new ProviderMailSummary(
            messageId,
            GetHeader(headers, "Subject", "(no subject)"),
            GetHeader(headers, "From", string.Empty),
            ReadInternalDate(root),
            GetOptionalString(root, "snippet") ?? string.Empty,
            IsRead: !HasLabel(root, "UNREAD"),
            HasAttachments: payload.ValueKind != JsonValueKind.Undefined && HasAttachmentPart(payload),
            Recipients: recipients);
    }

    private static string CreateProviderQuery(ProviderMailQuery query)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            parts.Add(query.Text);
        }

        if (!string.IsNullOrWhiteSpace(query.Sender))
        {
            parts.Add($"from:\"{query.Sender.Replace("\"", string.Empty, StringComparison.Ordinal)}\"");
        }

        if (!string.IsNullOrWhiteSpace(query.RecipientContains))
        {
            parts.Add($"to:\"{query.RecipientContains.Replace("\"", string.Empty, StringComparison.Ordinal)}\"");
        }

        if (query.UnreadOnly)
        {
            parts.Add("is:unread");
        }

        if (query.HasAttachments is not null)
        {
            parts.Add(query.HasAttachments.Value ? "has:attachment" : "-has:attachment");
        }

        if (query.Start is not null)
        {
            parts.Add($"after:{query.Start.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}");
        }

        if (query.End is not null)
        {
            parts.Add($"before:{query.End.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}");
        }

        return string.Join(' ', parts);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, string accessToken, string endpoint, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await ProviderHttpDiagnostics.ReadJsonAsync(
            HttpClient, request, _logger, endpoint, MaximumJsonBytes, ClassifyHttpFailure, cancellationToken,
            allowNoContent: endpoint == "gmail.messages.list");
    }

    private static Dictionary<string, string> ReadHeaders(JsonElement payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!payload.TryGetProperty("headers", out var headers) || headers.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var header in headers.EnumerateArray())
        {
            var name = GetOptionalString(header, "name");
            var value = GetOptionalString(header, "value");
            if (!string.IsNullOrWhiteSpace(name) && value is not null)
            {
                result[name] = value;
            }
        }

        return result;
    }

    private static bool HasLabel(JsonElement root, string label)
    {
        if (!root.TryGetProperty("labelIds", out var labels) || labels.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return labels.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.String &&
            string.Equals(item.GetString(), label, StringComparison.Ordinal));
    }

    private static bool HasAttachmentPart(JsonElement part)
    {
        if (part.TryGetProperty("filename", out var filename) &&
            filename.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(filename.GetString()))
        {
            return true;
        }

        return part.TryGetProperty("parts", out var parts) &&
               parts.ValueKind == JsonValueKind.Array &&
               parts.EnumerateArray().Any(HasAttachmentPart);
    }

    private static string ReadBody(JsonElement payload)
    {
        var plain = new List<string>();
        var html = new List<string>();
        CollectBodies(payload, plain, html);
        if (plain.Count > 0)
        {
            return string.Join("\n\n", plain.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        return html.Count == 0 ? string.Empty : HtmlToText(string.Join("\n", html));
    }

    private static void CollectBodies(JsonElement part, List<string> plain, List<string> html)
    {
        var mimeType = GetOptionalString(part, "mimeType");
        if (part.TryGetProperty("body", out var body))
        {
            var data = GetOptionalString(body, "data");
            if (!string.IsNullOrWhiteSpace(data))
            {
                var decoded = DecodeBase64Url(data);
                if (string.Equals(mimeType, "text/plain", StringComparison.OrdinalIgnoreCase))
                {
                    plain.Add(decoded);
                }
                else if (string.Equals(mimeType, "text/html", StringComparison.OrdinalIgnoreCase))
                {
                    html.Add(decoded);
                }
            }
        }

        if (part.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in parts.EnumerateArray())
            {
                CollectBodies(child, plain, html);
            }
        }
    }

    private static string DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
    }

    private static string HtmlToText(string html)
    {
        var withoutTags = Regex.Replace(
            html,
            "<[^>]+>",
            " ",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var decoded = WebUtility.HtmlDecode(withoutTags) ?? string.Empty;
        return string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static DateTimeOffset ReadInternalDate(JsonElement root)
    {
        var value = GetOptionalString(root, "internalDate");
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : DateTimeOffset.UnixEpoch;
    }

    private static string? GetOptionalString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string GetHeader(IReadOnlyDictionary<string, string> headers, string name, string fallback) =>
        headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static IReadOnlyList<string> AsHeaderList(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? [value] : [];

    private static void ValidateMailAccount(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!string.Equals(account.Provider, "google", StringComparison.Ordinal) || !account.MailReadEnabled)
        {
            throw new ArgumentException("The account has no Gmail read access.", nameof(account));
        }
    }

    private static void ValidateMessageId(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (messageId.Length > 256 || messageId.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("The Gmail message identifier is invalid.", nameof(messageId));
        }
    }
    private static ReadFailureKind ClassifyHttpFailure(int statusCode) => statusCode switch
    {
        400 => ReadFailureKind.InvalidRequest,
        401 => ReadFailureKind.SignInRequired,
        403 => ReadFailureKind.AccessDenied,
        404 or 410 => ReadFailureKind.ItemUnavailable,
        408 or 504 => ReadFailureKind.Timeout,
        429 or >= 500 => ReadFailureKind.ProviderUnavailable,
        _ => ReadFailureKind.Unknown
    };
}
