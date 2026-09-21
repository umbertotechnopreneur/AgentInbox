# Connect to Codex

Connect AgentInbox once, then ask Codex to find mail or show appointments from the accounts you've chosen. AgentInbox runs on your computer and only reads. The information you request can still reach the assistant's AI service.

## Windows desktop setup

After installing the Windows app and choosing what to share, open **Connect to Codex**:

1. Select **Check setup** to inspect this device. If the plugin can be added, select **Install plugin**. If a direct connection needs review, the page shows the plugin and direct connection choices with instructions for the selected method.
2. Follow those instructions to keep one AgentInbox connection in Codex. Change existing connections in Codex yourself, then select **Check again** to refresh the local status. Selecting a method in AgentInbox doesn't change Codex settings. A ready direct connection also offers **Switch to the AgentInbox plugin** in the expandable section.
3. When local setup is ready, copy the suggested first-task prompt and start a new Codex task to try the tools. Select **Finish setup** to close the wizard, or **Finish later** while setup still needs attention.

Plugin maintenance, detailed checks and manual setup are available in the expandable section. **Copy setup results** gives you a short diagnostic summary to share. The page shows when it last checked and clears old results when you try again. Failed, unfinished or unavailable checks aren't shown as success. Adding a marketplace only tells Codex where to find the plugin; the plugin still needs to be installed.

These final-step states are included in the locally installed AgentInbox Debug x64 package `0.0.1.1` and their synthetic tests passed. Native interaction and a live Codex connection remain untested.

Setup uses the native Codex command-line app to copy the plugin into `%LOCALAPPDATA%\AgentInbox\codex-plugin`, add its local marketplace and install `agentinbox@agentinbox-local`. If you set `AGENTINBOX_DATA_DIR`, the plugin goes there instead, and Codex's AgentInbox process uses the same data folder. Nothing is published to a public marketplace.

If setup can't find the native Codex executable, use the manual preparation button, then run the displayed commands in a terminal where `codex` works. This also applies when your Codex command is installed as a script.

If you've already added AgentInbox directly to Codex, guided plugin installation pauses so you don't end up with duplicate tools. Select the method you want to keep and follow its inline instructions. Remove the old direct entry in Codex's MCP settings yourself if you choose to switch to the plugin; disable the plugin in Codex if you keep the direct connection. Direct setup is ready only when inspection confirms one enabled direct registration without a conflicting active plugin. Unknown status remains unresolved.

**An installed plugin still needs a first connection.** Start a new Codex task and ask it to show AgentInbox's status and connected accounts. A successful setup check alone doesn't show that Codex has talked to AgentInbox. The guided Windows setup still needs testing; see [Windows setup](WINDOWS_SETUP.md) and the [test record](VALIDATION.md).

## Keep the connection working after updates

Windows package folders include a version number. Avoid pointing Codex at an executable inside `C:\Program Files\WindowsApps\UmbertoGiacobbiDotBiz.AgentInbox_...`, because that folder changes with updates.

Setup instead uses Windows' `agentinbox.exe` command shortcut, called an app execution alias. It writes the full path under your `%LOCALAPPDATA%\Microsoft\WindowsApps\agentinbox.exe` into the plugin, with `--stdio`. Windows points this to the installed package. Updates that keep the same package identity and alias don't need a new path in Codex.

If Windows disables the alias, enable AgentInbox under **Settings > Apps > Advanced app settings > App execution aliases**. Reinstalling a different package identity or uninstalling AgentInbox can require setup again.

A new AgentInbox process uses the updated executable through that shortcut. If the plugin's own files or connection settings change, use the plugin installation/update action in the expandable section and start a new Codex task.

## Connect manually without a plugin

You can add AgentInbox directly to Codex instead. Use one connection method at a time. For the installed Windows app, run this in PowerShell:

```powershell
$agentInboxAlias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\agentinbox.exe'
codex mcp add agentinbox -- $agentInboxAlias --stdio
```

If you use a custom `AGENTINBOX_DATA_DIR`, copy the direct MCP command shown by the Windows app. It includes your data folder.

For the Windows x64 ZIP, extract it into a folder you plan to keep, then add its executable:

```powershell
codex mcp add agentinbox -- 'C:\Tools\AgentInbox\cli\agentinbox.exe' --stdio
```

Codex starts AgentInbox when needed. Sign in to Google and Microsoft through AgentInbox; `codex mcp login` isn't used for those accounts. For a direct connection, `codex mcp list` shows the setup and `codex mcp remove agentinbox` removes the connection from Codex. Removing it leaves your AgentInbox data and Google or Microsoft permissions in place.

Try asking for unread mail or messages between two dates. Searches skip Spam/Junk and Trash/Deleted Items by default, and only use the accounts and types of data you've chosen to share.

For developers: [OpenAI MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), [plugin packaging](https://developers.openai.com/plugins/build/plugins), and [Codex plugin CLI commands](https://learn.chatgpt.com/docs/developer-commands). These references were last reviewed on September 7, 2026; the updated desktop setup hasn't been exercised.
