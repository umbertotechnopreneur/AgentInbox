# AgentInbox Windows package

AgentInbox reads email through IMAP or POP3 and optionally sends through SMTP. Windows setup uses WinUI 3; the engine is Python.

There is no validated package for this implementation yet. Windows commercial distribution is MSIX for Windows 11 x64 and ARM64.

Use provider settings and an eligible password or app password. For a Microsoft account, **Web authorization** is an explicit optional choice that requires your own app registration; provider and organization rules still apply. AgentInbox does not switch sign-in methods automatically, and Web authorization is not available for Google accounts. SMTP starts disabled. When enabled, sending happens through Codex without a second AgentInbox confirmation. Windows setup configures accounts and permissions only.

See [getting started](GETTING_STARTED.md), [privacy](PRIVACY.md) and [validation](VALIDATION.md).
