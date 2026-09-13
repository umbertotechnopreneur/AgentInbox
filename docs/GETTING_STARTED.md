# Getting started

MailMeUp is a local, read-only MCP program. The current tested target is Windows x64. Keep the extracted folder in a stable private location.

## Preview the Windows setup screens

The locally installed Windows x64 `0.1.1.20` preview provides commands to list and open each setup screen. Build and synthetic checks passed; native interaction checks remain pending.

```powershell
mailmeup ui --list-steps
mailmeup ui --list-steps --json
mailmeup ui --step sharing --demo
```

Choose `welcome`, `accounts`, `sharing` or `codex` with `--step`. `--demo` opens a separate preview with sample accounts: sign-in, provider reads and real Codex actions are disabled. Sharing and search-period changes stay in memory and reset when the demo window closes. The preview does not load your real accounts or credentials.

Omit `--demo` to open your local setup. Plain `mailmeup ui` opens Welcome in a new window or brings the current page forward. Explicit screen changes protect unsaved sharing and search-period edits. Listing screens starts no UI or account services.

Opening screens requires Windows and a built desktop executable. When it is not packaged with the CLI, provide its path:

```powershell
mailmeup ui --step accounts --demo --desktop-path 'C:\Build\MailMeUp.Desktop.exe'
```

See [wizard implementation](WIZARD_UI.md) for preview behavior and current validation limits.

## 1. Register the provider apps

Follow the short [Google and Microsoft registration guide](APP_REGISTRATION.md). Keep the Google JSON file private. Microsoft supplies a public Application (client) ID.

## 2. Configure MailMeUp

Windows example:

```powershell
.\mailmeup.exe setup google 'C:\Private\client_secret.json'
.\mailmeup.exe setup microsoft '<application-client-id>'
.\mailmeup.exe setup status
```

macOS and Linux are not tested because no test machines are available, so the current MVP does not claim support for them. Windows ARM64 builds but has not been executed on ARM64 hardware.

## 3. Connect accounts

Run the command once for each account:

```powershell
.\mailmeup.exe accounts connect google
.\mailmeup.exe accounts connect microsoft
.\mailmeup.exe accounts list
```

Add `--mail-only` or `--calendar-only` when you want only one read category.

## 4. Add it to Codex

```powershell
codex mcp add mailmeup -- 'C:\Tools\MailMeUp\mailmeup.exe' --stdio
codex mcp list
```

Restart or reload Codex if the new MCP server is not visible. Then try:

> Use MailMeUp to search all my connected inboxes for the quarterly plan.

> Use MailMeUp to list unread mail from all connected accounts, excluding Spam/Junk and Trash/Deleted Items.

> Use MailMeUp to list mail received between 2026-09-01 and 2026-09-05, optionally filtering by sender, recipient or attachments.

> Use MailMeUp to show appointments from all my connected calendars for the next seven days.

MailMeUp returns short results first and reads details only when requested. It cannot send mail, change messages, edit appointments or send invitations.

The current source limits mail searches without dates to the previous 14 days. In the Windows app, open **Sharing → Default mail search period** to choose 1–365 days and save. Longer periods take more time and requests and may hit provider limits. Explicit search dates override this default. This change was introduced in `0.1.1.19` and is included in the locally installed `0.1.1.20` preview; UI and provider validation remain pending.

Remove a local account with `mailmeup accounts remove <account-id>`. This removes local metadata and cached credentials; provider access can be revoked separately in Google or Microsoft account settings.
