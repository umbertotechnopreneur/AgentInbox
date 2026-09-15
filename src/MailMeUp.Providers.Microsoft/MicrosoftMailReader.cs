using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MailMeUp.Core;
using MailMeUp.Diagnostics;
using MailMeUp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailMeUp.Providers.Microsoft;

/// <summary>Reads Microsoft Graph mail search results and selected messages without changing mailbox state.</summary>
public sealed class MicrosoftMailReader : IMailReader
{
    private const int MaximumJsonBytes = 12 * 1024 * 1024;
    private static readonly HttpClient HttpClient = new();
    private readonly ILogger<MicrosoftMailReader> _logger;
    private readonly MicrosoftAccessTokenProvider _tokens;
    private readonly IProviderConfigurationStore _configurations;
    private readonly MicrosoftReadRequests _requests;
    private readonly MicrosoftExcludedFolderCache _excludedFolders = new();

    /// <summary>Creates a Microsoft mail reader backed by the protected MSAL cache.</summary>
    public MicrosoftMailReader(
        IProviderConfigurationStore configurations, ISecretStore secrets,
        ILogger<MicrosoftMailReader>? logger = null, IProviderRequestGovernor? governor = null)
    {
        _logger = logger ?? NullLogger<MicrosoftMailReader>.Instance;
        _tokens = new MicrosoftAccessTokenProvider(configurations, secrets, _logger);
        _configurations = configurations;
        _requests = new MicrosoftReadRequests(HttpClient, governor);
    }

    /// <inheritdoc />
    public string ProviderId => "microsoft";

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
        if (query.Text.Length > 500 || query.Text.Any(char.IsControl) || limit is < 1 or > 50)
        {
            throw new ArgumentException("The Microsoft mail search page is invalid.");
        }

