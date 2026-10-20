# Getting started

AgentInbox connects to email servers through IMAP or POP3 for reading and optional SMTP for sending. The Python engine and Windows setup are in development and unvalidated.

## 1. Check your provider

Choose an incoming protocol supported by your provider. Password or app password is the default. For Outlook.com or Microsoft 365, you can explicitly choose **Web authorization** and sign in with Microsoft using a short code. Sending needs SMTP access.

- Gmail requires an eligible account with an app password; organization policies can restrict access.
- iCloud Mail requires an app-specific password.
- Other providers can work when they support these connections.
- Outlook.com and Microsoft 365 presets fill server settings. Outlook.com requires Web authorization with Microsoft; Exchange Online no longer accepts ordinary passwords for IMAP and POP3. This choice requires your own Microsoft app registration, and organization rules can still restrict access.

Use the settings published by your provider. A preset fills parameters; it does not guarantee your account is eligible.

## 2. Enter incoming mail settings

In Windows setup, choose a preset or custom connection and select IMAP or POP3. Enter the account address, host, port, TLS mode and username. Keep Password selected and enter a password, or explicitly choose **Web authorization with Microsoft** for a Microsoft account and supply the client ID and tenant settings. Save before testing or starting web authorization. Choose whether your assistant may read that account.

IMAP supports selected server folders. POP3 exposes only INBOX, without unread flags or server folders; its searches use headers and the email Date header. The iCloud preset uses IMAP. See [authentication](AUTHENTICATION.md).

Use implicit TLS or required STARTTLS. Certificate and hostname checks cannot be disabled; there is no plaintext mode or custom-CA setting. AgentInbox never switches sign-in methods automatically. Web authorization is not available for Google accounts.

The read check tests authentication and a bounded mailbox read. It does not prove that every folder or later search will work. An empty mailbox can be a successful connection with no message to sample.

Windows passwords and Microsoft refresh tokens go to Credential Manager. Temporary outgoing-message and sign-in blobs use DPAPI. The native macOS Keychain and Linux Secret Service paths remain untested. Unavailable secure storage is an error, not a reason to write plain-text credentials.

## 3. Enable sending if needed

SMTP starts disabled. Enter its host, port, TLS mode and credentials, then run the separate connection check.

That check does not send a message. A test email is a real send: prepare it, review every recipient and its content, then approve it locally.

## 4. Connect your assistant

The source engine needs Python 3.11+:

```text
python -B python/run.py mcp
```

Use absolute paths in your MCP client. See [connection setup](CODEX_SETUP.md) and [MCP tools](MCP_CONTRACT.md).

Start with a narrow search, then read only selected results. Your assistant can send results to its AI service; read [privacy](PRIVACY.md).

Create fresh account settings. Previous profiles are not imported or converted; unknown formats fail. Do not copy old data or passwords into configuration JSON.
