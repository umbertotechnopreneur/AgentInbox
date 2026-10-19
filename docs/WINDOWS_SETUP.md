# Windows setup

The WinUI 3 app configures a bundled Python mail engine. See [validation](VALIDATION.md) for package, test and live-provider evidence.

Windows commercial distribution is MSIX only, for Windows 11 x64 and ARM64. Debug 1.0.6.6, including the flat account editor, built and installed locally on x64. Visual interaction and live mail access remain unvalidated.

## Configure an account

If no accounts are saved, choose **Create your first account** to open the form. **Cancel** returns to the empty state. To edit an existing account, use **Edit account**, which shows its name and email address. The colored add icon opens a separate new-account form; Cancel returns to the previous account. Remove and refresh sit beside the selector. Buttons have tooltips.

The flat editor has three views: **Account**, **Incoming** and **Outgoing**. The selected view is underlined. **Create account** or **Save changes** stays at the top right. Connection tests appear after saving and stay disabled while changes are pending. Outgoing tests appear only when SMTP is configured and require sending to be enabled. The test-message action is hidden while sending is off.

In **Account**, choose a provider preset or custom server and enter the account name, address and authentication choice. In **Incoming**, select IMAP or POP3, enter the hostname, port, TLS mode and username, then save before testing the saved connection. Password or app password is the default authentication choice.

For a Microsoft account, you can explicitly select Microsoft sign-in (OAuth2), enter your public-client app registration ID and tenant, save, then press **Sign in to Microsoft…**. The dialog shows a code and opens Microsoft's page only when you press its browser button. A password failure does not start this flow automatically. Registration and tenant policies can still prevent access; see [authentication](AUTHENTICATION.md).

POP3 uses only INBOX and has no unread flags or server folders. Its search inspects headers and uses the email Date header. IMAP supports selected server folders. Connections require TLS or required STARTTLS with certificate and hostname checks; there is no plaintext mode or custom-CA setting.

Enable assistant reads for the accounts you want to share. The read check tests authentication and a bounded mailbox read.

Sending is a separate opt-in in **Outgoing**. Save SMTP settings, then use **Test SMTP connection**. **Send a test email…** is a separate action that opens the normal review flow. Checking SMTP does not send a message.

## Review every send

Review the sending account, all To, CC and BCC recipients, subject, body and attachments. Approval applies once to the exact message.

Test messages follow the same flow. Closing or cancelling review does not authorize sending. Changed content needs a new approval. The engine enforces these rules independently of the UI.

## Local data

The app and engine share schema 3 settings. Passwords and Microsoft refresh tokens use Windows Credential Manager; temporary outgoing-message and sign-in blobs use current-user DPAPI. Unknown formats fail; previous account data is not imported.

An MSIX installation uses `%LOCALAPPDATA%\Packages\<package-family>\LocalState\AgentInbox`; a source launch uses `%LOCALAPPDATA%\AgentInbox`. The packaged Python alias and native app select the same physical profile. The copied MCP configuration includes that exact path. An explicit `AGENTINBOX_DATA_DIR` overrides either default and must be absolute. The local `profile_info` operation reports the selected paths without secrets.

**Connect assistant** detects Codex and guides registration through its installed CLI. Replacement of an existing AgentInbox entry requires confirmation. Manual configuration is under **Advanced setup**. Both paths use the stable `AgentInbox.exe` alias with `-B -m agentinbox mcp`, without version-specific package paths; outgoing review locates the current installed app. See [client setup](CODEX_SETUP.md).

A save, account removal, completed sign-in or outgoing result can succeed while protected-data deletion remains pending. The app shows a separate **Local cleanup is pending** notice. **Retry cleanup** retries queued local deletion without repeating the completed operation or sending another email. Keep the warning until the engine reports that the shared queue is clear.

Removing an account does not delete server messages. See [privacy](PRIVACY.md), [account settings](AUTHENTICATION.md) and [validation](VALIDATION.md).

## Developer installation

For an authorized local installation, use `install-debug-msix -Version <four-part-version>` with the supplied runtime and dependency inputs and an existing current-user certificate. It builds Debug, attempts installation immediately, then checks installed version and package status. Explicit Store work uses the local `build-store-msix -Version X.Y.Z` recipe for unsigned Release x64 and ARM64 packages. Neither recipe uploads or publishes. See the focused [packaging instructions](../packaging/windows/README.md#build-or-install); no package or installation has been validated for this implementation.
