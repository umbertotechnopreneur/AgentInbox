# Connect a local MCP client

I wanted one assistant connection for all my mailboxes. AgentInbox exposes your enabled Gmail, iCloud, Microsoft and custom-provider accounts through one local MCP connection. Your assistant lists the accounts you share, then searches, reads or sends through the account you specify. You do not need a separate MCP registration for each mailbox.

Windows setup guides you through connecting Codex. Another assistant can use the same mail tools if you configure its local MCP client manually. Codex starts AgentInbox on your PC when it needs its mail tools. Windows setup adds an entry to Codex's settings file. See the [official Codex MCP guide](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

## Installed Windows app

Open **Connect Codex**. AgentInbox finds Codex on your computer and reads its saved connection settings. Press **Connect to Codex**. If an AgentInbox entry already differs, **Update connection** asks before replacing only that entry. The page shows which Codex settings file it uses.

**AgentInbox is enabled in Codex settings** means that an enabled entry matches this installation. It does not mean that a Codex chat has used it. Press **Test connection** to check the saved entry, start its local AgentInbox command, complete the MCP greeting and ask for the five mail-tool definitions. The check makes no mail-tool calls: it does not list your accounts, sign in to a mail server, read email or send anything. It stops after 30 seconds and closes its child process. Changed settings, a missing launcher, a failed startup or an unexpected reply produce a failure rather than a success state.

**Connection details** shows the app path, separate startup options, the AgentInbox settings folder and the full generated JSON. Matching saved entries and settings still to be added have different labels. The JSON has a multiline, scrollable preview; it is for manual setup in another MCP app. Codex keeps its own entry in TOML, in the file shown above. Neither format contains account passwords.

The app prepares the change in a temporary Codex home and uses the installed CLI to edit its own TOML. It then replaces the configuration atomically. No mail server is started during setup. Python is included in the MSIX; you do not need to install it separately.

After the local test, open a new Codex chat and try the account-list request shown in the app. A successful local check does not prove that Codex loaded the connection or that a mailbox can be opened. If Codex cannot be found, open or update it and choose **Check Codex again**. Mailbox checks stay under **Accounts**.

The packaged configuration starts the stable execution alias at `%LOCALAPPDATA%\Microsoft\WindowsApps\AgentInbox.exe`, with these separate arguments:

```text
-B -m agentinbox mcp
```

The configuration sets `AGENTINBOX_DATA_DIR` to the app's physical profile. Keep the execution alias enabled in Windows app execution aliases settings.

## Source checkout

Choose a Python 3.11+ interpreter and the checkout's `python/run.py`. Configure your client's local stdio MCP registration to start:

```text
<absolute-python-path> -B <absolute-checkout-path>/python/run.py mcp
```

Pass `-B`, the script path and `mcp` as separate arguments. Do not depend on the client's working directory. Keep the checkout and interpreter at those paths.

## Use the same profile

Configure accounts locally first. The MCP process must use the same profile; an explicit `AGENTINBOX_DATA_DIR` must point to the intended profile. The local management `profile_info` operation reports its paths without returning secrets; see [CLI reference](CLI_REFERENCE.md).

Start a new client session after changing its configuration. List accounts, search one account in a short date range, then read a selected message. Process startup does not prove client integration or mailbox access works.

Sending requires SMTP to be enabled in Windows setup. Codex submits an explicitly requested message immediately through `send_email`; AgentInbox does not open another approval window. Keep the request ID to check unresolved outcomes, and never retry them automatically.

See [MCP tools](MCP_CONTRACT.md) and [privacy](PRIVACY.md).

## Export the Python plugin

The files under `integrations/codex` are source templates. When source packaging is authorized, create a fresh local marketplace containing the Python engine:

```text
python -B scripts/export-plugin.py --data-dir <absolute-profile-directory>
```

Use `--python <absolute-interpreter>` if the client should use a different Python installation. The engine has no GUI dependency; the Windows app is only for configuration.

The exporter writes only to a new `artifacts/codex-source` directory. It copies project source, sets the interpreter and profile paths, and includes no passwords, account data or runtime binaries. Do not publish this machine-specific configuration. It does not install or validate the plugin.

The portable manifests use the documented `${PLUGIN_ROOT}` path placeholder. See OpenAI's [local MCP plugin packaging](https://developers.openai.com/plugins/deploy/desktop-only). Register the exported local marketplace in your client and validate it in a new session before relying on it.
