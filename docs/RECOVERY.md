# Fix a mailbox connection

Identify the failed step: settings, protected storage, TLS, authentication, mailbox access or SMTP acceptance. A failed connection is not an empty inbox.

Use current settings from your provider. Confirm hostname, port, TLS mode and username. Never disable certificate validation or use an unencrypted connection.

Gmail or iCloud can require an eligible app password. Organization policies can disable protocol access. Outlook.com and Microsoft 365 are unsupported by this implementation.

Unlock the operating-system credential store before retrying. Do not put passwords in configuration JSON. Unknown settings formats fail; create a fresh profile instead of copying previous data.

Run IMAP read and SMTP connection checks separately. SMTP checks do not send mail; test messages require local approval.

If a send has an uncertain outcome, inspect the mailbox or ask your provider before deciding whether to send again. AgentInbox does not retry it automatically.

Removing an account removes local access and stored credentials, not its original mailbox. Revoke a dedicated app password when no longer needed.

Keep private mail and passwords out of support reports. See [privacy](PRIVACY.md).
