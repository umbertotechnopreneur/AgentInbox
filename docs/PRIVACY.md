# AgentInbox Privacy Policy

Last updated: 2026-10-19.

AgentInbox connects your email to an assistant you choose. It uses IMAP to read mail and optional SMTP to send messages you approve locally. This policy describes the current source implementation, which remains unvalidated.

[Product page](https://umbertogiacobbi.biz/agentinbox/) · [Privacy policy](https://umbertogiacobbi.biz/agentinbox/privacy/) · [Terms](https://umbertogiacobbi.biz/agentinbox/terms/)

## Who is responsible

I am Umberto Giacobbi. For privacy questions about AgentInbox or information you send me, contact hello@umbertogiacobbi.biz.

This policy covers the Windows app, Python engine and local MCP connection. AgentInbox does not upload your mailbox to a server I operate. Website browsing is covered by the separate [website privacy notice](https://umbertogiacobbi.biz/privacy/).

## Account settings and passwords

You supply an email address, server names, ports, TLS settings, usernames and passwords or app passwords. AgentInbox uses them to connect directly to the email servers you select.

Connection settings and account permissions are stored locally. Passwords use operating-system protection: DPAPI on Windows, Keychain on macOS or an available Secret Service on Linux. Missing secure storage fails instead of saving a plain-text password.

These facilities do not encrypt every local metadata file or protect a compromised signed-in operating-system account. Server connections require validated TLS.

## Reading mail

Requested results can contain subjects, senders, recipients, timestamps, message text, read status and attachment metadata. Search filters and requested message identifiers go to your configured IMAP server.

IMAP reads use read-only mailbox selection and non-marking retrieval. Result references and search state are kept in process memory, with ten-minute message references. The engine does not create a full mailbox archive.

The connected assistant receives requested results. It may send them to an AI service, retain conversations or process data outside your country. AgentInbox does not call an AI model itself, but local MCP does not make the whole workflow offline. Check your client's privacy, retention and training settings.

Passwords are not returned as MCP tool results.

## Preparing and sending mail

SMTP is disabled until you enable it for an account. Your assistant can then prepare a message, including recipients, subject, body and selected local attachment files.

Preparation stores protected message content locally for review. It does not send it. Local review shows all recipients and the exact content. Your approval authorizes that message once. The assistant cannot approve through MCP.

Approved content goes to the configured SMTP server and may subsequently reach recipients and their providers. Server acceptance does not establish final delivery.

Pending requests expire after ten minutes. Expired content is cleaned when the store is next accessed; closing the app does not guarantee immediate deletion of expired disk state. Local request metadata can remain for status reporting until cleaned or removed.

## Local data and diagnostics

The engine uses its local AgentInbox profile or the directory selected by `AGENTINBOX_DATA_DIR`. Settings, account metadata, pending-request state and protected content can persist there or in the system credential store.

Diagnostics exclude passwords, mail bodies, attachment contents and authentication exchanges. I do not receive automatic application telemetry or diagnostic uploads. If you contact me, I receive the contact details and material you choose to send. Do not send passwords or complete mailboxes.

Your operating system, backups and assistant may retain their own copies. Protected storage is not secure erasure of those copies.

## Stop access and remove data

Disable assistant reads or SMTP in local account settings to stop those operations. Remove an account to remove its local access and saved credentials. Your original mailbox remains with the provider.

Revoke any dedicated app password using the provider's controls when you no longer need it. Removing local settings does not revoke that password at the provider.

For full local cleanup, remove configured accounts, stop AgentInbox processes and remove the relevant local profile and pending data. Remove its MCP registration from your assistants. Uninstalling alone should not be relied on to erase every copy.

Separately delete conversations, exports and backups using the systems that hold them. I cannot remotely delete data stored only on your device or with another provider.

## Your requests and changes

For information you send me directly, you can request access, correction or deletion and, where applicable, restriction, objection or portability. You may also contact the competent data-protection authority.

I keep support correspondence while needed for the request and any resulting agreement or applicable record-keeping obligation. If an obligation requires retaining information you ask me to delete, I will explain.

I will update this notice when data handling changes. The date above identifies this version.
