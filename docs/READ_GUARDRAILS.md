# Mail read limits

The Python implementation bounds requests and reports incomplete coverage. These paths remain unvalidated.

| Limit | Source contract |
| --- | --- |
| Default / maximum date window | 14 / 31 days |
| Default / maximum page size | 10 / 20 messages |
| UID candidates | At most 300 recent candidates |
| Headers per page | At most 50 |
| Search work budget | 20 seconds |
| Message reference lifetime | 10 minutes, in memory |
| Raw message size | 10 MiB |
| Returned message text | 30,000 characters |

Search one account and folder at a time. A search covers only that scope. Respect `candidate_limit_reached`, `coverage_complete` and continuation state instead of claiming an exhaustive mailbox search.

Use compact previews before detail reads. IMAP selects mailboxes read-only and retrieves messages without marking them read.

Authentication, TLS, configuration and provider failures remain errors. Do not convert failures into empty inboxes, silently widen queries or loop through retries.

See [MCP tools](MCP_CONTRACT.md) and [diagnostics](LOGGING.md).
