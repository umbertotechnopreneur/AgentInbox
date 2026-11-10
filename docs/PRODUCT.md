# Read and send email with your AI assistant

I built AgentInbox so you can search your mailboxes from one conversation. It uses IMAP or POP3 to read messages and optional SMTP to send them.

Enter server settings and a password or app password, as in a regular mail client. Presets help fill parameters; custom IMAP/POP3/SMTP servers can be entered manually. The Outlook.com and Microsoft 365 presets use **Sign in with Microsoft**. Provider and organization rules still apply.

POP3 exposes INBOX only, without unread flags or server folders; its searches use headers and email Date values. TLS or required STARTTLS always validates certificates and hostnames. There is no plaintext mode or custom-CA setting.

Choose the accounts your assistant can read. Sending starts disabled. When enabled, Codex sends the messages you request through MCP, without a second confirmation window. The setup apps configure accounts and check connections.

The engine is Python. Windows setup uses WinUI 3. The local Linux setup app uses GTK 4 and is available as a Debian package you build from source. macOS uses the CLI from source.

See [getting started](GETTING_STARTED.md) and [privacy](PRIVACY.md).
