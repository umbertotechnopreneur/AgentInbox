# Python command reference

The engine requires Python 3.11+. Use `-B` to avoid bytecode beside source files.

| Command | Purpose |
| --- | --- |
| `python -B python/run.py mcp` | Run MCP over stdio |
| `python -B python/run.py manage` | Read local management JSON on stdin |
| `python -B python/run.py review <request-id>` | Open local review of a pending message |

Use absolute paths when another app starts these commands. Keep passwords and message content out of command-line arguments.

## Local management

Account and mail operations are `list_accounts`, `presets`, `save_account`, `remove_account`, `check_read`, `check_smtp`, `list_folders`, `list_pending`, `prepare_email` and `get_send_status`. Explicit Microsoft sign-in uses `start_microsoft_signin`, `poll_microsoft_signin` and `cancel_microsoft_signin`; these are local management operations, never MCP tools.

Schema 3 account input uses `incoming` with `protocol: "imap"` or `"pop3"` and required `authentication`. Choose `{ "method": "password" }` by default, or explicitly use `{ "method": "microsoft_oauth", "client_id": "YOUR_PUBLIC_CLIENT_GUID", "tenant": "common" }`. Tenant can also be `organizations`, `consumers` or a tenant GUID. Supply `passwords` with both `incoming` and `smtp` keys; Microsoft OAuth2 requires both values to be empty strings. See [authentication](AUTHENTICATION.md) for registration requirements.

`list_pending` returns the latest 100 outgoing records, including completed or uncertain outcomes, so local setup can show recent status as well as messages awaiting review. Its name does not mean every returned record is awaiting approval.

Requests and responses are JSON. A request contains exactly `operation` and `arguments`, for example:

```json
{"operation":"list_accounts","arguments":{}}
```

Write one JSON line to stdin, at most 1 MiB. A successful response has `ok: true` and `result`; failures have `ok: false` and `error`. Keep credentials on stdin and out of shell history. Use Windows setup for interactive configuration. The backend contract defines operation-specific fields; do not infer them from UI labels.

## Data and failures

`AGENTINBOX_DATA_DIR` selects an isolated profile. Use a task-specific directory under ignored `artifacts/` for development.

Unknown formats, invalid fields, unavailable secure storage and unsupported connections fail explicitly. There are no data importers or legacy command aliases.

Stdout contains protocol or management JSON. Diagnostics go to stderr. Local source review needs tkinter and a graphical session; unavailable review facilities must leave the message unsent. The current command surface remains unvalidated.
