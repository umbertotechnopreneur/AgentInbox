# Python command reference

The engine requires Python 3.11+. Use `-B` to avoid bytecode beside source files. Synthetic management-session tests pass; process startup and packaged commands still need end-to-end validation.

## Windows installation

The MSIX registers the stable `AgentInbox.exe` execution alias for its bundled Python runtime. Include `-B -m agentinbox` before the command:

```text
AgentInbox.exe -B -m agentinbox manage --session
AgentInbox.exe -B -m agentinbox mcp
AgentInbox.exe -B -m agentinbox review REQUEST_ID
```

For another application, use the absolute alias path `%LOCALAPPDATA%\Microsoft\WindowsApps\AgentInbox.exe`. Do not save paths inside a versioned WindowsApps package directory. The native app opens from the Start menu; the alias runs the mail engine.

## Source checkout

| Command | Purpose |
| --- | --- |
| `python -B python/run.py mcp` | Run MCP over stdio |
| `python -B python/run.py manage` | Read local management JSON on stdin |
| `python -B python/run.py manage --session` | Keep one management process open until stdin ends |
| `python -B python/run.py review <request-id>` | Open local review of a pending message |

Use absolute paths when another app starts these commands. Keep passwords and message content out of command-line arguments.

## Local management

Account operations are `list_accounts`, `presets`, `save_account`, `remove_account`, `check_read`, `check_smtp` and `list_folders`. Mail operations are `search_mail`, `read_mail`, `list_pending`, `prepare_email`, `cancel_email` and `get_send_status`. `profile_info` reports local paths and protection facilities; `retry_cleanup` retries queued local deletion. Explicit Microsoft sign-in uses `start_microsoft_signin`, `poll_microsoft_signin` and `cancel_microsoft_signin`. Configuration, sign-in, cancellation and cleanup operations are not MCP tools.

Schema 3 account input uses `incoming` with `protocol: "imap"` or `"pop3"` and required `authentication`. Choose `{ "method": "password" }` by default, or explicitly use `{ "method": "microsoft_oauth", "client_id": "YOUR_PUBLIC_CLIENT_GUID", "tenant": "common" }`. Tenant can also be `organizations`, `consumers` or a tenant GUID. Supply `passwords` with both `incoming` and `smtp` keys; Microsoft OAuth2 requires both values to be empty strings. See [authentication](AUTHENTICATION.md) for registration requirements.

`list_pending` returns the latest 100 outgoing records, including completed or uncertain outcomes, so local setup can show recent status as well as messages awaiting review. Its name does not mean every returned record is awaiting approval.

Requests and responses are JSON. A request contains exactly `operation` and `arguments`, for example:

```json
{"operation":"list_accounts","arguments":{}}
```

Write one JSON line per request, at most 1 MiB. A successful response has `ok: true` and `result`; failures have `ok: false` and `error`. Plain `manage` processes one request and exits. `manage --session` reads successive requests in the same process until EOF.

Use a session to search and then read a result. References and continuation tokens belong to that process; a second one-shot command cannot reuse them. For example, send these lines to the same session, substituting an account ID and then a reference from its response:

```json
{"operation":"search_mail","arguments":{"account_id":"ACCOUNT_ID","query":"invoice","limit":5}}
{"operation":"read_mail","arguments":{"reference":"REFERENCE_FROM_SEARCH"}}
```

Search parameters and limits match the [MCP read contract](MCP_CONTRACT.md#reading). Keep credentials on stdin and out of shell history. Use Windows setup for interactive configuration.

`cancel_email` accepts exactly `{"request_id":"REQUEST_ID"}` and cancels a message still awaiting approval. It cannot recall accepted mail or stop a submission already underway. Approval remains a separate local review, never a management JSON boolean.

In an installed Windows package, `review REQUEST_ID` asks the current native app to show the pending message. The result `review_requested: true` only confirms the request to open review; it is not approval or SMTP acceptance. After the human decision, use `get_send_status` with the request ID. A source launch with an explicit `AGENTINBOX_REVIEW_APP` uses that app; otherwise source GUI review uses tkinter. `review --pipe` belongs to the native app's private approval channel, not ordinary management automation.

## Data and failures

Use `profile_info` without arguments to inspect the selected profile:

```json
{"operation":"profile_info","arguments":{}}
```

It returns `data_directory`, `settings_file`, `outgoing_ledger`, `temporary_protected_directory`, `cleanup_queue_file`, `credential_store`, `temporary_store` and `schema_version`, plus read-only cleanup status. It reports paths and storage descriptions, not credentials or message content, and does not retry cleanup.

On Windows, a source process defaults to `%LOCALAPPDATA%\AgentInbox`. A packaged process, including the execution alias, defaults to `%LOCALAPPDATA%\Packages\<package-family>\LocalState\AgentInbox`. `AGENTINBOX_DATA_DIR` overrides either with an absolute path. Use a task-specific directory under ignored `artifacts/` for development.

An operation can complete while deletion of old protected data remains pending. Check `cleanup_pending`, `cleanup_pending_count` and `cleanup_error` separately from its result. A count of zero does not mean cleanup succeeded when `cleanup_pending` is true. Retry the shared reference-only queue with:

```json
{"operation":"retry_cleanup","arguments":{}}
```

This retries local deletion of retired credentials, sign-in state and outgoing content. It does not repeat a save, sign-in or email submission.

Unknown formats, invalid fields, unavailable secure storage and unsupported connections fail explicitly. There are no data importers or legacy command aliases.

Stdout contains protocol or management JSON. Diagnostics go to stderr. Local source review needs tkinter and a graphical session. Windows setup provides native outgoing review; unavailable review facilities must leave the message unsent.
