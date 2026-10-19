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
- Author synthetic tests for protocol, account, approval and authentication boundaries; execution is pending.

The implementation remains unvalidated. No package or runtime validation is claimed.
