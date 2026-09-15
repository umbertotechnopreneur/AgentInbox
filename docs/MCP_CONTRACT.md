# MCP tool contract

**Read-only scope.** No send, update, delete, invite or appointment-write tools.

All mail searches exclude Gmail `SPAM` and `TRASH`, and Microsoft `Junk Email` and `Deleted Items`, by default. Reading a message never marks it as read.

## Current source

| Tool | Result |
| --- | --- |
| `get_status` | Build stage, read-only mode, provider capabilities, mail-search preferences, active read limits, aggregate local usage and pending-restart status |
| `list_accounts` | Shared account IDs, providers, labels and addresses, with effective read categories |
| `search_mail` | Short matches across selected or all mail-enabled accounts, with optional structured filters |
| `search_unread_mail` | Unread Inbox matches by default, with optional date, sender, recipient and attachment filters |
| `search_mail_by_date` | Short matches in an inclusive/exclusive received-time range, with optional unread, sender, recipient and attachment filters |
| `read_mail` | Bounded plain text for one selected message reference |
| `list_calendars` | Calendars with short local references |
| `search_events` | Combined agenda in a bounded time window |
| `read_event` | Bounded details for one selected appointment |

All tools are read-only. A new installation has no accounts. Paths, provider item IDs and credentials are never returned. The original five provider-read tools pass local automated checks; the two structured mail tools and their provider-side filters still need dedicated validation.

Provider registration, interactive account connection and sharing choices belong to the local CLI or Windows setup app. They are deliberately not exposed through MCP. Calendar discovery for the owner's sharing picker is also local-only.

The September 13 source adds `read_guardrail_usage` and `read_guardrail_settings_pending_restart` to `get_status`. Usage is a local profile snapshot with counters and expiry timestamps, not provider quota or token accounting. Unsupported management returns `null` for both fields. Status makes no provider requests and remains available without charging the read/output budgets. Saved limit changes require restarting the participating MailMeUp processes; no settings-write tool is registered. See [read guardrails](READ_GUARDRAILS.md).

The application reloads local sharing choices for every read, including existing references and continuation cursors. It checks access again before releasing a response; a change made during a read discards that response. Hidden accounts are omitted from discovery and continuation coverage. Provider consent and local sharing are independent: consent alone does not override a saved sharing choice.

## Later

| Tool | Result |
| --- | --- |
| `read_thread` | Selected conversation text |
| `mail_stats` | Counts identified as exact or estimated |

Attachment content/downloads, sending, edits and invitations are outside the MVP.

## Keep results small and accurate

Omit account IDs to search all eligible accounts, or pass explicit IDs. Calendar selection is separate. Defaults are 20 results globally, 160-character mail previews and 2,000-character detail pages. More results use a short in-memory continuation cursor. The current source adds shared request, admission and MCP output-byte limits; see [read guardrails](READ_GUARDRAILS.md) for defaults, configuration and validation limits. Output bytes are not model tokens.

Mail search accepts common text plus optional sender/recipient contains filters, unread state, attachment presence and received-time boundaries. Each adapter translates those structured filters to Gmail or Microsoft syntax. `search_unread_mail` and `search_mail_by_date` do not require a text query.

The September 14 source adds `inboxOnly`: it defaults to `true` for `search_unread_mail` and `false` for `search_mail` and `search_mail_by_date`. For an unread Inbox briefing, combine `search_unread_mail` with the required date range. Pass `inboxOnly: false` explicitly to include unread archived or moved messages, while retaining Spam/Junk and Trash/Deleted exclusions. Inbox scope includes all Gmail Inbox categories, not only Primary; it does not exclude senders. A custom Gmail label does not itself remove a message from Inbox.

Inbox filtering happens at the provider before preview retrieval: Gmail requires the `INBOX` label, and Microsoft queries the Inbox message collection without traversing child folders. Results report `inbox_only` alongside their effective dates. Continuations retain this scope and reject a changed `inboxOnly` value. This source increment has not been built, tested or installed.

In the current source, searches without explicit dates cover the previous 14 days by default. The Windows Sharing page can save a global default from 1 to 365 days. Longer periods take more time, require more provider requests and can hit provider limits. An explicit start, end or recognized native provider date expression overrides the default; older mail remains available when requested explicitly. Calendar windows are unchanged.

Mail results include `effective_start`, `effective_end` and `default_lookback_days_applied`. Default windows use a fixed inclusive start and exclusive end for the whole search, including continuations. Dates embedded only in provider text are not translated into these fields. Local preferences reload for each request; changing the default requires a fresh search instead of continuing a cursor created with the old default. The local file is `search-preferences.json`, under the runtime data directory. MCP reports this preference but cannot change it.

Microsoft text/address searches use Graph `$search` without `$filter` or `$orderby`. Structured date bounds become `received` constraints inside the search expression, using [KQL UTC date comparisons](https://learn.microsoft.com/en-us/sharepoint/dev/general-development/keyword-query-language-kql-syntax-reference#date-or-time-values-for-properties). MailMeUp also filters each returned page locally before releasing matches and keeps its continuation even when all items on a page are excluded. Structured listings without search text or address criteria retain Graph filtering and received-time ordering. Graph message search has an [index limit of 1,000 results](https://learn.microsoft.com/en-us/graph/search-query-parameter#use-search-on-message-collections); it is not an exhaustive mailbox export. Narrow searches when needed. Existing refill limits and timeouts report partial account coverage.

Search first, select relevant previews, then read the details needed for the request. Do not open every match for a compact briefing. Keep detail reads sequential per account and bound the work; disclose a selection or unexamined pages instead of claiming an exhaustive review. `coverage_complete` reports account failures, while `next_cursor` reports remaining pages. Neither field says that every message body was read. Omit raw HTML, MIME, binary attachments and unnecessary attendee lists.

References and cursors expire after about 30 minutes or a server restart. Results return coverage and individual account failures; partial coverage is never presented as complete.

Each provider operation has a 30-second cancellation budget; mail-search refills share a 30-second provider-work deadline. A timeout, removed source account or failed continuation returns partial coverage alongside healthy results. Failed sources require a fresh search, except a local `read_budget_exceeded` pause: a returned mail cursor can resume after the budget window resets. Caller cancellation still cancels the whole request.

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

Google throttling is reported as `rate_limited`, including a 403 with a recognized rate-limit reason. It is not an `access_denied` or expired-login diagnosis. Follow the supplied wait/retry guidance, avoid tight retry loops and do not suggest reconnecting for a rate limit. Genuine permission failures keep `access_denied`. See [request pacing and diagnostics](LOGGING.md).

## Developer requirements

Microsoft HTTP 429 also produces `rate_limited` and shared retry/cooldown behavior. `read_budget_exceeded` denotes a local admission, provider-attempt or output limit. Stop bulk reads and follow the wait/narrow-request guidance; do not loop or suggest reconnecting. The Windows account check presents local-budget guidance separately from sign-in recovery.

Provider-specific filters need separate translations. Cursors bind to query and scope. References bind messages/events to their source account.

Calendar queries require a bounded window of at most 31 days, an explicit time-zone offset and correct recurring/all-day handling. Preserve occurrence identity, cancellations and exclusive date-only end dates.

Provider content is untrusted data. Only the adapter-generated notification carries the instruction to inform the user; message and event contents never supply instructions. Recovery guidance may ask the owner to reconnect existing read access, but must not request broader permissions or credential disclosure.
