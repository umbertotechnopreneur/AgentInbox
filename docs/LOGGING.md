# Diagnostics

MailMeUp uses Serilog in the executables and `ILogger<T>` in the application and provider readers. UI, CLI and MCP share the same diagnostic format.

The CLI writes diagnostics to stderr and the Windows setup app writes them to a shared local file. Both use the same bounded application events, so a UI check and a later Codex/MCP call can be compared without exposing provider data. MCP stdout remains reserved for JSON-RPC; no banner, progress animation or log line is written there.

The file is `%LOCALAPPDATA%\MailMeUp\logs\mailmeup-YYYYMMDD.log` by default, or under `logs` below `MAILMEUP_DATA_DIR`. Files roll daily and at 10 MiB, retaining the newest 14 files (not necessarily 14 days). Size-rolled files have a numeric suffix. The file sink records debug and above; console verbosity is controlled separately. Shutdown disposes the logger and flushes events.

## Levels

The default is `warning`. Set `MAILMEUP_LOG_LEVEL` or pass `--log-level`; the command option takes precedence. Accepted levels are `verbose`, `debug`, `information`, `warning`, `error` and `fatal`.

```powershell
mailmeup accounts list --json --log-level debug
mailmeup --stdio --log-level information
mailmeup setup status --no-color --no-animation
```

- Debug: operation starts, successful HTTP endpoint labels/status/timing and the presence of mail filters, never their values.
- Information: completed operations with elapsed milliseconds, cancellation, MCP process lifecycle and capability-check totals for mail/calendar.
- Warning: partial coverage and per-account categories; HTTP status, fixed endpoint/read phase, allowlisted provider codes, a GUID-shaped Microsoft request ID when present and exception type. Arbitrary error codes are suppressed.

CLI error text is user feedback on stderr and remains visible at any diagnostic log level. Exit status still reports failure. `--no-color`, a nonempty `NO_COLOR`, `TERM=dumb`, redirected stderr and MCP mode disable diagnostic colors.

## Correlation and read checks

Format `diag=2` includes `pid`, process `instance`, application `op`, per-provider-call `read`, provider name and a stable pseudonymous `account` key. Concurrent calls use independent scopes. The account key is the first 24 hexadecimal characters of SHA-256 over the local account ID, never its address. Desktop check logs pair it with the one-based account row at check time. Treat these correlatable keys as private diagnostics when sharing a log.

Match a partial-coverage warning's operation and account key to earlier provider failures. Endpoint labels distinguish message listing, Gmail metadata, message detail, calendar discovery, event listing and detail. Phases distinguish token acquisition, HTTP transport/status, response size limits and JSON parsing. Error responses are inspected only up to 16 KiB with a two-second sub-deadline; malformed, oversized or unreadable bodies do not hide the original HTTP status.

**Check connections** calls the actual provider readers. It checks general mail search, unread/date search, fixed-keyword/date search and one available message's details. Calendar discovery is followed by event searches and available event details across the discovered calendars, without a five-calendar cutoff. Mail and Calendar have independent 25-second budgets inside the application's 30-second account budget. Mail and event sampling follow at most three pages per search, including empty pages with a continuation. Repeated/exhausted continuations and timeouts remain incomplete or failed, never success. Calendar logs include discovered/checked counts, detail samples and whether all calendars were checked. Provider contents are discarded; no provider writes occur.

Gmail message listing explicitly accepts the HTTP 204 responses observed in local diagnostics as empty list pages and records that normalization at Information level. This opt-in is restricted to `gmail.messages.list`; message metadata/details and other required JSON reads reject HTTP 204 without fabricating content. Empty or malformed HTTP 200 payloads and HTTP error statuses still fail. The list projection also includes `resultSizeEstimate`, alongside message IDs and the continuation token, as described in the [Gmail list contract](https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.messages/list). This estimated count is not used to discard messages or decide pagination completeness.

The log distinguishes failures, searches without a detail sample and verified sample reads. The owner requested a minimal UI: account rows show only a red **Try to reconnect** action for read failures, with no diagnostic status text or check-result banners. Empty samples and bounded/incomplete checks are not painted as broken connections; their limitations remain in the log. No blanket green success is shown. Previous results are cleared before another attempt, including one that later fails or is cancelled. Reconnecting is an optional recovery attempt, not a claim that every read failure is an authentication problem. A passing sample does not certify every message, date range, arbitrary query or future request.

## Privacy boundary

Only `MailMeUp.*` source categories are admitted to the Serilog sink. External SDK, host and transport logs are excluded at every level because they may include request arguments, response content or provider exception messages. The application decorator supplies bounded diagnostics in their place.

Do not log raw account IDs, addresses, user paths, command arguments, search values, local/provider item references, message or event content, authorization headers, credentials, or exception objects/messages. Only the explicitly bounded fields and pseudonymous account key above are permitted. Never log arbitrary response headers, error strings, URLs, JSON paths or SDK errors. The category filter does not sanitize arbitrary future messages.

Account information explicitly requested by the owner appears in CLI results or MCP responses, separately from diagnostic logging. Terminal metadata is escaped as text, including control and bidirectional-formatting characters.

For comparison, run **Check connections** in the Windows app, then repeat the failing Codex operation. Compare the same account key across separate operation IDs. Absence of a Codex operation means it did not reach this logging/data context, not that the mailbox failed. `Unknown` does not prove that reconnection is required. Existing MCP processes must restart after an update to emit the new diagnostics.

## Google request limits — installed 0.1.1.19/0.1.1.20 behavior

The source-only follow-up replaces the process-local queue below with shared per-service profile guardrails and adds Microsoft throttling recovery. It has not been compiled or run. See [read guardrails](READ_GUARDRAILS.md).

The Google readers classify HTTP 403 using the bounded allowlisted provider reason. A recognized request/quota limit or HTTP 429 produces `RateLimited` and safe `rate_limited` advice; a genuine permission error remains `AccessDenied`. This follows the [Gmail error contract](https://developers.google.com/workspace/gmail/api/guides/handle-errors). The Windows account check offers wait/narrow-search guidance for rate-limit-only failures instead of reconnecting.

Mail and calendar HTTP reads share a per-account queue within each process. Requests are serialized, with minimum spacing of 200 ms after metadata/list request starts and one second after detail request starts. Temporary throttling and selected transient HTTP failures have at most three retries, with increasing 1/2/4-second waits plus jitter. A valid longer `Retry-After` takes precedence. Waits, queueing and response reads remain inside a 30-second request deadline and the caller's existing deadline; cancellation is preserved. A cooldown also slows later queued reads. Persistent daily/quota failures are reported without immediate retries.

Retry logs include only the fixed endpoint, attempt, safe category and delay. This limiter does not coordinate separate CLI/MCP processes or other API clients, and cannot guarantee that Google never throttles a read. These changes are built into the locally installed `0.1.1.19` preview. Synthetic regressions passed in the 229-test .NET suite; live-provider behavior remains untested.

## Implementation references

- [Serilog host integration](https://github.com/serilog/serilog-extensions-hosting)
- [Serilog console sink](https://github.com/serilog/serilog-sinks-console)
- [Serilog file sink](https://github.com/serilog/serilog-sinks-file)
- [Spectre.Console documentation](https://spectreconsole.net/console/)

Build and credential-path status are tracked separately in [validation](VALIDATION.md).
