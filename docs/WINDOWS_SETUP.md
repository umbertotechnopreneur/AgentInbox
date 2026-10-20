# Windows setup

The WinUI 3 app configures a bundled Python mail engine. See [validation](VALIDATION.md) for package, test and live-provider evidence.

Windows commercial distribution is MSIX only, for Windows 11 x64 and ARM64. Debug 1.0.9.0 built and installed locally on x64. Account-form navigation, native tabs, resizing and error recovery were exercised with an isolated profile. Saving native credentials and live mail access remain unvalidated.

## Configure an account

If no accounts are saved, choose **Create your first account** to open the form. **Cancel** returns to the empty state. To edit an existing account, use **Edit account**, which shows its name and email address. The colored add icon opens a separate new-account form; Cancel returns to the previous account. Remove and refresh sit beside the selector. Buttons have tooltips.

The editor uses native WinUI tabs: **Account**, **Incoming** and **Outgoing**, with vertically centered headers and the native separator below them. Content stays at the top left and fits the available width. **Create account** or **Save changes** sits at the top right on a wide window; actions and paired fields stack when space is limited. The initial window size follows Windows display scaling and the monitor's available work area.

In **Account**, choose a provider preset or custom server and enter the account name, address and authentication choice. In **Incoming**, select IMAP or POP3, then enter the hostname, port, TLS mode, username and password. **Check connection and get folders** sits directly below the credentials and uses the current form, including unsaved changes.

The account check opens an owned Mica window. It reports actual settings validation, verified TLS, sign-in, folder discovery and optional SMTP authentication. The first check does not read a message or send an email. Cancel stops a running check. Errors stay in the window with the stage that failed; they do not switch the authentication method or save an incomplete account.

After a successful check, select between one and twenty server folders. The list contains exact mailbox names, including names containing commas, rather than raw IMAP descriptions. Current support requires ASCII server names. Existing selections are kept when available; new folders are never selected automatically. **Use selected folders** updates the form without saving it. With a new password-based account, **Create account** runs this same check, then **Add account** in the check window saves the explicit folder choices. The final result remains visible until you close it. A save already in progress cannot be cancelled or repeated automatically.

Choose **Also test reading one message** to run a bounded read from the first selected folder before applying choices or saving. The action label changes to include the read test. IMAP preserves its read status; POP3 sends no delete commands. The result distinguishes a successful sample from an empty folder, and no message content is returned or saved. After adding an account with sending enabled, **Send a test email…** opens recipient selection and the normal preview and approval flow. It does not send immediately. A server's SMTP acceptance is still separate from recipient delivery.

**Web authorization with Microsoft** appears only for supported Microsoft incoming and SMTP servers; Gmail and iCloud presets do not offer it. If a server change invalidates a selected method, choose how to sign in again before saving. For a Microsoft account, explicitly select **Web authorization with Microsoft**, enter your public-client app registration ID and tenant, save, then press **Start web authorization…**. The dialog shows a code and opens Microsoft's page only when you press its browser button. A password failure does not start this flow automatically. Registration and organization rules can still prevent access; see [account settings](AUTHENTICATION.md).

POP3 uses only INBOX and has no unread flags or server folders. Its search inspects headers and uses the email Date header. IMAP supports selected server folders. Connections require TLS or required STARTTLS with certificate and hostname checks; there is no plaintext mode or custom-CA setting.

Enable assistant reads for the accounts you want to share. Saving folder choices does not enable reading by itself. The separate **Test IMAP reading** or **Test POP3 reading** action appears only for a saved account without pending edits; it tests a bounded message read. An empty mailbox is reported separately from a successful sample read.

Sending is a separate opt-in in **Outgoing**. The current-form account check includes SMTP authentication when SMTP is configured. The separate saved-settings SMTP and test-message actions require sending to be enabled; they stay disabled while changes are pending. **Send a test email…** opens the normal review flow. Checking SMTP does not enable sending or send a message.

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
