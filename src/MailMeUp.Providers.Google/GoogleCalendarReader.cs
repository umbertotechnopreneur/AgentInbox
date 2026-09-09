using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MailMeUp.Core;
using MailMeUp.Diagnostics;
using MailMeUp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailMeUp.Providers.Google;

/// <summary>Reads Google calendars and appointments without changing calendar data or attendance.</summary>
public sealed class GoogleCalendarReader : ICalendarReader
{
    private const int MaximumJsonBytes = 12 * 1024 * 1024;
    private static readonly HttpClient HttpClient = new();
    private readonly ILogger<GoogleCalendarReader> _logger;
    private readonly GoogleAccessTokenProvider _tokens;

    /// <summary>Creates a Google Calendar reader backed by protected account tokens.</summary>
    public GoogleCalendarReader(IProviderConfigurationStore configurations, ISecretStore secrets, ILogger<GoogleCalendarReader>? logger = null)
    {
        _logger = logger ?? NullLogger<GoogleCalendarReader>.Instance;
        _tokens = new GoogleAccessTokenProvider(configurations, secrets, _logger);
    }

    /// <inheritdoc />
    public string ProviderId => "google";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderCalendar>> ListCalendarsAsync(
        Account account,
        CancellationToken cancellationToken = default)
    {
        ValidateCalendarAccount(account);
        using var diagnostics = ReadDiagnostics.Begin(_logger, account, "list_calendars");
        try
        {
            var accessToken = await _tokens.GetAsync(account, cancellationToken);
            var calendars = new List<ProviderCalendar>();
            var seenCursors = new HashSet<string>(StringComparer.Ordinal);
            var pagesRead = 0;
            string? cursor = null;
            do
            {
                if (++pagesRead > 20)
                {
                    throw new ProviderReadException("Google calendar discovery exceeded its page limit.");
                }

                var url = "https://www.googleapis.com/calendar/v3/users/me/calendarList" +
                          "?maxResults=250&fields=items(id%2Csummary%2Cprimary%2CtimeZone%2CaccessRole)%2CnextPageToken";
                if (!string.IsNullOrWhiteSpace(cursor))
                {
                    url += "&pageToken=" + Uri.EscapeDataString(cursor);
                }

                using var document = await GetJsonAsync(url, accessToken, "google.calendars.list", cancellationToken);
                if (document.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var id = GetOptionalString(item, "id");
                        var role = GetOptionalString(item, "accessRole");
                        if (string.IsNullOrWhiteSpace(id) ||
                            string.Equals(role, "freeBusyReader", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(role, "none", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        calendars.Add(new ProviderCalendar(
                            id,
                            GetOptionalString(item, "summary") ?? id,
                            GetOptionalBoolean(item, "primary"),
                            GetOptionalString(item, "timeZone")));
                        if (calendars.Count > 500)
                        {
                            throw new ProviderReadException("Google calendar discovery exceeded its calendar limit.");
                        }
                    }
                }

                cursor = GetOptionalString(document.RootElement, "nextPageToken");
                if (!string.IsNullOrWhiteSpace(cursor) &&
                    (calendars.Count >= 500 || !seenCursors.Add(cursor)))
                {
                    throw new ProviderReadException("Google calendar discovery could not complete within its limits.");
                }
            }
            while (!string.IsNullOrWhiteSpace(cursor));

            return calendars;
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
            throw new ProviderReadException("Google calendars could not be listed.");
        }
    }

    /// <inheritdoc />
    public async Task<ProviderEventSearchPage> SearchEventsAsync(
        Account account,
        string providerCalendarId,
        DateTimeOffset start,
        DateTimeOffset end,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        ValidateCalendarAccount(account);
        using var diagnostics = ReadDiagnostics.Begin(_logger, account, "search_events");
        ValidateProviderId(providerCalendarId, "calendar");
        if (limit is < 1 or > 50 || end <= start || cursor is { Length: > 4_096 })
        {
            throw new ArgumentException("The Google event search page is invalid.");
        }

        try
        {
            var accessToken = await _tokens.GetAsync(account, cancellationToken);
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(providerCalendarId)}/events" +
                      "?singleEvents=true&orderBy=startTime&showDeleted=true" +
                      "&timeMin=" + Uri.EscapeDataString(start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)) +
                      "&timeMax=" + Uri.EscapeDataString(end.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)) +
                      "&maxResults=" + limit.ToString(CultureInfo.InvariantCulture) +
                      "&fields=items(id%2Cstatus%2Csummary%2Clocation%2Cstart%2Cend)%2CnextPageToken%2CtimeZone";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                url += "&pageToken=" + Uri.EscapeDataString(cursor);
            }

