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

An MSIX installation uses `%LOCALAPPDATA%\Packages\<package-family>\LocalState\AgentInbox`; a source launch uses `%LOCALAPPDATA%\AgentInbox`. The packaged Python alias and native app select the same physical profile. The copied MCP configuration includes that exact path. An explicit `AGENTINBOX_DATA_DIR` overrides either default and must be absolute. The local `profile_info` operation reports the selected paths without secrets.

**Connect assistant** exports the stable `AgentInbox.exe` alias with `-B -m agentinbox mcp`. It avoids saved version-specific package paths, and outgoing review locates the current installed app. See [client setup](CODEX_SETUP.md).

A save, account removal, completed sign-in or outgoing result can succeed while protected-data deletion remains pending. The app shows a separate **Local cleanup is pending** notice. **Retry cleanup** retries queued local deletion without repeating the completed operation or sending another email. Keep the warning until the engine reports that the shared queue is clear.

Removing an account does not delete server messages. See [privacy](PRIVACY.md), [account settings](AUTHENTICATION.md) and [validation](VALIDATION.md).

## Developer installation

For an authorized local installation, use `install-debug-msix -Version <four-part-version>` with the supplied runtime and dependency inputs and an existing current-user certificate. It builds Debug, attempts installation immediately, then checks installed version and package status. Explicit Store work uses the local `build-store-msix -Version X.Y.Z` recipe for unsigned Release x64 and ARM64 packages. Neither recipe uploads or publishes. See the focused [packaging instructions](../packaging/windows/README.md#build-or-install); no package or installation has been validated for this implementation.