        try
        {
            var accessToken = await _tokens.GetAsync(account, ["Mail.Read"], cancellationToken);
            IReadOnlyList<string> excludedFolderIds = [];
            if (!query.InboxOnly)
            {
                var configuration = await _configurations.GetAsync("microsoft", cancellationToken)
                    ?? throw new ProviderReadException("Microsoft app setup is missing.", ReadFailureKind.SetupRequired);
                excludedFolderIds = await _excludedFolders.GetAsync(configuration.ClientId, account.Id,
                    token => ReadExcludedFolderIdsAsync(account, accessToken, token), cancellationToken);
            }
            var url = string.IsNullOrWhiteSpace(cursor)
                ? CreateSearchUrl(query, limit, excludedFolderIds)
                : ValidateNextLink(cursor, query.InboxOnly);
            using var document = await GetJsonAsync(account, url, accessToken, preferText: false, "graph.messages.list", cancellationToken);
            return ParseSearchPage(document.RootElement, query, limit, excludedFolderIds);
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
            throw new ProviderReadException("Microsoft mail search failed.");
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
            var accessToken = await _tokens.GetAsync(account, ["Mail.Read"], cancellationToken);
            var url = $"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(providerMessageId)}" +
                      "?%24select=id%2Csubject%2Cfrom%2CtoRecipients%2CccRecipients%2CreceivedDateTime%2Cbody%2CisRead%2ChasAttachments";
            using var document = await GetJsonAsync(account, url, accessToken, preferText: true, "graph.messages.get", cancellationToken);
            var root = document.RootElement;
            var body = root.TryGetProperty("body", out var bodyProperty)
                ? GetOptionalString(bodyProperty, "content") ?? string.Empty
                : string.Empty;
            var contentType = root.TryGetProperty("body", out bodyProperty)
                ? GetOptionalString(bodyProperty, "contentType")
                : null;
            if (string.Equals(contentType, "html", StringComparison.OrdinalIgnoreCase))
            {
                body = HtmlToText(body);
            }

            return new ProviderMailMessage(
                providerMessageId,
                GetOptionalString(root, "subject") ?? "(no subject)",
                ReadSender(root),
                ReadRecipients(root, "toRecipients"),
                ReadRecipients(root, "ccRecipients"),
                ReadDate(root),
                body,
                IsRead: GetOptionalBoolean(root, "isRead") ?? true,
                HasAttachments: GetOptionalBoolean(root, "hasAttachments") ?? false);
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
            throw new ProviderReadException("The Microsoft message could not be read.");
        }
    }

    private static string CreateSearchUrl(
        ProviderMailQuery query,
        int limit,
        IReadOnlyList<string> excludedFolderIds)
    {
        var filterParts = new List<string>
        {
            $"receivedDateTime ge {FormatGraphDate(query.Start ?? new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero))}"
        };

        if (query.End is not null)
        {
            filterParts.Add($"receivedDateTime lt {FormatGraphDate(query.End.Value)}");
        }

        if (query.UnreadOnly)
        {
            filterParts.Add("isRead eq false");
        }

        if (query.HasAttachments is not null)
        {
            filterParts.Add($"hasAttachments eq {query.HasAttachments.Value.ToString().ToLowerInvariant()}");
        }

        filterParts.AddRange(excludedFolderIds.Select(id => $"parentFolderId ne '{EscapeODataString(id)}'"));

        var searchParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            // Keep an OR in provider text inside the structured date/address constraints.
            searchParts.Add($"({query.Text})");
        }

        if (!string.IsNullOrWhiteSpace(query.Sender))
        {
            searchParts.Add($"from:\"{EscapeSearchValue(query.Sender)}\"");
        }

        if (!string.IsNullOrWhiteSpace(query.RecipientContains))
        {
            searchParts.Add($"recipients:\"{EscapeSearchValue(query.RecipientContains)}\"");
        }

        var parameters = new List<string>
        {
            "%24select=id%2Csubject%2Cfrom%2CtoRecipients%2CccRecipients%2CreceivedDateTime%2CbodyPreview%2CisRead%2ChasAttachments%2CparentFolderId",
            "%24top=" + limit.ToString(CultureInfo.InvariantCulture)
        };

        if (searchParts.Count > 0)
        {
            // Graph message search rejects $filter and $orderby alongside $search.
            // KQL received constraints bound the provider scan; keep exact local filtering too.
            if (query.Start is { } start)
            {
                searchParts.Add($"received>={FormatSearchDate(start)}");
            }
            if (query.End is { } end)
            {
                searchParts.Add($"received<{FormatSearchDate(end)}");
            }
            var escapedSearch = string.Join(" AND ", searchParts)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
            parameters.Insert(0, "%24search=" + Uri.EscapeDataString($"\"{escapedSearch}\""));
        }
        else
        {
            // The broad receivedDateTime lower bound is first in $filter so Graph accepts this $orderby.
            parameters.Add("%24filter=" + Uri.EscapeDataString(string.Join(" and ", filterParts)));
            parameters.Add("%24orderby=receivedDateTime%20DESC");
        }

        var collection = query.InboxOnly ? "/v1.0/me/mailFolders/inbox/messages" : "/v1.0/me/messages";
        return "https://graph.microsoft.com" + collection + "?" + string.Join('&', parameters);
    }

    private static ProviderMailSearchPage ParseSearchPage(
        JsonElement root,
        ProviderMailQuery query,
        int limit,
        IReadOnlyList<string> excludedFolderIds)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("value", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The provider mail result list is missing or invalid.");
        }
        var summaries = new List<ProviderMailSummary>();
        if (root.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in values.EnumerateArray().Take(limit))
            {
                var id = GetOptionalString(item, "id");
                var parentFolderId = GetOptionalString(item, "parentFolderId");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(parentFolderId) ||
                    excludedFolderIds.Contains(parentFolderId, StringComparer.Ordinal))
                {
                    continue;
                }

                var summary = new ProviderMailSummary(
                    id,
                    GetOptionalString(item, "subject") ?? "(no subject)",
                    ReadSender(item),
                    ReadDate(item),
                    GetOptionalString(item, "bodyPreview") ?? string.Empty,
                    IsRead: GetOptionalBoolean(item, "isRead") ?? true,
                    HasAttachments: GetOptionalBoolean(item, "hasAttachments") ?? false,
                    Recipients: ReadRecipients(item, "toRecipients")
                        .Concat(ReadRecipients(item, "ccRecipients"))
                        .ToArray());
                if ((query.Start is null || summary.ReceivedAt >= query.Start.Value) &&
                    (query.End is null || summary.ReceivedAt < query.End.Value) &&
                    (!query.UnreadOnly || !summary.IsRead) &&
                    (query.HasAttachments is null || summary.HasAttachments == query.HasAttachments.Value))
                {
                    summaries.Add(summary);
                }
            }
        }

        // Keep the continuation even when this whole page was excluded; the application
        // refills within its existing time/page budget and applies sender/recipient filters.
        return new ProviderMailSearchPage(summaries, GetOptionalString(root, "@odata.nextLink"));
    }

    private async Task<IReadOnlyList<string>> ReadExcludedFolderIdsAsync(
        Account account,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var ids = new List<string>(2);
        foreach (var folder in new[] { "junkemail", "deleteditems" })
        {
            var url = $"https://graph.microsoft.com/v1.0/me/mailFolders/{folder}?%24select=id";
            using var document = await GetJsonAsync(account, url, accessToken, preferText: false, "graph.mailFolders.get", cancellationToken);
            var id = GetOptionalString(document.RootElement, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ProviderReadException("Microsoft mail folder exclusions could not be verified.");
            }

            ids.Add(id);
        }

        return ids;
    }

    private static string FormatGraphDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static string EscapeODataString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeSearchValue(string value) => value.Replace("\"", string.Empty, StringComparison.Ordinal);

    internal static string ValidateNextLink(string cursor, bool inboxOnly)
    {
        var expectedPath = inboxOnly ? "/v1.0/me/mailFolders/inbox/messages" : "/v1.0/me/messages";
        if (cursor.Length > 8_192 ||
            !Uri.TryCreate(cursor, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(uri.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProviderReadException("The Microsoft mail continuation is invalid.");
        }

        return uri.AbsoluteUri;
    }

    private Task<JsonDocument> GetJsonAsync(
        Account account,
        string url,
        string accessToken,
        bool preferText,
        string endpoint,
        CancellationToken cancellationToken)
        => _requests.GetJsonAsync(account, url, accessToken, _logger, endpoint, MaximumJsonBytes, preferText, cancellationToken);

    private static string ReadSender(JsonElement message)
    {
        if (!message.TryGetProperty("from", out var from) ||
            !from.TryGetProperty("emailAddress", out var emailAddress))
        {
            return string.Empty;
        }

        return FormatAddress(emailAddress);
    }

    private static IReadOnlyList<string> ReadRecipients(JsonElement message, string propertyName)
    {
        if (!message.TryGetProperty(propertyName, out var recipients) || recipients.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return recipients.EnumerateArray()
            .Select(recipient => recipient.TryGetProperty("emailAddress", out var address) ? FormatAddress(address) : string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(20)
            .ToArray();
    }

    private static string FormatAddress(JsonElement emailAddress)
    {
        var name = GetOptionalString(emailAddress, "name");
        var address = GetOptionalString(emailAddress, "address");
        if (string.IsNullOrWhiteSpace(name))
        {
            return address ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(address) ? name : $"{name} <{address}>";
    }

    private static DateTimeOffset ReadDate(JsonElement message)
    {
        var value = GetOptionalString(message, "receivedDateTime");
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var result)
            ? result
            : DateTimeOffset.UnixEpoch;
    }

    private static string? GetOptionalString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool? GetOptionalBoolean(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var property) &&
        (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
            ? property.GetBoolean()
            : null;

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

    private static void ValidateMailAccount(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!string.Equals(account.Provider, "microsoft", StringComparison.Ordinal) || !account.MailReadEnabled)
        {
            throw new ArgumentException("The account has no Microsoft mail read access.", nameof(account));
        }
    }

    private static void ValidateMessageId(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (messageId.Length > 1_024 || messageId.Any(char.IsControl))
        {
            throw new ArgumentException("The Microsoft message identifier is invalid.", nameof(messageId));
        }
    }
}
