# CLI reference

The CLI uses a small MailMeUp banner with links to [GitHub](https://github.com/umbertotechnopreneur/MailMeUp) and [Umberto Giacobbi](https://umbertogiacobbi.biz), emoji section dividers, readable fields and next-step commands. There are no cards or panels. Terminals without Unicode support use text alternatives.

| Command | Behavior |
| --- | --- |
| `mailmeup` / `mailmeup --help` | Help and command examples |
| `mailmeup --version` | Version string only |
| `mailmeup status` | Actual implementation capabilities, separate from provider setup |
| `mailmeup accounts list` | Connected accounts and read access; an empty-state hint on a new installation |
| `mailmeup accounts connect <google\|microsoft>` | Browser sign-in for read-only mail and calendars |
| `mailmeup accounts connect <provider> --mail-only` | Connect with mail read access only |
| `mailmeup accounts connect <provider> --calendar-only` | Connect with calendar read access only |
| `mailmeup accounts remove <account-id>` | Remove local metadata and cached credentials |
| `mailmeup setup status` | Provider registration readiness and the next setup or sign-in command |
| `mailmeup setup google <client-json>` | Import a Google Desktop app client file into protected storage |
| `mailmeup setup microsoft <client-id>` | Save a Microsoft desktop app client ID |
| `mailmeup --stdio` | MCP server; stdout contains protocol messages only |

## Output and options

In a terminal, commands show human-readable results. Data commands return the existing snake_case JSON shape when stdout is redirected or `--json` is supplied. Help stays text and version stays a single version string. There are no banners or animations in JSON or MCP output.

| Option | Behavior |
| --- | --- |
| `--json` | Force JSON for status, accounts and setup commands |
| `--no-color` | Disable colors and ANSI escape sequences; also respects a nonempty `NO_COLOR` or `TERM=dumb` |
| `--no-animation` | Disable the activity spinner while keeping readable output |
| `--log-level <level>` | Override `MAILMEUP_LOG_LEVEL`; default `warning` |
| `--` | Treat the following tokens as literal values, including paths starting with a hyphen |

Options can appear before or after the command. The two access options apply only to `accounts connect` and cannot be combined. Both read categories are requested when neither option is supplied. No prompts are added to scripted commands.

```powershell
mailmeup setup status
mailmeup accounts list --json
mailmeup --no-color --no-animation status
mailmeup accounts list --log-level debug > accounts.json
```

Logs and errors go to stderr, separately from results. Levels: `verbose`, `debug`, `information`, `warning`, `error`, `fatal`. Logs record operation names, timing, failure types and partial-coverage counts, without account or message data. See [diagnostics](LOGGING.md).

Exit codes: `0` success, `1` operation/startup failure, `2` invalid command/options, `130` cancelled CLI operation. Ctrl+C cancels sign-in and other pending CLI work. An orderly MCP shutdown returns success.

## Local setup

`MAILMEUP_DATA_DIR` selects an absolute data directory. By default the program uses .NET's per-user `LocalApplicationData` directory plus `MailMeUp`, independent of the working directory. Discovery does not create a database. The executable creates `logs\mailmeup-YYYYMMDD.log` below this directory for bounded diagnostics; see [diagnostics](LOGGING.md).

Provider setup does not sign in an account. Complete it before `accounts connect`. Use `accounts list` to obtain the local ID for `accounts remove`. Removal does not revoke access at the provider.

The Google source file is not deleted automatically. Keep it private and remove it when no longer needed.

The CLI and recovery changes passed local Windows checks. Real revocation, reconnect and clean installation remain. See [account recovery](RECOVERY.md) and [validation](VALIDATION.md).
