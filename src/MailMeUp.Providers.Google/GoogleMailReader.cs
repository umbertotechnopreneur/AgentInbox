using System.Globalization;
using System.Net;
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
    private const int MaximumSummaryPartDepth = 16;
    private static readonly string SummaryFields = CreateSummaryFields();
    private readonly ILogger<GoogleMailReader> _logger;
    private readonly GoogleAccessTokenProvider _tokens;
    private readonly IProviderRequestGovernor _governor;

    /// <summary>Creates a Gmail reader backed by protected Google account tokens.</summary>
    public GoogleMailReader(IProviderConfigurationStore configurations, ISecretStore secrets,
        ILogger<GoogleMailReader>? logger = null, IProviderRequestGovernor? governor = null)
    {
        _logger = logger ?? NullLogger<GoogleMailReader>.Instance;
        _tokens = new GoogleAccessTokenProvider(configurations, secrets, _logger);
        _governor = governor ?? InMemoryReadGuardrails.Shared;
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
        _logger.LogDebug("Mail request shape: text={HasText}; sender={HasSender}; recipient={HasRecipient}; start={HasStart}; end={HasEnd}; unread={UnreadOnly}; inbox={InboxOnly}; attachments={HasAttachmentFilter}; continuation={HasContinuation}; limit={Limit}",
            !string.IsNullOrWhiteSpace(query.Text), query.Sender is not null, query.RecipientContains is not null,
            query.Start.HasValue, query.End.HasValue, query.UnreadOnly, query.InboxOnly, query.HasAttachments.HasValue, cursor is not null, limit);
        if (query.Text.Length > 500 || query.Text.Any(char.IsControl) || limit is < 1 or > 50 || cursor is { Length: > 4_096 })
        {
            throw new ArgumentException("The Gmail search page is invalid.");
        }

        try
        {
            var accessToken = await _tokens.GetAsync(account, cancellationToken);
            var url = CreateListRequestUrl(query, limit, cursor);

            using var page = await GetJsonAsync(account, url, accessToken, "gmail.messages.list", cancellationToken);
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

                    var summary = await ReadSummaryAsync(account, id, accessToken, query.InboxOnly, cancellationToken);
                    if (summary is not null)
                    {
                        summaries.Add(summary);
                    }
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
            using var document = await GetJsonAsync(account, url, accessToken, "gmail.messages.get", cancellationToken);
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

    private async Task<ProviderMailSummary?> ReadSummaryAsync(
        Account account,
        string messageId,
        string accessToken,
        bool inboxOnly,
        CancellationToken cancellationToken)
    {
        ValidateMessageId(messageId);
        var url = CreateSummaryRequestUrl(messageId);
        using var document = await GetJsonAsync(account, url, accessToken, "gmail.messages.metadata", cancellationToken);
        return ParseSearchSummary(messageId, document.RootElement, inboxOnly);
    }

    internal static string CreateListRequestUrl(ProviderMailQuery query, int limit, string? cursor)
    {
        var url = new StringBuilder("https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=")
            .Append(limit.ToString(CultureInfo.InvariantCulture))
            .Append("&q=").Append(Uri.EscapeDataString(CreateProviderQuery(query)))
            .Append("&includeSpamTrash=false")
            .Append("&fields=messages(id%2CthreadId)%2CnextPageToken%2CresultSizeEstimate");
        if (query.InboxOnly)
        {
            // A separate label constraint also applies when free-text search contains an OR.
            url.Append("&labelIds=INBOX");
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            url.Append("&pageToken=").Append(Uri.EscapeDataString(cursor));
        }

        return url.ToString();
    }

    internal static ProviderMailSummary? ParseSearchSummary(string messageId, JsonElement root, bool inboxOnly)
    {
        // A rule or another client can move the message after the IDs were listed.
        if (inboxOnly && (!HasLabel(root, "INBOX") || HasLabel(root, "SPAM") || HasLabel(root, "TRASH")))
        {
            return null;
        }

        return ParseSummary(messageId, root);
    }

    internal static string CreateSummaryRequestUrl(string messageId) =>
        $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}" +
        "?format=full&fields=" + Uri.EscapeDataString(SummaryFields);

    internal static ProviderMailSummary ParseSummary(string messageId, JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            throw new ProviderReadException("Gmail returned a message without its MIME structure.", ReadFailureKind.ProviderUnavailable);
        var headers = ReadHeaders(payload);
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
            HasAttachments: HasAttachmentPart(payload, 0),
            Recipients: recipients);
    }

    private static string CreateSummaryFields()
    {
        // A final part ID exposes deeper nesting without selecting its body or silently reporting no attachments.
        var part = "mimeType,filename,parts(partId)";
        for (var depth = MaximumSummaryPartDepth - 1; depth >= 0; depth--)
            part = "mimeType,filename,parts(" + part + ")";
        return "id,internalDate,snippet,labelIds,payload(headers(name,value)," + part + ")";
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

    private Task<JsonDocument> GetJsonAsync(Account account, string url, string accessToken, string endpoint, CancellationToken cancellationToken) =>
        GoogleReadRequests.Shared.GetJsonAsync(
            account, url, accessToken, _logger, endpoint, MaximumJsonBytes, cancellationToken,
            allowNoContent: endpoint == "gmail.messages.list", governor: _governor);

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

    private static bool HasAttachmentPart(JsonElement part, int depth)
    {
        if (depth > MaximumSummaryPartDepth)
            throw new ProviderReadException("The message MIME structure exceeds the supported preview depth.", ReadFailureKind.ResultLimit);
        if (!string.IsNullOrWhiteSpace(GetOptionalString(part, "filename")))
            return true;
        return part.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array &&
            parts.EnumerateArray().Any(child => HasAttachmentPart(child, depth + 1));
    }

    internal static string ReadBody(JsonElement payload)
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
        // Attached files are not the message body, including text attachments.
        if (!string.IsNullOrWhiteSpace(GetOptionalString(part, "filename")))
            return;
        var mimeType = GetOptionalString(part, "mimeType");
        var isPlain = string.Equals(mimeType, "text/plain", StringComparison.OrdinalIgnoreCase);
        var isHtml = string.Equals(mimeType, "text/html", StringComparison.OrdinalIgnoreCase);
        if ((isPlain || isHtml) && part.TryGetProperty("body", out var body))
        {
            var data = GetOptionalString(body, "data");
            if (string.IsNullOrWhiteSpace(data) && !string.IsNullOrWhiteSpace(GetOptionalString(body, "attachmentId")))
                throw new ProviderReadException("The message text is stored separately and could not be read completely.", ReadFailureKind.ResultLimit);
            if (!string.IsNullOrWhiteSpace(data))
            {
                var decoded = DecodeBase64Url(data);
                if (isPlain)
                {
                    plain.Add(decoded);
                }
                else
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
}
