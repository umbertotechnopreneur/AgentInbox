# CLI reference

Use the command-line app to set up services, connect accounts and start MailMeUp for your assistant. In a terminal, it shows readable results and suggests what to do next. If your terminal can't show Unicode symbols, it uses plain text.

| Command | What it does |
| --- | --- |
| `mailmeup` / `mailmeup --help` | Help and command examples |
| `mailmeup --version` | Version string only |
| `mailmeup status` | Shows which features are implemented; doesn't check Google or Microsoft setup |
| `mailmeup accounts list` | Lists connected accounts and read permissions, or explains how to add your first account |
| `mailmeup accounts connect <google\|microsoft>` | Browser sign-in for read-only mail and calendars |
| `mailmeup accounts connect <provider> --mail-only` | Connect with mail read access only |
| `mailmeup accounts connect <provider> --calendar-only` | Connect with calendar read access only |
| `mailmeup accounts remove <account-id>` | Removes the local account record and saved sign-in tokens |
| `mailmeup setup status` | Shows whether Google and Microsoft are configured and what to do next |
| `mailmeup setup google <client-json>` | Import a Google Desktop app client file into protected storage |
| `mailmeup setup microsoft <client-id>` | Save a Microsoft desktop app client ID |
| `mailmeup --stdio` | MCP server; stdout contains protocol messages only |

## Output and options

Commands show readable text in a terminal. Redirect a data command's output or add `--json` to get JSON with `snake_case` field names. Help stays text, and the version command returns only the version. JSON and MCP output have no banners or animations.

| Option | What it does |
| --- | --- |
| `--json` | Force JSON for status, accounts and setup commands |
| `--no-color` | Disable colors and ANSI escape sequences; also respects a nonempty `NO_COLOR` or `TERM=dumb` |
| `--no-animation` | Disable the activity spinner while keeping readable output |
| `--log-level <level>` | Override `MAILMEUP_LOG_LEVEL`; default `warning` |
| `--` | Treat the following tokens as literal values, including paths starting with a hyphen |

Options can go before or after the command. Use `--mail-only` or `--calendar-only` with `accounts connect`, but not both together. Leaving both out requests access to mail and calendars. Scripted commands don't add interactive prompts.

```powershell
mailmeup setup status
mailmeup accounts list --json
mailmeup --no-color --no-animation status
mailmeup accounts list --log-level debug > accounts.json
```

Logs and errors go to stderr, separate from command results. Choose from `verbose`, `debug`, `information`, `warning`, `error` or `fatal`. Logs include operation names, timing, error types and counts of accounts that couldn't be read. They leave out message content and account addresses; diagnostic account keys are described in [logging](LOGGING.md).

Exit codes are `0` for success, `1` for an operation or startup failure, `2` for an invalid command or option, and `130` for a cancelled command. Press Ctrl+C to cancel sign-in or other pending work. A normal MCP shutdown returns success.

## Local setup

Set `MAILMEUP_DATA_DIR` to a full path if you want to choose the data folder. Otherwise, MailMeUp uses a `MailMeUp` folder under .NET's per-user `LocalApplicationData` directory, regardless of where you run the command. Listing features or an empty account list doesn't create a database. Log files go under `logs\mailmeup-YYYYMMDD.log` in that data folder; see [logging](LOGGING.md) for size and retention limits.

Set up Google or Microsoft first, then use `accounts connect` to sign in. Use `accounts list` to find the ID needed by `accounts remove`. Removing an account locally leaves its app permission in place at Google or Microsoft.

The Google configuration file isn't deleted after import. Keep it private and remove it when you no longer need it.

CLI and recovery changes have passed local Windows checks. Revoking access and reconnecting real accounts, plus installation on a clean machine, still need testing. See [account recovery](RECOVERY.md) and the [test record](VALIDATION.md).
