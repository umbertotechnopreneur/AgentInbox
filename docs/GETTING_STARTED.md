# Getting started

This guide gets AgentInbox connected to your accounts and Codex on Windows x64. It's still an early preview, so keep an eye on the results as you try it.

If you installed the Windows app, start with the [four setup screens](WINDOWS_SETUP.md#setup). If you're using the ZIP, extract it into a private folder you plan to keep, such as `C:\Tools\AgentInbox`. The command-line steps are below.

## Take a look around first

You can try the Windows setup with sample accounts before connecting your own:

These commands are included in preview `0.1.1.20`. Build and automated checks passed for this feature; clicks and navigation in the Windows app still need checking.

```powershell
agentinbox ui --list-steps
agentinbox ui --list-steps --json
agentinbox ui --step sharing --demo
```

Use `--step` to choose `welcome`, `accounts`, `sharing` or `codex`. With `--demo`, the window uses made-up accounts. It won't sign in, read your mailbox or change Codex. Any sharing or search-period changes disappear when you close the demo.

Leave out `--demo` to open your own setup. `agentinbox ui` opens Welcome, or brings the existing window forward. If you change screens with unsaved sharing or search-period edits, the app asks you what to do with them. Listing screens doesn't open a window or load accounts.

The desktop app needs to be available alongside the command-line app. If you built it in another folder, give its path:

```powershell
agentinbox ui --step accounts --demo --desktop-path 'C:\Build\AgentInbox.Desktop.exe'
```

The September 13 code also adds read limits and usage information to Sharing. Those controls aren't in the installed `0.1.1.20` preview and haven't been tested yet. See [read limits](READ_GUARDRAILS.md) and [setup details for developers](WIZARD_UI.md).

## 1. Set up Google or Microsoft

Follow the [registration guide](APP_REGISTRATION.md) for the service you use. For now, this means creating your own app registration. Google gives you a JSON configuration file to keep private; Microsoft gives you an Application (client) ID.

## 2. Configure AgentInbox

Open PowerShell in the folder where you extracted AgentInbox. Run the setup command for each service you want to use:

```powershell
.\agentinbox.exe setup google 'C:\Private\client_secret.json'
.\agentinbox.exe setup microsoft '<application-client-id>'
.\agentinbox.exe setup status
```

This guide is for Windows x64. The Windows ARM64 package builds but hasn't been run on ARM64 hardware. macOS and Linux haven't been tested and aren't supported in this preview.

## 3. Connect accounts

Run the command once for each account:

```powershell
.\agentinbox.exe accounts connect google
.\agentinbox.exe accounts connect microsoft
.\agentinbox.exe accounts list
```

Your browser will open for sign-in. Add `--mail-only` or `--calendar-only` if you only want to share email or calendars.

## 4. Add it to Codex

```powershell
codex mcp add agentinbox -- 'C:\Tools\AgentInbox\agentinbox.exe' --stdio
codex mcp list
```

Replace the example path with your own. If AgentInbox doesn't appear in Codex, restart or reload Codex. Then try:

> Use AgentInbox to search all my connected inboxes for the quarterly plan.

> Use AgentInbox to list unread mail from all connected accounts, excluding Spam/Junk and Trash/Deleted Items.

> Use AgentInbox to list mail received between 2026-09-01 and 2026-09-05, optionally filtering by sender, recipient or attachments.

> Use AgentInbox to show appointments from all my connected calendars for the next seven days.

You'll get a short list first, then you can ask to open a message or appointment. AgentInbox can't send mail, change messages, edit appointments or send invitations.

## Choose how far back to search

Without dates in your request, mail searches use the past 14 days. In the Windows app, open **Sharing → Default mail search period**, choose 1–365 days and save. Searching further back takes longer and makes more requests to Google or Microsoft, which may ask AgentInbox to wait. Dates in your request take priority over this setting.

This setting was added in `0.1.1.19` and is included in `0.1.1.20`. The desktop controls and searches against real accounts still need checking for this change.

## Remove an account

Use `agentinbox accounts list` to find its ID, then `agentinbox accounts remove <account-id>`. This removes the account and its saved sign-in tokens from this device. To revoke the app's access too, use your Google or Microsoft account settings.