            using var document = await GetJsonAsync(url, accessToken, "google.events.list", cancellationToken);
            var events = new List<ProviderEventSummary>();
            if (document.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var parsed = ParseEventSummary(item, GetOptionalString(document.RootElement, "timeZone"));
                    if (parsed is not null)
                    {
                        events.Add(parsed);
                    }
                }
            }

            return new ProviderEventSearchPage(
                events,
                GetOptionalString(document.RootElement, "nextPageToken"));
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
            throw new ProviderReadException("Google appointments could not be searched.");
        }
    }

    /// <inheritdoc />
    public async Task<ProviderEvent> ReadEventAsync(
        Account account,
        string providerCalendarId,
        string providerEventId,
        CancellationToken cancellationToken = default)
    {
        ValidateCalendarAccount(account);
        using var diagnostics = ReadDiagnostics.Begin(_logger, account, "read_event");
        ValidateProviderId(providerCalendarId, "calendar");
        ValidateProviderId(providerEventId, "event");
        try
        {
            var accessToken = await _tokens.GetAsync(account, cancellationToken);
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(providerCalendarId)}" +
                      $"/events/{Uri.EscapeDataString(providerEventId)}" +
                      "?fields=id%2Cstatus%2Csummary%2Cdescription%2Clocation%2Cstart%2Cend%2Cattendees(displayName%2Cemail%2CresponseStatus)%2ChangoutLink%2CconferenceData(entryPoints)";
            using var document = await GetJsonAsync(url, accessToken, "google.events.get", cancellationToken);
            var root = document.RootElement;
            var boundaries = ParseBoundaries(root)
                ?? throw new ProviderReadException("Google returned an appointment without a valid time.");
            return new ProviderEvent(
                providerEventId,
                GetOptionalString(root, "summary") ?? "(untitled appointment)",
                boundaries.Start,
                boundaries.End,
                boundaries.AllDay,
                string.Equals(GetOptionalString(root, "status"), "cancelled", StringComparison.OrdinalIgnoreCase),
                GetOptionalString(root, "location") ?? string.Empty,
                HtmlToText(GetOptionalString(root, "description") ?? string.Empty),
                ReadAttendees(root),
                ReadMeetingLink(root));
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
            throw new ProviderReadException("The Google appointment could not be read.");
        }
    }

    private static ProviderEventSummary? ParseEventSummary(JsonElement item, string? calendarTimeZone)
    {
        var id = GetOptionalString(item, "id");
        var boundaries = ParseBoundaries(item, calendarTimeZone);
        if (string.IsNullOrWhiteSpace(id) || boundaries is null)
        {
            // Deleted occurrences may contain only their ID and original start time.
            if (string.Equals(GetOptionalString(item, "status"), "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            throw new ProviderReadException("Google returned an appointment without a valid identity or time.");
        }

        return new ProviderEventSummary(
            id,
            boundaries.SortStart,
            GetOptionalString(item, "summary") ?? "(untitled appointment)",
            boundaries.Start,
            boundaries.End,
            boundaries.AllDay,
            string.Equals(GetOptionalString(item, "status"), "cancelled", StringComparison.OrdinalIgnoreCase),
            GetOptionalString(item, "location") ?? string.Empty);
    }

    private static EventBoundaries? ParseBoundaries(JsonElement item, string? calendarTimeZone = null)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("start", out var startElement) ||
            !item.TryGetProperty("end", out var endElement))
        {
            return null;
        }

        if (TryParseEventTime(startElement, out var start) &&
            TryParseEventTime(endElement, out var end) && end >= start)
        {
            return new EventBoundaries(
                start,
                start.ToString("O", CultureInfo.InvariantCulture),
                end.ToString("O", CultureInfo.InvariantCulture),
                AllDay: false);
        }

        var startDate = GetOptionalString(startElement, "date");
        var endDate = GetOptionalString(endElement, "date");
        if (DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var allDayStart) &&
            DateOnly.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var allDayEnd) &&
            allDayEnd > allDayStart)
        {
            var midnight = allDayStart.ToDateTime(TimeOnly.MinValue);
            var sortStart = string.IsNullOrWhiteSpace(calendarTimeZone)
                ? new DateTimeOffset(midnight, TimeSpan.Zero)
                : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
                    midnight, TimeZoneInfo.FindSystemTimeZoneById(calendarTimeZone)));
            return new EventBoundaries(
                sortStart,
                startDate!,
                endDate!,
                AllDay: true);
        }

        return null;
    }

    private static bool TryParseEventTime(JsonElement element, out DateTimeOffset result)
    {
        result = default;
        var value = GetOptionalString(element, "dateTime");
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var timeSeparator = value.IndexOf('T');
        if (value.EndsWith('Z') || timeSeparator >= 0 && value.AsSpan(timeSeparator).IndexOfAny('+', '-') >= 0)
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);
        }

        var timeZoneId = GetOptionalString(element, "timeZone");
        if (string.IsNullOrWhiteSpace(timeZoneId) ||
            !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return false;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (timeZone.IsInvalidTime(local) || timeZone.IsAmbiguousTime(local))
        {
            return false;
        }

        result = new DateTimeOffset(local, timeZone.GetUtcOffset(local));
        return true;
    }

    private static IReadOnlyList<string> ReadAttendees(JsonElement item)
    {
        if (!item.TryGetProperty("attendees", out var attendees) || attendees.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return attendees.EnumerateArray().Select(attendee =>
        {
            var name = GetOptionalString(attendee, "displayName");
            var email = GetOptionalString(attendee, "email");
            var response = GetOptionalString(attendee, "responseStatus");
            var identity = string.IsNullOrWhiteSpace(name) ? email : string.IsNullOrWhiteSpace(email) ? name : $"{name} <{email}>";
            return string.IsNullOrWhiteSpace(identity)
                ? string.Empty
                : string.IsNullOrWhiteSpace(response) ? identity : $"{identity} [{response}]";
        }).Where(value => !string.IsNullOrWhiteSpace(value)).Take(20).ToArray();
    }

    private static string? ReadMeetingLink(JsonElement item)
    {
        var direct = GetOptionalString(item, "hangoutLink");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (!item.TryGetProperty("conferenceData", out var conference) ||
            conference.ValueKind != JsonValueKind.Object ||
            !conference.TryGetProperty("entryPoints", out var entryPoints) ||
            entryPoints.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in entryPoints.EnumerateArray())
        {
            if (string.Equals(GetOptionalString(entry, "entryPointType"), "video", StringComparison.OrdinalIgnoreCase))
            {
                return GetOptionalString(entry, "uri");
            }
        }

        return null;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, string accessToken, string endpoint, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await ProviderHttpDiagnostics.ReadJsonAsync(
            HttpClient, request, _logger, endpoint, MaximumJsonBytes, ClassifyHttpFailure, cancellationToken);
    }

    private static string? GetOptionalString(JsonElement parent, string propertyName) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool GetOptionalBoolean(JsonElement parent, string propertyName) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        property.GetBoolean();

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

    private static void ValidateCalendarAccount(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!string.Equals(account.Provider, "google", StringComparison.Ordinal) || !account.CalendarReadEnabled)
        {
            throw new ArgumentException("The account has no Google Calendar read access.", nameof(account));
        }
    }

    private static void ValidateProviderId(string value, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 1_024 || value.Any(char.IsControl))
        {
            throw new ArgumentException($"The Google {kind} identifier is invalid.", nameof(value));
        }
    }

    private sealed record EventBoundaries(DateTimeOffset SortStart, string Start, string End, bool AllDay);
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
