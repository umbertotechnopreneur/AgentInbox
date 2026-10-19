# Read and send email with your AI assistant

I built AgentInbox so you can search your mailboxes from one conversation. It uses IMAP or POP3 to read messages and optional SMTP to send them.

Enter server settings and a password or app password, as in a regular mail client. Presets help fill parameters; custom IMAP/POP3/SMTP servers can be entered manually. Microsoft OAuth2 is an explicit optional choice requiring your app registration. It never starts automatically after a password failure. Provider and tenant policies still apply.

POP3 exposes INBOX only, without unread flags or server folders; its searches use headers and email Date values. TLS or required STARTTLS always validates certificates and hostnames. There is no plaintext mode or custom-CA setting.

Choose the accounts your assistant can read. Sending starts disabled. When enabled, the assistant prepares a message, and you approve its exact recipients, text and attachments locally.

The engine is Python. Windows setup uses WinUI 3. Windows commercial distribution is MSIX for Windows 11 x64 and ARM64; Linux and macOS are source-only and unverified. Contributor pull requests improving the native Keychain and Secret Service paths are welcome.

This implementation is in development with no current build, runtime or provider validation. See [getting started](GETTING_STARTED.md), [privacy](PRIVACY.md) and [validation](VALIDATION.md).
