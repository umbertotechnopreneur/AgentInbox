# AgentInbox Windows package

AgentInbox reads email through IMAP or POP3 and optionally sends through SMTP. Windows setup uses WinUI 3; the engine is Python.

There is no validated package for this implementation yet. Windows commercial distribution is MSIX for Windows 11 x64 and ARM64.

Use provider settings and an eligible password or app password. Microsoft OAuth2 is an explicit optional choice that requires your public-client app registration; provider and tenant policies still apply. There is no automatic method fallback or Google OAuth flow. SMTP starts disabled. Every message, including a test email, needs local approval of exact recipients, text and attachments.

See [getting started](GETTING_STARTED.md), [privacy](PRIVACY.md) and [validation](VALIDATION.md).
