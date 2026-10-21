# AgentInbox Terms of Use

Last updated: 2026-10-22.

I build AgentInbox to help you read email and request optional sends through your chosen assistant. These terms describe its boundaries and your responsibilities.

[Product page](https://umbertogiacobbi.biz/agentinbox/) · [Privacy policy](https://umbertogiacobbi.biz/agentinbox/privacy/)

## Developer and source

I am Umberto Giacobbi, and I maintain AgentInbox with help from contributors. Contact hello@umbertogiacobbi.biz with questions.

The public source is [MIT-licensed](https://github.com/umbertotechnopreneur/AgentInbox/blob/main/LICENSE). That license governs your rights to use, copy, modify and distribute it. These terms do not remove those rights. Third-party components retain their own licenses.

The Python engine and Windows configuration app are in development. See [validation](VALIDATION.md) for the checks performed on particular source snapshots. Windows commercial distribution is MSIX for Windows 11 x64 and ARM64. Linux and macOS are source-only, without compiled distributions or commercial support. Source availability does not establish platform compatibility or Store availability.

## Authorized accounts and data

Connect only mailboxes you own or are authorized to use. Follow your organization's rules and provider policies. You choose which accounts your assistant may read and whether SMTP is enabled.

Keep your device and credentials secure. Do not put passwords, private messages or local settings in public issues.

Your email provider, MCP client and AI service have their own terms and may charge separately. AgentInbox is independent; naming a provider or client does not imply endorsement.

## Reading and sending

IMAP and POP3 reads depend on connection permissions, server behavior, search scope and limits. POP3 exposes INBOX only, without unread flags or server folders; it searches headers and uses the email Date header. Results can be partial or fail. Check important information against the original message.

Password sign-in is the default. Optional Microsoft **Web authorization** requires your explicit selection and your own suitable app registration. Provider and organization rules can prevent access; no preset guarantees compatibility. AgentInbox never switches sign-in methods automatically. Connections require validated TLS or STARTTLS, without plaintext or custom-CA controls.

SMTP starts disabled. When you enable it, your connected assistant can submit email immediately through MCP. AgentInbox does not ask for a second confirmation. Specify the intended account, recipients, message and attachments before asking your assistant to send. A test email is also a real submission and can disclose information to recipients and their providers.

SMTP acceptance is not proof of recipient delivery. An uncertain outcome may require checking your mailbox or provider before deciding whether to send again.

Your assistant may forward results to an AI service, retain them or produce inaccurate summaries. AgentInbox does not control separate tools or copies held by those services. See [privacy](PRIVACY.md).

## Warranty and support

The source is provided as is, with the warranty and liability terms in the MIT license. Nothing here excludes rights or responsibilities that cannot legally be excluded, including applicable consumer rights for a paid edition.

I do not promise uninterrupted access, a particular support response time or a service level unless separately agreed in writing.

## Stopping use and changes

Remove local accounts and MCP registrations when you stop using AgentInbox. Revoke dedicated app passwords or Microsoft consent with your provider and follow the privacy policy to remove local and assistant-held copies.

I may update these terms as the app changes. Updates do not revoke MIT rights or retroactively remove rights attached to a purchase. Changes requiring notice or consent will be handled before they apply.
