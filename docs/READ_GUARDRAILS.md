# Mail read limits

The Python implementation bounds requests and reports incomplete coverage. These paths remain unvalidated.

| Limit | Source contract |
| --- | --- |
| Default / maximum date window | 14 / 31 days |
| Default / maximum page size | 10 / 20 messages |
| Message candidates | At most 300 recent candidates |
| Headers per page | At most 50 |
| Search work budget | 20 seconds |
| Message reference lifetime | 10 minutes, in memory |
| Raw message size | 10 MiB |
| Returned message text | 30,000 characters |

Search one account and folder at a time. A search covers only that scope. Respect `candidate_limit_reached`, `coverage_complete` and continuation state instead of claiming an exhaustive mailbox search.

Use compact previews before detail reads. IMAP selects mailboxes read-only and retrieves messages without marking them read.

POP3 exposes only INBOX and has no unread flags or server folders. Its query searches Subject, From and To headers, and its date filters use the email Date header, which can differ from server-received time. Returned `date_basis` and `search_scope` make that distinction explicit. Missing, malformed or timezoneless dates are skipped; `undated_messages_skipped` accumulates across continuation pages and makes `date_filter_incomplete` true and `coverage_complete` false.

POP3 requires UIDL and TOP 0, with a 1 MiB UIDL response limit and a 16 KiB header limit. There is no full-body fallback for header searches. AgentInbox sends no delete commands; provider retention settings can still affect the mailbox.

Authentication, TLS, configuration and provider failures remain errors. Do not convert failures into empty inboxes, silently widen queries or loop through retries.

See [MCP tools](MCP_CONTRACT.md) and [diagnostics](LOGGING.md).
