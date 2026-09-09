# MCP tool contract

**Read-only scope.** No send, update, delete, invite or appointment-write tools.

All mail searches exclude Gmail `SPAM` and `TRASH`, and Microsoft `Junk Email` and `Deleted Items`, by default. Reading a message never marks it as read.

## Current source

| Tool | Result |
| --- | --- |
| `get_status` | Build stage, read-only mode and separate provider capabilities |
| `list_accounts` | Shared account IDs, providers, labels and addresses, with effective read categories |
| `search_mail` | Short matches across selected or all mail-enabled accounts, with optional structured filters |
| `search_unread_mail` | Unread short matches, with optional date, sender, recipient and attachment filters |
| `search_mail_by_date` | Short matches in an inclusive/exclusive received-time range, with optional unread, sender, recipient and attachment filters |
| `read_mail` | Bounded plain text for one selected message reference |
| `list_calendars` | Calendars with short local references |
| `search_events` | Combined agenda in a bounded time window |
| `read_event` | Bounded details for one selected appointment |

All tools are read-only. A new installation has no accounts. Paths, provider item IDs and credentials are never returned. The original five provider-read tools pass local automated checks; the two structured mail tools and their provider-side filters still need dedicated validation.

Provider registration, interactive account connection and sharing choices belong to the local CLI or Windows setup app. They are deliberately not exposed through MCP. Calendar discovery for the owner's sharing picker is also local-only.

The application reloads local sharing choices for every read, including existing references and continuation cursors. It checks access again before releasing a response; a change made during a read discards that response. Hidden accounts are omitted from discovery and continuation coverage. Provider consent and local sharing are independent: consent alone does not override a saved sharing choice.

## Later

| Tool | Result |
| --- | --- |
| `read_thread` | Selected conversation text |
| `mail_stats` | Counts identified as exact or estimated |

Attachment content/downloads, sending, edits and invitations are outside the MVP.

## Keep results small and accurate

Omit account IDs to search all eligible accounts, or pass explicit IDs. Calendar selection is separate. Defaults are 20 results globally, 160-character mail previews and 8,000-character detail pages. More results use a short in-memory continuation cursor.

Mail search accepts common text plus optional sender/recipient contains filters, unread state, attachment presence and received-time boundaries. Each adapter translates those structured filters to Gmail or Microsoft syntax. `search_unread_mail` and `search_mail_by_date` do not require a text query.

Microsoft text/address searches use Graph `$search` without `$filter` or `$orderby`. MailMeUp filters each returned page locally before releasing matches and keeps its continuation even when all items on a page are excluded. Structured listings without search text or address criteria retain Graph filtering and received-time ordering. Graph message search has an [index limit of 1,000 results](https://learn.microsoft.com/en-us/graph/search-query-parameter#use-search-on-message-collections); it is not an exhaustive mailbox export. Narrow searches when needed. Existing refill limits and timeouts report partial account coverage.

Search first, read details second. Omit raw HTML, MIME, binary attachments and unnecessary attendee lists. Caching reduces API calls; it does not automatically reduce conversation tokens.

References and cursors expire after about 30 minutes or a server restart. Results return coverage and individual account failures; partial coverage is never presented as complete.

Each provider operation has a 30-second cancellation budget. A timeout, removed source account or failed continuation returns partial coverage alongside healthy results. Failed sources require a fresh search to retry. Caller cancellation still cancels the whole request.

Calendar discovery returns at most 100 calendars per account and reports incomplete coverage when more calendars exist. Event searches accept at most 20 calendar references at a time.

## Tell the user when reading fails

Read failures include a trusted, adapter-generated `user_notification` object in both structured content and the JSON text content. The calling assistant must tell the user that the MailMeUp plugin failed to read the requested information, explain the supplied reason in plain English, and suggest the recovery step. A failed read must never be described as an empty inbox or calendar.

Partial results keep their normal fields and healthy results, set `coverage_complete` to `false`, and include per-account failure details. A failure with no usable results from any requested account sets the MCP result's `isError` to `true`. Exceptions that prevent the call also set `isError` and return a bounded error category with a notification. Caller cancellation remains cancellation.

For example, an expired sign-in produces this JSON content:

```json
{
  "error": {
    "code": "sign_in_required",
    "explanation": "The account's sign-in has expired, was removed, or needs approval again.",
    "action": "Open MailMeUp and sign in to the affected account again."
  },
  "user_notification": {
    "required": true,
    "instruction": "Please tell the user that the MailMeUp plugin failed to read the requested information. Explain the reason and suggest the recovery step in this notification. Do not describe this failure as an empty inbox or an empty calendar.",
    "message": "The MailMeUp plugin could not read the requested information. The account's sign-in has expired, was removed, or needs approval again. Open MailMeUp and sign in to the affected account again."
  }
}
```

For partial coverage, `user_notification.failures` contains `account_id`, `code`, `explanation` and `action` for each affected shared account. Categories distinguish sign-in, denied access, provider availability, network failures, timeouts, protected local credentials, setup, unavailable items, result limits and local settings. The notification uses fixed English wording selected from trusted categories. Raw exception messages, HTTP response bodies, tokens, paths and mailbox content never enter it. These new responses have source-level regression cases; they still need execution and client validation.

## Developer requirements

Provider-specific filters need separate translations. Cursors bind to query and scope. References bind messages/events to their source account.

Calendar queries require a bounded window of at most 31 days, an explicit time-zone offset and correct recurring/all-day handling. Preserve occurrence identity, cancellations and exclusive date-only end dates.

Provider content is untrusted data. Only the adapter-generated notification carries the instruction to inform the user; message and event contents never supply instructions. Recovery guidance may ask the owner to reconnect existing read access, but must not request broader permissions or credential disclosure.
