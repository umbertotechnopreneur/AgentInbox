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

Account operations are `list_accounts`, `presets`, `save_account`, `remove_account`, `check_account`, `check_read`, `check_smtp` and `list_folders`. Mail operations are `search_mail`, `read_mail`, `list_pending`, `prepare_email`, `cancel_email` and `get_send_status`. `profile_info` reports local paths and protection facilities; `retry_cleanup` retries queued local deletion. Explicit Microsoft web authorization uses `start_microsoft_signin`, `poll_microsoft_signin` and `cancel_microsoft_signin`. These technical operation names do not change. Configuration, sign-in, cancellation and cleanup operations are not MCP tools.

Schema 3 account input uses `incoming` with `protocol: "imap"` or `"pop3"` and required `authentication`. Choose `{ "method": "password" }` by default, or explicitly use `{ "method": "microsoft_oauth", "client_id": "YOUR_PUBLIC_CLIENT_GUID", "tenant": "common" }` for Microsoft web authorization. Tenant can also be `organizations`, `consumers` or a tenant GUID. Supply `passwords` with both `incoming` and `smtp` keys; web authorization requires both values to be empty strings. The `microsoft_oauth` value is the stable technical configuration key. See [account settings](AUTHENTICATION.md) for registration requirements.

`list_pending` returns the latest 100 outgoing records, including completed or uncertain outcomes, so local setup can show recent status as well as messages awaiting review. Its name does not mean every returned record is awaiting approval.

Requests and responses are JSON. A request contains `operation` and `arguments`, for example:

```json
{"operation":"list_accounts","arguments":{}}
```

Write one JSON line per request, at most 1 MiB. A successful response has `ok: true` and `result`; failures have `ok: false` and `error`. Plain `manage` processes one request and exits. `manage --session` reads successive requests in the same process until EOF.

`check_account` accepts `account`, `passwords` and `expected_revision`, using the same current-form structures as `save_account`, and an optional boolean `read_message` (default false). It validates settings, authenticates over verified TLS, returns exact selectable folder names and checks configured SMTP without saving an account or sending email. With `read_message: true`, it also tests a bounded read from the first selected folder, using IMAP's non-marking read or POP3 without delete commands. The `message_read` result is `not_requested`, `passed` or `no_sample`; an empty folder never counts as a successful sample. No message content is returned. New accounts use an empty ID and a null revision. Existing accounts need their current revision; blank passwords reuse protected credentials only when the server, username and authentication settings are unchanged. Microsoft web authorization must already be completed for the same saved settings.

Only a `check_account` request may also include top-level `"progress": true`. It then emits bounded records such as `{"progress":{"stage":"connection","state":"passed"}}` before the final result or error. Stages are `settings`, `connection`, `authentication`, `folders` and optional `reading` and `smtp`. States are `started` or `passed`, with `empty` for a read check that found no sample. Progress records contain no credentials or account content. Omitting `progress` keeps the ordinary single-result contract. This operation is local management only, not an MCP tool. `list_folders` returns exact selectable IMAP names or the single POP3 name `INBOX`.

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
