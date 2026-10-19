# Active work

The current milestone replaces the mail engine with Python, IMAP reads and optional SMTP with local approval. It starts with fresh configuration and no compatibility or data-import requirement.

- [x] Write the Python engine, protected credentials, versioned settings and bounded IMAP operations; inspect source for policy and state-transition errors.
- [x] Add immutable message preparation, local review, single-use approval and uncertain-send handling.
- [x] Replace WinUI 3 setup, connection checks and confirmation using the Python management contract.
- [x] Replace MCP registration, Windows entry points and MSIX packaging sources for Python.
- [x] Replace repository documentation and local website source copy with the mail-only contract.
- [x] Author fifteen synthetic tests without executing them.
- [ ] Run relevant synthetic, UI, package and separately authorized provider checks only when requested. See [validation](../../docs/VALIDATION.md).

No build, test, live account check, installation or publication has validated this replacement.
