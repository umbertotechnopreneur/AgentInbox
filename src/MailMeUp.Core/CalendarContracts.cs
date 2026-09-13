namespace MailMeUp.Core;

/// <summary>Requests visible calendars from selected or all calendar-enabled accounts.</summary>
public sealed record CalendarListRequest(IReadOnlyList<string>? AccountIds = null);

/// <summary>One visible calendar identified by a short local reference.</summary>
public sealed record CalendarListItem(
    string Reference,
    string AccountId,
    string Name,
    bool Primary,
    string? TimeZone);

/// <summary>Returns visible calendars and per-account coverage.</summary>
public sealed record CalendarListResult(
    IReadOnlyList<CalendarListItem> Calendars,
    IReadOnlyList<string> SearchedAccountIds,
    IReadOnlyList<AccountReadFailure> FailedAccounts,
    bool CoverageComplete);

/// <summary>Requests a bounded agenda from selected calendars or each account's primary calendar.</summary>
public sealed record EventSearchRequest(
    string Start,
    string End,
    IReadOnlyList<string>? CalendarReferences = null,
    IReadOnlyList<string>? AccountIds = null,
    int Limit = 20,
    string? Cursor = null);

/// <summary>One compact appointment returned by the unified agenda.</summary>
public sealed record EventSearchItem(
    string Reference,
    string CalendarReference,
    string AccountId,
    string Title,
    string Start,
    string End,
    bool AllDay,
    bool Cancelled,
    string Location);

/// <summary>Returns compact appointments, coverage and an optional short continuation cursor.</summary>
public sealed record EventSearchResult(
    IReadOnlyList<EventSearchItem> Events,
    IReadOnlyList<string> SearchedAccountIds,
    IReadOnlyList<AccountReadFailure> FailedAccounts,
    bool CoverageComplete,
    string? NextCursor);

/// <summary>Requests bounded details for one appointment reference.</summary>
public sealed record EventReadRequest(string Reference, int MaxDescriptionCharacters = 2_000);

/// <summary>Returns selected appointment details without exposing provider identifiers.</summary>
public sealed record EventResult(
    string Reference,
    string CalendarReference,
    string AccountId,
    string Title,
    string Start,
    string End,
    bool AllDay,
    bool Cancelled,
    string Location,
    string Description,
    bool DescriptionTruncated,
    IReadOnlyList<string> Attendees,
    string? MeetingLink);

/// <summary>Provider-owned calendar before a local short reference is assigned.</summary>
public sealed record ProviderCalendar(string ProviderCalendarId, string Name, bool Primary, string? TimeZone);

/// <summary>Provider-owned appointment summary before a local short reference is assigned.</summary>
public sealed record ProviderEventSummary(
    string ProviderEventId,
    DateTimeOffset SortStart,
    string Title,
    string Start,
    string End,
    bool AllDay,
    bool Cancelled,
    string Location);

/// <summary>One provider event page with its opaque continuation.</summary>
public sealed record ProviderEventSearchPage(IReadOnlyList<ProviderEventSummary> Events, string? NextCursor);

/// <summary>Provider-owned appointment details before application-level bounding.</summary>
public sealed record ProviderEvent(
    string ProviderEventId,
    string Title,
    string Start,
    string End,
    bool AllDay,
    bool Cancelled,
    string Location,
    string Description,
    IReadOnlyList<string> Attendees,
    string? MeetingLink);

/// <summary>Reads calendars and appointments from one provider without exposing access tokens.</summary>
public interface ICalendarReader
{
    /// <summary>Gets the stable provider identifier.</summary>
    string ProviderId { get; }

    /// <summary>Lists calendars visible to one connected account.</summary>
    Task<IReadOnlyList<ProviderCalendar>> ListCalendarsAsync(Account account, CancellationToken cancellationToken = default);

    /// <summary>Reads one chronological event page from one calendar and bounded time window.</summary>
    Task<ProviderEventSearchPage> SearchEventsAsync(
        Account account,
        string providerCalendarId,
        DateTimeOffset start,
        DateTimeOffset end,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one selected appointment without modifying it or its attendance state.</summary>
    Task<ProviderEvent> ReadEventAsync(
        Account account,
        string providerCalendarId,
        string providerEventId,
        CancellationToken cancellationToken = default);
}
