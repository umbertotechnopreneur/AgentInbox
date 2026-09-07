# Connect to Codex

MailMeUp runs locally as a read-only MCP server. Google and Microsoft sign-in and sharing choices stay in MailMeUp. Requested message and appointment content can reach the assistant's AI service.

## Windows desktop setup

The new WinUI setup source includes **Connect to Codex**. After installing the MSIX and choosing what to share:

1. Select **Check setup** (or **Refresh status**) to inspect local configuration.
2. Review the separate results for the MailMeUp command, native Codex CLI, local marketplace, plugin and direct MCP registration. **Marketplace added** does not mean **plugin installed**.
3. Select **Install local plugin** when available, or the state-specific help action if setup needs attention. After installation, start a new Codex task to load its tools.

The page includes the last attempt time and **Copy setup results** for sharing a safe diagnostic summary. A failed, unsupported, timed-out or unperformed check is not shown as success. A new attempt clears old findings. **Manual setup (fallback)** holds the preparation action and commands; the main screen does not run a CLI prompt or display a terminal transcript.

The setup uses the native Codex CLI. It copies the bundled plugin into `%LOCALAPPDATA%\MailMeUp\codex-plugin`, adds that local marketplace and installs `mailmeup@mailmeup-local`. When `MAILMEUP_DATA_DIR` is set, the plugin files are placed under that directory and the same data directory is passed to the MCP server. These are local files and configuration changes; nothing is published to a public marketplace.

If the native CLI is unavailable, prepare the plugin files in MailMeUp and run the displayed commands in a terminal where `codex` is available. A CLI installed only as a script uses this manual route.

An existing direct MailMeUp MCP registration blocks guided plugin installation to avoid duplicate tools; it is not itself a mailbox error. **Review connections** explains how to keep the direct route or switch deliberately. If both the direct registration and enabled local plugin are found, the page reports both. Review the entry in Codex's MCP settings and remove it yourself only if switching to the plugin. Setup does not remove registrations or change other plugins.

**Status means configuration status.** An installed and enabled plugin has not necessarily completed an MCP handshake. Start a new Codex task and ask it to show MailMeUp status and configured accounts when you are ready to exercise the connection. The desktop/MSIX onboarding path still requires owner-approved build and package validation.

## Stable path across MSIX updates

MSIX package directories contain a version. Do not configure Codex with an executable inside `C:\Program Files\WindowsApps\MailMeUp_...`.

The package declares the `mailmeup.exe` app execution alias. Setup writes the fully resolved path under the current user's `%LOCALAPPDATA%\Microsoft\WindowsApps\mailmeup.exe` into the plugin, together with `--stdio`. Windows resolves that alias to the installed package, so ordinary updates that preserve the package identity and alias do not require a new Codex executable path.

If Windows disables the alias, enable MailMeUp under **Settings > Apps > Advanced app settings > App execution aliases**. Reinstalling a different package identity or uninstalling MailMeUp can require setup again.

The local plugin source and its MCP command are separate: routine executable updates use the alias immediately, while changes to plugin metadata or wiring require selecting **Install plugin** again and starting a new Codex task.

## Direct MCP alternative

A direct registration is also supported and does not install a plugin. Use one connection method at a time. For an installed MSIX, run this in PowerShell:

```powershell
$mailmeupAlias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\mailmeup.exe'
codex mcp add mailmeup -- $mailmeupAlias --stdio
```

The desktop's displayed direct MCP command also includes the resolved `MAILMEUP_DATA_DIR`. Use that command when using an explicit data directory.

For the existing Windows x64 ZIP, extract into a stable directory and register that executable:

```powershell
codex mcp add mailmeup -- 'C:\Tools\MailMeUp\mailmeup.exe' --stdio
```

Codex starts the process. Mailbox authentication happens in MailMeUp, not through `codex mcp login`. For direct registrations, `codex mcp list` shows configuration and `codex mcp remove mailmeup` removes only the MCP registration; it does not revoke provider access or delete MailMeUp data.

For common mailbox requests, use unread-mail or date-range search. Mail searches exclude Spam/Junk and Trash/Deleted Items by default. Account and category sharing choices apply to assistant reads.

Developer references: [OpenAI MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), [plugin packaging](https://developers.openai.com/plugins/build/plugins), and [Codex plugin CLI commands](https://learn.chatgpt.com/docs/developer-commands). MCP settings and CLI status documentation re-read on 2026-09-07; the updated desktop flow has not been executed.
