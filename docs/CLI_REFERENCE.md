# Python command reference

I wanted my agent to work across all my mailboxes and their folders. The CLI configures as many compatible accounts as you need from Gmail, Microsoft, iCloud, GoDaddy and other IMAP/POP3 providers. Run one MCP process for all enabled accounts, or use a management session. Choose up to twenty IMAP folders per account to work across dozens of folders across mailboxes; POP3 exposes one INBOX per account. Each search targets one account and folder, so repeat it for the mailboxes and folders you want your agent to use.

The engine requires Python 3.11+. Use `-B` to avoid bytecode beside source files. The current isolated Windows suite passed 129 cases, including the new setup commands, with two POSIX-only cases skipped. Native terminal interaction, provider access, installed-package behavior and Linux/macOS execution remain unverified. See [validation](VALIDATION.md).

## Configure your mailboxes from the command line

Start with the [two-mailbox setup guide](CLI_SETUP_GUIDE.md) and its [Gmail/Microsoft JSON example](examples/accounts.setup.json). These commands connect existing mailboxes; they do not create accounts at Google or Microsoft. Add `--help` to any command for its options.

Prefix each command below with `python -B python/run.py` from the source checkout. Set `AGENTINBOX_DATA_DIR` to an absolute profile directory and use the same value for MCP.

| Command | What it does |
| --- | --- |
| `profile show` | Show the selected profile and protected-storage locations |
| `presets` | Show supported providers and connection requirements |
| `accounts list` | List all saved connections, including disabled ones |
| `accounts show ID` | Show settings, current revision and Microsoft sign-in status |
| `accounts add --file FILE --entry gmail` | Create one connection from a named settings entry; prompt for a password if needed |
| `accounts update ID --revision N --file FILE --entry gmail` | Update that connection using its current revision |
| `accounts update ID --revision N --file FILE --entry gmail --replace-passwords` | Also prompt for replacement passwords |
| `accounts signin ID` | Show a Microsoft URL/code and wait for device-code sign-in; Ctrl+C cancels temporary local state |
| `accounts folders ID` | Connect and list available folder names |
| `accounts check ID` | Check saved incoming and configured SMTP connections without reading a sample or sending |
| `accounts check ID --read-message` | Also attempt one bounded sample read without returning message content |
| `accounts check-smtp ID` | Check the SMTP connection without sending |
| `accounts remove ID` | Remove local access and credentials, preserving the provider mailbox |
| `profile cleanup` | Retry pending credential cleanup without repeating the original operation |

All these commands accept `--json` for one `ok`/`result` or `ok`/`error` response. Password prompts, sign-in instructions and connection progress go to stderr. Hidden password input requires an interactive terminal; the CLI refuses an input method that would echo a password. No password option or plaintext credential file is provided. Save, Microsoft authorization and provider connection checks are separate outcomes. Pending cleanup is reported separately from an already committed save or sign-in.

The setup file is UTF-8 JSON, optionally with a BOM, at most 256 KB, with one top-level `accounts` object. Its keys are entry names; each value is the existing account input contract described below. Duplicate JSON keys are rejected. Leave each account's `id` empty. `add` creates a new ID; `update ID` supplies the selected saved ID and requires the explicit revision. Only the chosen entry is validated and saved; no bulk import or implicit overwrite occurs. Passwords and tokens are not allowed in entries. New password-based accounts prompt for incoming and, when configured, SMTP credentials separately. Microsoft OAuth never prompts for a password. Updates reuse existing protected credentials unless `--replace-passwords` is requested; changed server settings can require that option. Changing Microsoft scopes/settings can require another `accounts signin`.

## Windows installation

The MSIX registers the stable `AgentInbox.exe` execution alias for its bundled Python runtime. Include `-B -m agentinbox` before the command:

```text
AgentInbox.exe -B -m agentinbox manage --session
AgentInbox.exe -B -m agentinbox mcp
```

For another application, use the absolute alias path `%LOCALAPPDATA%\Microsoft\WindowsApps\AgentInbox.exe`. Do not save paths inside a versioned WindowsApps package directory. The native app opens from the Start menu; the alias runs the mail engine.

## Source checkout

| Command | Purpose |
| --- | --- |
| `python -B python/run.py mcp` | Run MCP over stdio |
| `python -B python/run.py manage` | Read local management JSON on stdin |
| `python -B python/run.py manage --session` | Keep one management process open until stdin ends |

Use absolute paths when another app starts these commands. Keep passwords and message content out of command-line arguments.

## Local management

