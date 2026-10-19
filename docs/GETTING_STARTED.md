# Getting started

AgentInbox connects to email servers through IMAP for reading and optional SMTP for sending. The Python engine and Windows setup are in development and unvalidated.

## 1. Check your provider

You need an IMAP server that accepts a password or app password over TLS. Sending also needs SMTP access.

- Gmail requires an eligible account with an app password; organization policies can restrict access.
- iCloud Mail requires an app-specific password.
- Other providers can work when they support these connections.
- Outlook.com and Microsoft 365 are unsupported. Their presets fail before attempting a connection.

Use the settings published by your provider. A preset fills parameters; it does not guarantee your account is eligible.

## 2. Enter incoming mail settings

In Windows setup, choose a preset or custom connection, then enter the account address, IMAP host, port, TLS mode, username and password. Choose whether your assistant may read that account.

The read check tests authentication and a bounded mailbox read. It does not prove that every folder or later search will work. An empty mailbox can be a successful connection with no message to sample.

Passwords go to protected system storage. Unavailable secure storage is an error, not a reason to write a plain-text password.

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
