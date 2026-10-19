# Windows setup

The WinUI 3 app configures a separate Python mail engine. Current source changes are unbuilt and untested.

Windows commercial distribution is MSIX only, for Windows 11 x64 and ARM64. There is no validated package for this implementation yet.

## Configure an account

Choose a preset or custom server settings. Enter the account address and incoming IMAP hostname, port, TLS mode, username and password or app password.

Enable assistant reads for the accounts you want to share. The read check tests authentication and a bounded mailbox read.

Sending is a separate opt-in. Enter SMTP settings and run its connection check. Checking SMTP does not send a message.

## Review every send

Review the sending account, all To, CC and BCC recipients, subject, body and attachments. Approval applies once to the exact message.

Test messages follow the same flow. Closing or cancelling review does not authorize sending. Changed content needs a new approval. The engine enforces these rules independently of the UI.

## Local data

The app and engine share versioned settings and protected passwords. Unknown formats fail; previous account data is not imported.

An MSIX installation chooses a physical folder under its Windows application local-data directory. A source launch uses the current user's local application data. The copied MCP configuration includes the exact profile path so the app and assistant use the same settings. An explicit `AGENTINBOX_DATA_DIR` overrides the default and must be an absolute path.

Removing an account does not delete server messages. See [privacy](PRIVACY.md), [account settings](AUTHENTICATION.md) and [validation](VALIDATION.md).