Account operations are `list_accounts`, `presets`, `save_account`, `remove_account`, `check_account`, `check_read`, `check_smtp` and `list_folders`. Read/status operations are `search_mail`, `read_mail` and `get_send_status`. Sending is exposed only through MCP. `profile_info` reports local paths and protection facilities; `retry_cleanup` retries queued local deletion. Explicit Microsoft web authorization uses `start_microsoft_signin`, `poll_microsoft_signin` and `cancel_microsoft_signin`. These technical operation names do not change. Configuration, sign-in and cleanup operations are not MCP tools.

Schema 3 account input uses `incoming` with `protocol: "imap"` or `"pop3"` and required `authentication`. Choose `{ "method": "password" }` by default, or explicitly use `{ "method": "microsoft_oauth", "client_id": "YOUR_PUBLIC_CLIENT_GUID", "tenant": "common" }` for Microsoft web authorization. Tenant can also be `organizations`, `consumers` or a tenant GUID. Supply `passwords` with both `incoming` and `smtp` keys; web authorization requires both values to be empty strings. The `microsoft_oauth` value is the stable technical configuration key. See [account settings](AUTHENTICATION.md) for registration requirements.

Requests and responses are JSON. A request contains `operation` and `arguments`.

For an SMTP check, `check_smtp` accepts `account`, `passwords` and `expected_revision`, like `check_account`. It validates the account form but connects only to SMTP, does not require an incoming password or enabled sending, and never saves or sends. Blank SMTP passwords reuse protected credentials only for unchanged saved settings; Microsoft authorization must already cover the saved configuration. The previous `account_id` shortcut is not supported.

For example, list saved accounts:

```json
{"operation":"list_accounts","arguments":{}}
```

Write one JSON line per request, at most 1 MiB. A successful response has `ok: true` and `result`; failures have `ok: false` and `error`. Plain `manage` processes one request and exits. `manage --session` reads successive requests in the same process until EOF.

`check_account` accepts `account`, `passwords` and `expected_revision`, using the same current-form structures as `save_account`, and an optional boolean `read_message` (default false). It validates settings, authenticates over verified TLS, returns exact selectable folder names and checks configured SMTP without saving an account or sending email. With `read_message: true`, it also tests a bounded read from the first selected folder, using IMAP's non-marking read or POP3 without delete commands. The `message_read` result is `not_requested`, `passed` or `no_sample`; an empty folder never counts as a successful sample. No message content is returned. New accounts use an empty ID and a null revision. Existing accounts need their current revision; blank passwords reuse protected credentials only when the server, username and authentication settings are unchanged. Microsoft web authorization must already be completed for the same saved settings.

Only `check_account` and `check_smtp` requests may also include top-level `"progress": true`. They then emit bounded records such as `{"progress":{"stage":"connection","state":"passed"}}` before the final result or error. Both use `settings`, `connection` and `authentication` stages. Account checks also use `folders` and optional `reading` and `smtp`. States are `started` or `passed`, with `empty` for a read check that found no sample. A failed stage never emits `passed`. Progress records contain no credentials or account content. Omitting `progress` keeps the ordinary single-result contract. These operations are local management only, not MCP tools. `list_folders` returns exact selectable IMAP names or the single POP3 name `INBOX`.

Use a session to search and then read a result. References and continuation tokens belong to that process; a second one-shot command cannot reuse them. For example, send these lines to the same session, substituting an account ID and then a reference from its response:

```json
{"operation":"search_mail","arguments":{"account_id":"ACCOUNT_ID","query":"invoice","limit":5}}
{"operation":"read_mail","arguments":{"reference":"REFERENCE_FROM_SEARCH"}}
```

Search parameters and limits match the [MCP read contract](MCP_CONTRACT.md#reading). Keep credentials on stdin and out of shell history. Use the account commands above or Windows setup for interactive configuration.

## Data and failures

Local management failures can include `error.server_response` for IMAP and SMTP errors, separately from `error.code` and `error.message`. This bounded, untrusted diagnostic retains protocol status text and SMTP codes while redacting known credentials and email addresses and removing control characters. It is for local display, is not logged, and is omitted from MCP error results. Absence of this field means no server or connection detail was captured; the app must not invent one.

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

This retries local deletion of retired credentials and sign-in state. It does not repeat a save, sign-in or email submission.

Unknown formats, invalid fields, unavailable secure storage and unsupported connections fail explicitly. There are no data importers or legacy command aliases.

`mcp` and `manage` keep stdout reserved for protocol/management JSON. Human-facing setup commands use readable text unless `--json` is selected. Diagnostics go to stderr. The Windows native app opens configuration only and accepts no command-line arguments. Removed commands fail explicitly; there is no review subprocess or GUI dependency in the Python engine.
