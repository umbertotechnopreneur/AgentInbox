# Changelog

## Unreleased

- Replace the mail engine with Python 3.11+ and standard-library IMAP, POP3 and SMTP.
- Keep WinUI 3 for Windows setup and local message confirmation.
- Use passwords or app passwords by default, with an explicit Microsoft-only OAuth2 option that requires a suitable app registration; never switch automatically.
- Add POP3 mailbox reading with INBOX-only, header-search and email-Date boundaries; no unread flags or deletion.
- Store Windows passwords and Microsoft refresh tokens in Credential Manager; protect temporary outgoing and sign-in blobs with DPAPI.
- Require TLS or STARTTLS with certificate and hostname validation; omit plaintext and custom-CA settings.
- Make SMTP optional and disabled by default; require local approval of every exact message.
- Reject unsupported providers, invalid settings and unknown schemas without fallback.
- Start with fresh configuration, without previous-data import or compatibility layers.
- Replace product, setup, privacy and developer documentation with the current mail-only contract.
- Keep Windows commercial distribution MSIX-only; Linux and macOS remain source-only.
- Replace plugin templates and add a Python source exporter using explicit local interpreter and profile paths.
- Replace packaging commands and workflows with offline, scanned MSIX inputs and Windows synthetic-test sources.
- Follow the local PromptMeUp Store recipe: three-part `-Version`, unsigned Release x64 and ARM64 MSIX packages, and separate signed Debug installation; no automatic publication.
- Check packaging output ancestors as filesystem directories without relying on PowerShell-only properties.
- Add a stable packaged Python execution alias, shared package profile resolution and native review discovery without saved versioned package paths.
- Add persistent local management sessions for search/read references, profile reporting and pending-message cancellation.
- Keep committed outcomes when local protected-data cleanup fails; queue references for explicit retry and show a separate UI warning.
- Add synthetic tests for protocol, account, approval and authentication boundaries; all 77 pass locally on Windows.

WinUI, live providers, native credential stores and package installation remain unvalidated. See [validation](docs/VALIDATION.md) for the local test result and its limits.
