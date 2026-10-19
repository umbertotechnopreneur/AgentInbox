# Connect a local MCP client

AgentInbox supplies a Python MCP server. Client setup for this implementation has not been validated.

Choose a Python 3.11+ interpreter and the checkout's `python/run.py`. Configure your client's local stdio MCP registration to start:

```text
<absolute-python-path> -B <absolute-checkout-path>/python/run.py mcp
```

Pass `-B`, the script path and `mcp` as separate arguments. Do not depend on the client's working directory. Keep the checkout and interpreter at those paths.

Configure accounts locally first. The MCP process must use the same profile; an explicit `AGENTINBOX_DATA_DIR` must point to the intended profile.

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
