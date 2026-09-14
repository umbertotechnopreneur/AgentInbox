# Read guardrails

**September 12–13 source increments: not built, tested or installed.** Installed MSIX `0.1.1.20` predates these controls and the budget editor. See [validation](VALIDATION.md).

MailMeUp bounds provider work and the content returned through MCP. These local limits do not measure the provider's remaining quota or the AI model's tokens.

## Default limits

| Control | Default |
| --- | --- |
| Concurrent content HTTP requests per account/service | 1, across processes using the same data directory |
| HTTP attempts per account/service | 60 per rolling minute, including retries |
| HTTP attempts across the local profile | 180 per rolling minute, including retries |
| Content read admissions | 120 per rolling 15 minutes |
| Mail/event detail admissions | 20 within the same 15 minutes, included in the 120 |
| One MCP read response | 64 KiB of serialized JSON |
| Cumulative MCP read output | 256 KiB per rolling 15 minutes |
| Default detail text | 2,000 characters; explicit requests can ask for up to 16,000 |
| Minimum interval before metadata/detail attempts | 200 ms / 1 second |

Gmail and Google Calendar have separate account/service queues and cooldowns. Microsoft mail, folder discovery and calendars share the `microsoft-outlook` queue for the account. The exclusive lease covers the HTTP response read. Every retry reacquires the lease and consumes another attempt. Other clients or profiles do not share these controls.

HTTP 429 and recognized Gmail 403 quota reasons produce `rate_limited`. Selected transient failures receive at most three retries with exponential waits and jitter inside a 30-second request deadline. A longer `Retry-After` takes precedence and persists before releasing the lease, including when no retry fits the deadline. Persistent Gmail quota failures stop without an immediate retry. Other processes observe the same cooldown.

Content admission happens before application searches, calendar discovery or detail reads, even if they later fail. Cache hits and additional body offsets still consume a detail admission because they return additional content. Setup read checks use provider attempt controls but do not return mailbox content through MCP. OAuth sign-in and SDK token refresh are outside the new content HTTP governor and retain existing credential-session coordination.

## Output accounting

The MCP adapter measures a compact UTF-8 JSON result envelope containing both `structuredContent` and the compatibility `content[].text` copy, including JSON escaping and result fields. This is a conservative local serialization measurement, not a wire-traffic metric: JSON-RPC framing and client-added context are excluded. It makes no assumption about whether an assistant feeds one or both representations into a model.

A response exceeding the single-response or remaining cumulative limit is replaced by a fixed `read_budget_exceeded` notification; oversized content is not delivered. Status and empty results do not consume cumulative output. Local status is also exempt from the single-response limit so the owner can inspect controls after exhaustion or with a very small content cap. Fixed recovery notifications remain available after exhaustion. Provider work may already have happened before output size is known, and concurrent admitted calls can finish provider work before their output is rejected.

The caller should stop bulk reads, disclose incomplete coverage and wait for the rolling window or narrow the request. A smaller response may fit the remaining output budget; reconnecting cannot reset a local budget. Local provider-attempt exhaustion can return partial mail results and a resumable cursor preserving buffered matches and the failed page position. Retry after the window resets. Other provider failures retain fresh-search recovery. Coverage never means that all pages or bodies were examined.

These limits bound MailMeUp output. They cannot cap tokens from conversation history, other tools, client prompts or repeated client-side ingestion. Model-specific token accounting requires a separate client integration.

## Avoiding unnecessary reads

Mail search initially requests at most ten previews per account, or the smaller requested result count. It replenishes only an exhausted source while assembling the global page, preserving the initial provider page size in continuations. All refills in one search share a 30-second provider-work deadline and a maximum of twenty pages per account. Gmail still requires one list request plus a request for each preview; the change reduces eager hydration, not that API cost.

Message and event details have separate in-memory caches lasting up to two minutes. Each retains at most 64 entries and approximately 4 MiB of text, with a 256 KiB text limit per entry and sixteen pending loads. Concurrent reads of the same item share a load; failures are not cached. Keys include account, item and sharing snapshot; sharing is checked before and after every response. Cache data is never written to disk. Cached details may be up to two minutes old.

