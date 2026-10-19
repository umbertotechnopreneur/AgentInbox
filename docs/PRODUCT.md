# Read and send email with your AI assistant

I built AgentInbox so you can search your mailboxes from one conversation. It uses IMAP to read messages and optional SMTP to send them.

Enter server settings and a password or app password, as in a regular mail client. Presets help fill in compatible providers; custom IMAP/SMTP servers can be entered manually. Provider rules still apply.

Choose the accounts your assistant can read. Sending starts disabled. When enabled, the assistant prepares a message, and you approve its exact recipients, text and attachments locally.

The engine is Python. Windows setup uses WinUI 3. Windows commercial distribution is MSIX for Windows 11 x64 and ARM64; Linux and macOS are source-only and unverified.

This implementation is in development with no current build, runtime or provider validation. See [getting started](GETTING_STARTED.md), [privacy](PRIVACY.md) and [validation](VALIDATION.md).
