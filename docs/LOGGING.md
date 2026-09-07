# Diagnostics

MailMeUp uses Serilog in the executable and `ILogger<T>` in the shared application decorator. Both the CLI and MCP use the same operation diagnostics.

The CLI writes diagnostics to stderr and the Windows setup app writes them to a shared local file. Both use the same bounded application events, so a UI check and a later Codex/MCP call can be compared without exposing provider data. MCP stdout remains reserved for JSON-RPC; no banner, progress animation or log line is written there.

The file is `%LOCALAPPDATA%\MailMeUp\logs\mailmeup-YYYYMMDD.log` by default, or under `logs` below the absolute `MAILMEUP_DATA_DIR` directory when that override is set. Files roll daily and the newest 14 files are retained. The file sink records `debug` and above; console verbosity is still controlled by `MAILMEUP_LOG_LEVEL` or `--log-level`. The logger is disposed after shutdown and flushes buffered events.

## Levels

The default is `warning`. Set `MAILMEUP_LOG_LEVEL` or pass `--log-level`; the command option takes precedence. Accepted levels are `verbose`, `debug`, `information`, `warning`, `error` and `fatal`.

```powershell
mailmeup accounts list --json --log-level debug
mailmeup --stdio --log-level information
mailmeup setup status --no-color --no-animation
```

- Debug: operation starts, command lifecycle and bounded startup failure types.
- Information: completed operations with elapsed milliseconds, cancellation, MCP process lifecycle and capability-check totals for mail/calendar.
- Warning: failed operations and partial account coverage, including only counts and fixed failure categories such as `AccessDenied`, `SignInRequired` or `LocalCredentialsUnavailable`.

CLI error text is user feedback on stderr and remains visible at any diagnostic log level. Exit status still reports failure. `--no-color`, a nonempty `NO_COLOR`, `TERM=dumb`, redirected stderr and MCP mode disable diagnostic colors.

## Privacy boundary

Only `MailMeUp.*` source categories are admitted to the Serilog sink. External SDK, host and transport logs are excluded at every level because they may include request arguments, response content or provider exception messages. The application decorator supplies bounded diagnostics in their place.

Do not log accounts, addresses, user paths, command arguments, search criteria, local/provider references, message or event content, authorization headers, credentials, or exception objects/messages. Record fixed operation names, counts, durations and exception type names. Keep this rule when adding new application logs; the category filter does not sanitize arbitrary future messages.

Account information explicitly requested by the owner appears in CLI results or MCP responses, separately from diagnostic logging. Terminal metadata is escaped as text, including control and bidirectional-formatting characters.

For a comparison, run **Check read access** in the Windows app and then repeat the failing operation through Codex. The log records `check_account_connections`, `search_mail`, `read_mail`, `list_calendars`, `search_events` and `read_event` separately, with duration, account counts and partial-coverage categories. A UI success with no subsequent Codex operation in the file indicates that the MCP process did not reach the application; a Codex operation with `AccessDenied`, `SignInRequired`, `LocalCredentialsUnavailable` or another category identifies the failing capability without logging the account identity.

## Implementation references

- [Serilog host integration](https://github.com/serilog/serilog-extensions-hosting)
- [Serilog console sink](https://github.com/serilog/serilog-sinks-console)
- [Serilog file sink](https://github.com/serilog/serilog-sinks-file)
- [Spectre.Console documentation](https://spectreconsole.net/console/)

Build and credential-path status are tracked separately in [validation](VALIDATION.md).