Microsoft exclusion-folder IDs have a separate five-minute, 128-entry cache keyed by application and account. Expired or failed refreshes do not serve stale exclusions. Gmail previews request MIME structure through `format=full` with a field projection omitting body data, fixing ordinary attachment filtering. Excessive MIME nesting and separately stored text bodies return incomplete-read errors rather than false empty content. Binary and named text attachments are not decoded as message text.

## Windows controls and usage

The source adds a read-limits and usage section on **Sharing**, alongside the existing mail-search period. It shows aggregate HTTP attempts in the last minute, content and detail admissions in the active read window, and serialized output in KiB. Usage includes MailMeUp processes sharing this profile. Refresh reads only the local ledger; it makes no provider request and consumes no admission or output budget. The timestamp identifies the snapshot rather than implying continuously updated measurements.

The editor has explicit Save, Discard and default-value actions. Loading defaults changes only the draft. Increasing limits allows more traffic and more assistant context. Output values use KiB (1,024 bytes), not estimated model tokens. Validation preserves the relationship between detail and total reads, and between per-response and cumulative output caps. Unsaved limits are protected when navigating away or closing the window.

Saved settings and the running process's active settings are separate. After saving changed limits, restart MailMeUp and reconnect the assistant so all CLI/MCP/desktop processes load the same configuration. Saving does not reset usage counters or cooldowns and does not automatically terminate processes. A save based on stale settings is rejected; refresh and explicitly discard or reapply the draft instead of overwriting another window's changes.

Usage times identify the earliest recorded charge that expires, not a simultaneous reset of every rolling counter or a promise that a read will then succeed. Active provider cooldowns are reported separately. Another process, another budget or a provider response can still delay the next read. Counts and expiry times use this process's active limits; they may differ from another process awaiting restart with different settings.

`get_status` exposes `read_guardrail_usage` and `read_guardrail_settings_pending_restart` alongside `read_guardrails`. Unavailable management is represented as `null`, not invented zero usage. Only aggregate counters and timestamps are returned; no account identities, hashed scopes, file paths or content enter the usage snapshot. Settings writes remain local UI operations and are not exposed as MCP tools.

The isolated UI demo uses illustrative counters and in-memory limit settings. The preview label identifies this data as synthetic; it never reads the production ledger or changes its settings.

## Local configuration file

Optional `read-guardrails.json` belongs directly under the runtime data directory selected by `MAILMEUP_DATA_DIR` or the normal application profile. MCP cannot change it. Complete defaults:

```json
{
  "providerAttemptsPerAccountServicePerMinute": 60,
  "providerAttemptsPerProfilePerMinute": 180,
  "contentReadsPerWindow": 120,
  "detailReadsPerWindow": 20,
  "readWindowSeconds": 900,
  "outputBytesPerWindow": 262144,
  "responseBytes": 65536,
  "metadataIntervalMilliseconds": 200,
  "detailIntervalMilliseconds": 1000,
  "leaseWaitSeconds": 30
}
```

Omitted properties use defaults. Unknown, duplicate, malformed or out-of-range settings fail closed. Restart all CLI/MCP/desktop processes using the profile after a change; mixed running configurations can apply different thresholds to the shared ledger. The Windows editor persists this same file atomically. The existing 14-day search default remains separate and applies to new undated searches without a restart.

Usage state under `read-guardrails/` contains timestamps, byte counts and hashed account/service keys, never message content or tokens. File leases coordinate processes, ledger replacement is atomic, and corrupt state blocks new reads. Restarting a process does not reset the rolling budgets. Constructing services or querying an empty profile creates no usage state. Local filesystem changes can reset a profile, so these are operational controls, not protection against a hostile local user.

## Next iteration

Exercise the new synthetic regressions, native editor interactions and published executable when requested. Remaining work includes OAuth refresh failure backoff, provider/project quota-unit accounting, more explicit Graph search completeness and ordering, and client-specific token metering. Live quota and multi-process behavior still need dedicated validation; these increments make no new live-provider claim.
