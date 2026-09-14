# Connect to Codex

Connect MailMeUp once, then ask Codex to find mail or show appointments from the accounts you've chosen. MailMeUp runs on your computer and only reads. The information you request can still reach the assistant's AI service.

## Windows desktop setup

After installing the Windows app and choosing what to share, open **Connect to Codex**:

1. Select **Check setup** (or **Refresh status**) to see what's ready.
2. Review the results for the MailMeUp command, Codex command-line app, local marketplace, plugin and any direct MCP connection. Adding a marketplace only tells Codex where to find the plugin; the plugin still needs to be installed.
3. Select **Install local plugin**, or follow the help shown if something needs attention. Then start a new Codex task to load the tools.

If setup doesn't work, **Copy setup results** gives you a short diagnostic summary to share. The page shows when it last checked and clears old results when you try again. Failed, unfinished or unavailable checks aren't shown as success. Open **Manual setup (fallback)** for the preparation step and commands to run yourself.

Setup uses the native Codex command-line app to copy the plugin into `%LOCALAPPDATA%\MailMeUp\codex-plugin`, add its local marketplace and install `mailmeup@mailmeup-local`. If you set `MAILMEUP_DATA_DIR`, the plugin goes there instead, and Codex's MailMeUp process uses the same data folder. Nothing is published to a public marketplace.

If setup can't find the native Codex executable, use the manual preparation button, then run the displayed commands in a terminal where `codex` works. This also applies when your Codex command is installed as a script.

If you've already added MailMeUp directly to Codex, guided plugin installation pauses so you don't end up with duplicate tools. Use **Review connections** to keep the direct connection or switch to the plugin. Remove the old entry in Codex's MCP settings yourself if you choose to switch. The page shows both connections if it finds both; setup won't remove one or change other plugins for you.

**An installed plugin still needs a first connection.** Start a new Codex task and ask it to show MailMeUp's status and connected accounts. A successful setup check alone doesn't show that Codex has talked to MailMeUp. The guided Windows setup still needs testing; see [Windows setup](WINDOWS_SETUP.md) and the [test record](VALIDATION.md).

## Keep the connection working after updates

Windows package folders include a version number. Avoid pointing Codex at an executable inside `C:\Program Files\WindowsApps\MailMeUp_...`, because that folder changes with updates.

Setup instead uses Windows' `mailmeup.exe` command shortcut, called an app execution alias. It writes the full path under your `%LOCALAPPDATA%\Microsoft\WindowsApps\mailmeup.exe` into the plugin, with `--stdio`. Windows points this to the installed package. Updates that keep the same package identity and alias don't need a new path in Codex.

If Windows disables the alias, enable MailMeUp under **Settings > Apps > Advanced app settings > App execution aliases**. Reinstalling a different package identity or uninstalling MailMeUp can require setup again.

A new MailMeUp process uses the updated executable through that shortcut. If the plugin's own files or connection settings change, select **Install plugin** again and start a new Codex task.

## Connect manually without a plugin

You can add MailMeUp directly to Codex instead. Use one connection method at a time. For the installed Windows app, run this in PowerShell:

```powershell
$mailmeupAlias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\mailmeup.exe'
codex mcp add mailmeup -- $mailmeupAlias --stdio
```

If you use a custom `MAILMEUP_DATA_DIR`, copy the direct MCP command shown by the Windows app. It includes your data folder.

For the Windows x64 ZIP, extract it into a folder you plan to keep, then add its executable:

```powershell
codex mcp add mailmeup -- 'C:\Tools\MailMeUp\mailmeup.exe' --stdio
```

Codex starts MailMeUp when needed. Sign in to Google and Microsoft through MailMeUp; `codex mcp login` isn't used for those accounts. For a direct connection, `codex mcp list` shows the setup and `codex mcp remove mailmeup` removes the connection from Codex. Removing it leaves your MailMeUp data and Google or Microsoft permissions in place.

Try asking for unread mail or messages between two dates. Searches skip Spam/Junk and Trash/Deleted Items by default, and only use the accounts and types of data you've chosen to share.

For developers: [OpenAI MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), [plugin packaging](https://developers.openai.com/plugins/build/plugins), and [Codex plugin CLI commands](https://learn.chatgpt.com/docs/developer-commands). These references were last reviewed on September 7, 2026; the updated desktop setup hasn't been exercised.
