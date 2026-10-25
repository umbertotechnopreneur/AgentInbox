# AgentInbox Privacy Policy

Last updated: 2026-10-23.

AgentInbox connects your email to an assistant you choose. It uses IMAP or POP3 to read mail and optional SMTP to send messages you request through your assistant. This policy describes the current source implementation; see [validation](VALIDATION.md) for the checks performed.

[Product page](https://umbertogiacobbi.biz/agentinbox/) · [Privacy policy](https://umbertogiacobbi.biz/agentinbox/privacy/) · [Terms](https://umbertogiacobbi.biz/agentinbox/terms/)

## Who is responsible

I am Umberto Giacobbi. For privacy questions about AgentInbox or information you send me, contact hello@umbertogiacobbi.biz.

This policy covers the Windows app, Python engine and local MCP connection. AgentInbox does not upload your mailbox to a server I operate. Website browsing is covered by the separate [website privacy notice](https://umbertogiacobbi.biz/privacy/).

## Account settings and passwords

You supply an email address, an incoming protocol, server names, ports, TLS settings and usernames. Password or app password is the default authentication method. AgentInbox uses these settings to connect directly to the email servers you select.

For a Microsoft account, you can explicitly choose **Web authorization** and supply a public client ID and tenant setting from your app registration. AgentInbox then shows a short code for signing in with Microsoft; a browser opens only when you request it. Microsoft processes your sign-in and consent under its policies. AgentInbox receives access and refresh tokens for the requested mail access. Web authorization is not available for Google accounts, and AgentInbox does not change sign-in methods after a failure.

Connection settings and account permissions are stored locally. Windows Credential Manager stores passwords and Microsoft refresh tokens. Current-user DPAPI protects temporary sign-in blobs. The source uses native Keychain on macOS and an available Secret Service on Linux; those paths remain untested. Missing secure storage fails instead of writing plain-text credentials.

These facilities do not encrypt every local metadata file or protect a compromised signed-in operating-system account. Server connections require implicit TLS or required STARTTLS with certificate and hostname checks. There is no plaintext mode or custom-CA setting.

## Reading mail

Requested results can contain subjects, senders, recipients, timestamps, message text and attachment metadata. IMAP can also return read status. Protocol requests go to your configured mail server.

IMAP reads use read-only mailbox selection and non-marking retrieval. Result references and search state are kept in process memory, with ten-minute message references. The engine does not create a full mailbox archive.

POP3 exposes one mailbox as INBOX and has no unread flags or server folders. AgentInbox sends no delete commands; provider retention policies can still affect messages. Searches inspect message headers; dates come from the email Date header rather than server-received time. Header retrieval can therefore occur before local search filtering.

The connected assistant receives requested results. It may send them to an AI service, retain conversations or process data outside your country. AgentInbox does not call an AI model itself, but local MCP does not make the whole workflow offline. Check your client's privacy, retention and training settings.

Passwords, Microsoft tokens and device-sign-in codes are not returned as MCP tool results.

## Sending mail

SMTP starts disabled. Enabling it for an account allows Codex or another connected MCP client to submit messages directly through `send_email`. The Windows app only configures accounts and permissions. AgentInbox does not open a second confirmation window; give your assistant the intended account, recipients, content and attachments before asking it to send.

The engine builds the message and reads selected local attachment files in memory for the submission. It does not save message bodies or attachment bytes to an outgoing queue. The configured SMTP server receives the message and recipients, and it and the recipients can retain copies. Server acceptance does not establish final delivery.

A local metadata ledger retains request and account identifiers, timestamps, status and an optional safe error code. It contains no subjects, recipients, message bodies, attachments or passwords. It prevents the same request ID from sending twice and allows status lookup after a lost response. Unresolved outcomes are not retried automatically. Metadata remains until you remove the relevant local profile; no automatic retention period is promised.

## Local data and diagnostics

The Windows account list requests provider favicons from Google's favicon service over HTTPS. Each URL contains only a fixed public provider domain (Gmail, iCloud or Outlook), never your email address, server name or credentials. Google receives the network request, including your IP address, and may serve the image through its CDN. If the image is unavailable, the app uses a native mail icon. These requests do not check mailbox access.

The engine uses its local AgentInbox profile or the absolute directory selected by `AGENTINBOX_DATA_DIR`. On Windows, source use defaults to `%LOCALAPPDATA%\AgentInbox`; an MSIX app and its Python alias use `%LOCALAPPDATA%\Packages\<package-family>\LocalState\AgentInbox`. Settings, account metadata, send status and protected sign-in state can persist there or in the system credential store. The local `profile_info` command returns paths and storage descriptions without returning secrets.

A persistent local cleanup queue contains references to old credentials and consumed sign-in state, not their plaintext contents. If deletion fails, the completed operation remains recorded and cleanup stays pending. The app shows a separate warning; **Retry cleanup** retries deletion. Pending protected data can remain until secure storage is available and cleanup succeeds.

Diagnostics exclude passwords, mail bodies, attachment contents and authentication exchanges. I do not receive automatic application telemetry or diagnostic uploads. If you contact me, I receive the contact details and material you choose to send. Do not send passwords or complete mailboxes.

Your operating system, backups and assistant may retain their own copies. Protected storage is not secure erasure of those copies.

## Stop access and remove data

Disable assistant reads or SMTP in local account settings to stop those operations. Removing an account removes its local access and queues deletion of its saved credentials. Check and retry any pending-cleanup warning before treating credential deletion as complete. Your original mailbox remains with the provider.

Revoke any dedicated app password or Microsoft consent using the provider's controls when you no longer need it. Removing local settings does not revoke a password, token or consent at the provider.

For full local cleanup, remove configured accounts and complete any pending credential or protected-content cleanup before stopping AgentInbox processes and removing the relevant local profile. Deleting the profile first can remove the references needed to retry credential cleanup. Remove its MCP registration from your assistants. Uninstalling alone should not be relied on to erase every copy.

Separately delete conversations, exports and backups using the systems that hold them. I cannot remotely delete data stored only on your device or with another provider.

## Your requests and changes

For information you send me directly, you can request access, correction or deletion and, where applicable, restriction, objection or portability. You may also contact the competent data-protection authority.

I keep support correspondence while needed for the request and any resulting agreement or applicable record-keeping obligation. If an obligation requires retaining information you ask me to delete, I will explain.

I will update this notice when data handling changes. The date above identifies this version.
