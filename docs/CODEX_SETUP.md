# Connect a local MCP client

AgentInbox supplies a Python MCP server. Guided Codex configuration passed isolated local checks; a live client-to-mail connection remains unvalidated.

## Installed Windows app

Open **Connect assistant**. AgentInbox detects the native Codex CLI, including the copy supplied by the Codex Windows app, and shows the configuration it can update. Press **Connect Codex**. If an AgentInbox entry already differs, **Update connection** asks before replacing only that entry. Other settings remain intact; a changed detection snapshot requires another check.

The app prepares the change in a temporary Codex home and uses the installed CLI to edit its own TOML. It then replaces the configuration atomically. No mail server is started during setup. Python is included in the MSIX; you do not need to install it separately.

Start a new Codex task after saving. A saved configuration is not proof that the assistant has connected or accessed a mailbox. If Codex cannot be detected, open or update Codex and refresh detection, or use **Advanced setup** for manual configuration and other MCP clients. See the [official Codex MCP commands](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

The packaged configuration starts the stable execution alias at `%LOCALAPPDATA%\Microsoft\WindowsApps\AgentInbox.exe`, with these separate arguments:

```text
-B -m agentinbox mcp
```

The configuration sets `AGENTINBOX_DATA_DIR` to the app's physical profile. It does not save a versioned package path or `AGENTINBOX_REVIEW_APP`. The engine locates the current installed review app when it needs native approval. Keep the execution alias enabled in Windows app execution aliases settings.

## Source checkout

Choose a Python 3.11+ interpreter and the checkout's `python/run.py`. Configure your client's local stdio MCP registration to start:

```text
<absolute-python-path> -B <absolute-checkout-path>/python/run.py mcp
```

Pass `-B`, the script path and `mcp` as separate arguments. Do not depend on the client's working directory. Keep the checkout and interpreter at those paths.

## Use the same profile

Configure accounts locally first. The MCP process must use the same profile; an explicit `AGENTINBOX_DATA_DIR` must point to the intended profile. The local management `profile_info` operation reports its paths without returning secrets; see [CLI reference](CLI_REFERENCE.md).

Start a new client session after changing its configuration. List accounts, search one account in a short date range, then read a selected message. Process startup does not prove client integration or mailbox access works.

Sending requires local SMTP enablement and separate local approval for every prepared message. The assistant cannot approve its own send.

See [MCP tools](MCP_CONTRACT.md) and [privacy](PRIVACY.md).

## Export the Python plugin

The files under `integrations/codex` are source templates. When source packaging is authorized, create a fresh local marketplace containing the Python engine:

```text
python -B scripts/export-plugin.py --data-dir <absolute-profile-directory>
```

Use `--python <absolute-interpreter>` if the client should use a different Python installation. On Windows, `--review-app <absolute-AgentInbox.Desktop.exe>` enables automatic opening of the local review window. Without it, open the app's Outgoing page manually, or use the local `review` command with tkinter.

The exporter writes only to a new `artifacts/codex-source` directory. It copies project source, sets the interpreter and profile paths, and includes no passwords, account data or runtime binaries. Do not publish this machine-specific configuration. It does not install or validate the plugin.

The portable manifests use the documented `${PLUGIN_ROOT}` path placeholder. See OpenAI's [local MCP plugin packaging](https://developers.openai.com/plugins/deploy/desktop-only). Register the exported local marketplace in your client and validate it in a new session before relying on it.
