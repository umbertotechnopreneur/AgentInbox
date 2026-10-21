# Fix a mailbox connection

Identify the failed step: settings, protected storage, TLS, authentication, mailbox access or SMTP acceptance. A failed connection is not an empty inbox.

Use current settings from your provider. Confirm protocol, hostname, port, TLS mode and username. TLS or required STARTTLS validates certificates and hostnames. There is no plaintext mode or custom-CA setting; do not bypass a certificate failure.

Gmail or iCloud can require an eligible app password. Organization rules can disable mail access. Outlook.com requires Web authorization with Microsoft; Exchange Online no longer accepts ordinary passwords for IMAP and POP3. For these services, explicitly choose **Web authorization** with your own Microsoft app registration. The registration, your consent and your organization's rules are separate requirements. A preset never guarantees access, and AgentInbox never switches sign-in methods automatically. See [account settings](AUTHENTICATION.md).

Unlock the operating-system credential store before retrying. Do not put passwords in configuration JSON. Unknown settings formats fail; create a fresh profile instead of copying previous data.

Run the selected IMAP/POP3 read check and SMTP connection check separately. SMTP checks do not send mail. A test message must be requested through Codex and is a real submission. POP3 has only INBOX and no unread flags; an unread filter or another folder is an error, not an empty result.

If a send has an uncertain outcome, inspect the mailbox or ask your provider before deciding whether to send again. AgentInbox does not retry it automatically.

Removing an account removes local access and queues credential deletion. Complete any pending cleanup before treating deletion as finished; the original mailbox stays with the provider. Revoke a dedicated app password or Microsoft consent using the provider's controls when no longer needed.

Keep private mail and passwords out of support reports. See [privacy](PRIVACY.md).
