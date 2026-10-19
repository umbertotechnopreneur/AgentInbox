# Windows setup

The WinUI 3 app configures a separate Python mail engine. Current source changes are unbuilt and untested.

Windows commercial distribution is MSIX only, for Windows 11 x64 and ARM64. There is no validated package for this implementation yet.

## Configure an account

Choose a preset or custom server settings, then select IMAP or POP3. Enter the account address, incoming hostname, port, TLS mode and username. Password or app password is the default authentication choice.

For a Microsoft account, you can explicitly select Microsoft sign-in (OAuth2), enter your public-client app registration ID and tenant, save, then press **Sign in to Microsoft…**. The dialog shows a code and opens Microsoft's page only when you press its browser button. A password failure does not start this flow automatically. Registration and tenant policies can still prevent access; see [authentication](AUTHENTICATION.md).

POP3 uses only INBOX and has no unread flags or server folders. Its search inspects headers and uses the email Date header. IMAP supports selected server folders. Connections require TLS or required STARTTLS with certificate and hostname checks; there is no plaintext mode or custom-CA setting.

Enable assistant reads for the accounts you want to share. The read check tests authentication and a bounded mailbox read.

Sending is a separate opt-in. Enter SMTP settings and run its connection check. Checking SMTP does not send a message.

## Review every send

Review the sending account, all To, CC and BCC recipients, subject, body and attachments. Approval applies once to the exact message.

Test messages follow the same flow. Closing or cancelling review does not authorize sending. Changed content needs a new approval. The engine enforces these rules independently of the UI.

## Local data

The app and engine share schema 3 settings. Passwords and Microsoft refresh tokens use Windows Credential Manager; temporary outgoing-message and sign-in blobs use current-user DPAPI. Unknown formats fail; previous account data is not imported.

An MSIX installation chooses a physical folder under its Windows application local-data directory. A source launch uses the current user's local application data. The copied MCP configuration includes the exact profile path so the app and assistant use the same settings. An explicit `AGENTINBOX_DATA_DIR` overrides the default and must be an absolute path.

Removing an account does not delete server messages. See [privacy](PRIVACY.md), [account settings](AUTHENTICATION.md) and [validation](VALIDATION.md).
