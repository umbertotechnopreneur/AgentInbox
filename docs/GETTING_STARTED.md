# Connect your mailboxes to your AI assistant

I wanted to use all my mailboxes in one conversation instead of connecting one at a time. With AgentInbox, you add your accounts once and expose the ones you enable through one local MCP connection. You choose reading and sending access separately for each account. See [validation](VALIDATION.md) for what has actually been checked.

## 1. Check your provider

Choose Gmail, iCloud, Microsoft or a custom provider. Gmail and iCloud use an app password. Outlook.com and Microsoft 365 select **Sign in with Microsoft** and the appropriate account type. Custom providers need IMAP or POP3 for reading. Sending is optional and needs SMTP access.

- Gmail requires an eligible account with an app password; organization policies can restrict access.
- iCloud Mail requires an app-specific password.
- Other providers can work when they support these connections.
- Outlook.com and Microsoft 365 use Microsoft sign-in. Windows setup supplies this app's public client ID; you do not enter it in the form. Your mailbox or organization must still allow the selected mail protocols.

Use the settings published by your provider. A preset fills parameters; it does not guarantee your account is eligible.

## 2. Add each mailbox

Open **Accounts** and choose **Add**. Give the connection a name you recognize, choose its provider and enter your email address. Follow the instructions below the provider choice. For Microsoft, press **Sign in with Microsoft**: the app saves complete settings before starting sign-in. For a custom provider, enter the server, port, connection security and sign-in email supplied by your provider.

Choose whether your assistant may read this account. Check the connection, choose the folders it may use and save your changes. Repeat for your other mailboxes. AgentInbox has no fixed mailbox-count limit; the local settings file has a size limit, and your provider controls access to each account.

Use **Test All** to check your saved accounts in sequence. It checks connection and sign-in without reading message content or sending email. It stops at the first error or cancellation. Click an account's result symbol to reopen its details during the current session.

IMAP supports selected server folders. POP3 exposes only INBOX, without unread flags or server folders; its searches use headers and the email Date header. The iCloud preset uses IMAP. See [authentication](AUTHENTICATION.md).

Use implicit TLS or required STARTTLS. Certificate and hostname checks cannot be disabled; there is no plaintext mode or custom-CA setting. AgentInbox never switches sign-in methods automatically. Sign in with Microsoft is available only for Microsoft accounts.

Connection checks and sample reading are separate. You can optionally test reading one message; an empty folder can pass connection and sign-in without providing a sample. A successful check does not prove that every later search will work.

Windows passwords and Microsoft refresh tokens go to Credential Manager. Temporary sign-in blobs use DPAPI. The native macOS Keychain and Linux Secret Service paths remain untested. Unavailable secure storage is an error, not a reason to write plain-text credentials.

## 3. Enable sending if needed

SMTP starts disabled. Enter its host, port, TLS mode and credentials, then run the separate connection check.

That check does not send a message. Enable sending only for the accounts you want your assistant to send from. Specify the sending account, recipients and intended content when asking for a message. There is no second confirmation window in AgentInbox; a test email is a real send too.

## 4. Connect your assistant

In Windows setup, **Connect Codex** guides you through saving the local connection. Use **Test connection**, then open a new Codex chat and ask:

> Which email accounts can you access through AgentInbox?

One MCP connection exposes all the accounts you enable. Ask your assistant to search a named account or several of your mailboxes. For another local MCP client, configure the source entry point below manually.

The source engine needs Python 3.11+:

```text
python -B python/run.py mcp
```

Use absolute paths in your MCP client. See [connection setup](CODEX_SETUP.md) and [MCP tools](MCP_CONTRACT.md).

Start with a narrow search, then read only selected results. Your assistant can send results to its AI service; read [privacy](PRIVACY.md).

Create fresh account settings. Previous profiles are not imported or converted; unknown formats fail. Do not copy old data or passwords into configuration JSON.
