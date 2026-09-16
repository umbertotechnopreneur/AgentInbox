namespace MailMeUp.Core;

/// <summary>Requests a compact mail search across selected accounts; InboxOnly excludes archives and other folders.</summary>
public sealed record MailSearchRequest(
    string? Query = null,
    IReadOnlyList<string>? AccountIds = null,
    int Limit = 20,
    string? Cursor = null,
    string? Sender = null,
    string? Start = null,
    string? End = null,
    string? RecipientContains = null,
    bool UnreadOnly = false,
    bool? HasAttachments = null,
    bool InboxOnly = false);

/// <summary>Provider-neutral mail criteria; InboxOnly requires provider filtering before retrieving message summaries.</summary>
public sealed record ProviderMailQuery(
    string Text,
    string? Sender,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string? RecipientContains = null,
    bool UnreadOnly = false,
    bool? HasAttachments = null,
    bool InboxOnly = false);

/// <summary>One short mail match suitable for an MCP response.</summary>
public sealed record MailSearchItem(
    string Reference,
    string AccountId,
    string Subject,
    string Sender,
    DateTimeOffset ReceivedAt,
    string Preview,
    bool IsRead = true,
    bool HasAttachments = false);

/// <summary>Identifies an account that could not be covered by a multi-account read.</summary>
public sealed record AccountReadFailure(string AccountId, string Reason, ReadFailureKind Kind = ReadFailureKind.Unknown);

/// <summary>Returns compact matches, coverage, a continuation and InboxOnly to report the applied folder scope.</summary>
public sealed record MailSearchResult(
    IReadOnlyList<MailSearchItem> Items,
    IReadOnlyList<string> SearchedAccountIds,
    IReadOnlyList<AccountReadFailure> FailedAccounts,
    bool CoverageComplete,
    string? NextCursor,
    DateTimeOffset? EffectiveStart = null,
    DateTimeOffset? EffectiveEnd = null,
    int? DefaultLookbackDaysApplied = null,
    bool InboxOnly = false);

/// <summary>Requests a bounded text segment for one result reference.</summary>
public sealed record MailReadRequest(string Reference, int Offset = 0, int MaxCharacters = 2_000);

/// <summary>Returns selected message details with bounded plain text.</summary>
public sealed record MailMessageResult(
    string Reference,
    string AccountId,
    string Subject,
    string Sender,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    DateTimeOffset ReceivedAt,
    string Text,
    int Offset,
    bool MoreAvailable,
    bool IsRead = true,
    bool HasAttachments = false);

/// <summary>Provider-owned compact search item before a local short reference is assigned.</summary>
public sealed record ProviderMailSummary(
    string ProviderMessageId,
    string Subject,
    string Sender,
    DateTimeOffset ReceivedAt,
    string Preview,
    bool IsRead = true,
    bool HasAttachments = false,
    IReadOnlyList<string>? Recipients = null);

/// <summary>One provider search page with its opaque provider continuation.</summary>
public sealed record ProviderMailSearchPage(IReadOnlyList<ProviderMailSummary> Items, string? NextCursor);

/// <summary>Provider-owned message content before application-level bounding.</summary>
public sealed record ProviderMailMessage(
    string ProviderMessageId,
    string Subject,
    string Sender,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    DateTimeOffset ReceivedAt,
    string PlainText,
    bool IsRead = true,
    bool HasAttachments = false);

/// <summary>Reads mail from one provider without exposing access tokens to the application layer.</summary>
public interface IMailReader
{
    /// <summary>Gets the stable provider identifier.</summary>
    string ProviderId { get; }

    /// <summary>Searches one account and returns a provider page ordered newest first.</summary>
    Task<ProviderMailSearchPage> SearchAsync(
        Account account,
        ProviderMailQuery query,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one selected provider message without modifying mailbox state.</summary>
    Task<ProviderMailMessage> ReadAsync(
        Account account,
        string providerMessageId,
        CancellationToken cancellationToken = default);
}

/// <summary>Indicates that a provider read failed without exposing response bodies or credentials.</summary>
public sealed class ProviderReadException : Exception
{
    /// <summary>Creates a sanitized provider read failure.</summary>
    public ProviderReadException(string message, ReadFailureKind kind = ReadFailureKind.Unknown)
        : base(message)
    {
        Kind = kind;
    }

    /// <summary>Gets a safe category for user guidance; exception messages are never forwarded to the assistant.</summary>
    public ReadFailureKind Kind { get; }
}
