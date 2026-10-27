# AgentInbox Windows package

I wanted my AI agent to work across all my mailboxes instead of connecting one at a time. Connect as many compatible accounts as you need from Gmail, Microsoft, iCloud, GoDaddy and other IMAP/POP3 providers. One local MCP connection exposes the accounts and folders you enable. With up to twenty selected IMAP folders per account, your agent can work across dozens of folders across mailboxes. POP3 exposes one INBOX per account. Sending through SMTP is optional.

Windows setup uses WinUI 3; the mail engine is Python. Windows distribution is MSIX for Windows 11 x64 and ARM64. See the validation record for source builds, installations and checks actually performed; package installation does not validate provider access or client integration.

Use provider settings and an eligible password or app password. Microsoft presets select **Sign in with Microsoft**; Windows setup supplies the public client ID. Provider and organization rules still apply. Enable receive and optional sending separately for each account in Windows setup. Sending through your assistant requires your explicit request and has no second AgentInbox confirmation window. Guided connection setup is available for Codex; other local MCP clients are configured manually.

See [getting started](GETTING_STARTED.md), [privacy](PRIVACY.md) and [validation](VALIDATION.md).
