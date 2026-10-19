# Changelog

## Unreleased

- Replace the mail engine with Python 3.11+ and standard-library IMAP and SMTP.
- Keep WinUI 3 for Windows setup and local message confirmation.
- Use server settings and passwords or app passwords with protected system storage.
- Make SMTP optional and disabled by default; require local approval of every exact message.
- Reject unsupported providers, invalid settings and unknown schemas without fallback.
- Start with fresh configuration, without previous-data import or compatibility layers.
- Replace product, setup, privacy and developer documentation with the current mail-only contract.
- Keep Windows commercial distribution MSIX-only; Linux and macOS remain source-only.
- Replace plugin templates and add a Python source exporter using explicit local interpreter and profile paths.
- Replace packaging commands and workflows with offline, scanned MSIX inputs and Windows synthetic-test sources.
- Add fifteen synthetic tests for message approval, SMTP outcomes and account-policy boundaries; execution is pending.

The implementation remains unvalidated. No package or runtime validation is claimed.
