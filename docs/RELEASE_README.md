# MailMeUp portable preview

Search your Google and Microsoft inboxes and calendars from your AI assistant.

Use MailMeUp to search your Google and Microsoft inboxes and check your calendars from an AI conversation. This ZIP or archive contains the command-line app. You'll use it to set up Google or Microsoft, sign in, then connect to Codex through MCP, the protocol assistants use to call tools.

This is an early preview. Windows x64 is the tested platform. The Windows ARM64 package builds but hasn't been run on ARM64 hardware. macOS and Linux haven't been tested and aren't supported in this preview.

MailMeUp only reads. It can't send mail, change or delete anything in your accounts, create appointments or send invitations. Information you request can reach your assistant's AI service, even though MailMeUp runs on your computer.

Start with `mailmeup --help` (use `mailmeup.exe` on Windows). Follow the setup guide below to register an app with Google or Microsoft, then run `mailmeup accounts connect <google|microsoft>`. Use `mailmeup setup status` and `mailmeup accounts list` to see what's configured. A fresh installation has no accounts.

App secrets and saved sign-in tokens use your operating system's protected storage. The .NET runtime is included, so you don't need the .NET SDK. You still need a compatible Windows version.

Add MailMeUp to Codex using the full path to your executable:

```text
codex mcp add mailmeup -- /absolute/path/mailmeup --stdio
```

On Windows, replace the example with a path such as `C:\Tools\MailMeUp\mailmeup.exe`. Codex starts MailMeUp for you, so there's no need to start another server. Try asking for unread mail or next week's appointments.

For reference, the tools are `get_status`, `list_accounts`, `search_mail`, `search_unread_mail`, `search_mail_by_date`, `read_mail`, `list_calendars`, `search_events` and `read_event`.

If you want a different data folder, set `MAILMEUP_DATA_DIR` to its full path and keep it private. The archive contains no account tokens or real mailbox data. The app needs a writable local folder to unpack its native libraries.

These portable preview binaries are unsigned. BUILD_INFO.txt lists the version, commit and whether this archive was given a basic startup and command check on its target platform.

[Setup guide](https://github.com/umbertotechnopreneur/MailMeUp/blob/main/docs/GETTING_STARTED.md)

[Documentation and source](https://github.com/umbertotechnopreneur/MailMeUp)

Copyright (c) 2026 Umberto Giacobbi. MIT license; dependency licenses are included separately.
