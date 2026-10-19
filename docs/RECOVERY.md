# Fix a mailbox connection

Identify the failed step: settings, protected storage, TLS, authentication, mailbox access or SMTP acceptance. A failed connection is not an empty inbox.

Use current settings from your provider. Confirm protocol, hostname, port, TLS mode and username. TLS or required STARTTLS validates certificates and hostnames. There is no plaintext mode or custom-CA setting; do not bypass a certificate failure.

Gmail or iCloud can require an eligible app password. Organization policies can disable protocol access. Outlook.com requires modern authentication; Exchange Online removed Basic authentication for IMAP and POP3. For these services, explicitly choose Microsoft OAuth2 with a suitable public-client app registration. Client registration, consent and tenant policy are separate requirements. A preset never guarantees access, and AgentInbox never switches authentication automatically. See [authentication](AUTHENTICATION.md).

Unlock the operating-system credential store before retrying. Do not put passwords in configuration JSON. Unknown settings formats fail; create a fresh profile instead of copying previous data.

Run the selected IMAP/POP3 read check and SMTP connection check separately. SMTP checks do not send mail; test messages require local approval. POP3 has only INBOX and no unread flags; an unread filter or another folder is an error, not an empty result.

If a send has an uncertain outcome, inspect the mailbox or ask your provider before deciding whether to send again. AgentInbox does not retry it automatically.

Removing an account removes local access and stored credentials, not its original mailbox. Revoke a dedicated app password or Microsoft consent using the provider's controls when no longer needed.

Keep private mail and passwords out of support reports. See [privacy](PRIVACY.md).
