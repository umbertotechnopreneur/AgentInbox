# AgentInbox Privacy Policy

Last updated: 2026-10-19.

AgentInbox connects your email to an assistant you choose. It uses IMAP or POP3 to read mail and optional SMTP to send messages you approve locally. This policy describes the current source implementation, which remains unvalidated.

[Product page](https://umbertogiacobbi.biz/agentinbox/) · [Privacy policy](https://umbertogiacobbi.biz/agentinbox/privacy/) · [Terms](https://umbertogiacobbi.biz/agentinbox/terms/)

## Who is responsible

I am Umberto Giacobbi. For privacy questions about AgentInbox or information you send me, contact hello@umbertogiacobbi.biz.

This policy covers the Windows app, Python engine and local MCP connection. AgentInbox does not upload your mailbox to a server I operate. Website browsing is covered by the separate [website privacy notice](https://umbertogiacobbi.biz/privacy/).

## Account settings and passwords

You supply an email address, an incoming protocol, server names, ports, TLS settings and usernames. Password or app password is the default authentication method. AgentInbox uses these settings to connect directly to the email servers you select.

You can explicitly choose Microsoft OAuth2 for a Microsoft account and supply a public client ID and tenant setting from your app registration. Device-code sign-in contacts Microsoft; a browser opens only when you request it. Microsoft processes your sign-in and consent under its policies. AgentInbox receives access and refresh tokens for the requested mail access. It does not provide Google OAuth or automatically change authentication methods after a failure.

Connection settings and account permissions are stored locally. Windows Credential Manager stores passwords and Microsoft refresh tokens. Current-user DPAPI protects temporary outgoing-message and sign-in blobs. The source uses native Keychain on macOS and an available Secret Service on Linux; those paths remain untested. Missing secure storage fails instead of writing plain-text credentials.

These facilities do not encrypt every local metadata file or protect a compromised signed-in operating-system account. Server connections require implicit TLS or required STARTTLS with certificate and hostname checks. There is no plaintext mode or custom-CA setting.

## Reading mail

Requested results can contain subjects, senders, recipients, timestamps, message text and attachment metadata. IMAP can also return read status. Protocol requests go to your configured mail server.

IMAP reads use read-only mailbox selection and non-marking retrieval. Result references and search state are kept in process memory, with ten-minute message references. The engine does not create a full mailbox archive.

POP3 exposes one mailbox as INBOX and has no unread flags or server folders. AgentInbox sends no delete commands; provider retention policies can still affect messages. Searches inspect message headers; dates come from the email Date header rather than server-received time. Header retrieval can therefore occur before local search filtering.

The connected assistant receives requested results. It may send them to an AI service, retain conversations or process data outside your country. AgentInbox does not call an AI model itself, but local MCP does not make the whole workflow offline. Check your client's privacy, retention and training settings.

Passwords, Microsoft tokens and device-sign-in codes are not returned as MCP tool results.

## Preparing and sending mail

SMTP is disabled until you enable it for an account. Your assistant can then prepare a message, including recipients, subject, body and selected local attachment files.

Preparation stores protected message content locally for review. It does not send it. Local review shows all recipients and the exact content. Your approval authorizes that message once. The assistant cannot approve through MCP.

Approved content goes to the configured SMTP server and may subsequently reach recipients and their providers. Server acceptance does not establish final delivery.

Pending requests expire after ten minutes. Expired content is cleaned when the store is next accessed; closing the app does not guarantee immediate deletion of expired disk state. Local request metadata can remain for status reporting until cleaned or removed.

## Local data and diagnostics

The engine uses its local AgentInbox profile or the absolute directory selected by `AGENTINBOX_DATA_DIR`. On Windows, source use defaults to `%LOCALAPPDATA%\AgentInbox`; an MSIX app and its Python alias use `%LOCALAPPDATA%\Packages\<package-family>\LocalState\AgentInbox`. Settings, account metadata, pending-request state and protected content can persist there or in the system credential store. The local `profile_info` command returns paths and storage descriptions without returning secrets.

A persistent local cleanup queue contains references to old credentials, consumed sign-in state and completed or cancelled outgoing content, not their plaintext contents. If deletion fails, the completed operation remains recorded and cleanup stays pending. The app shows a separate warning; **Retry cleanup** retries deletion. Pending protected data can remain until secure storage is available and cleanup succeeds.

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
