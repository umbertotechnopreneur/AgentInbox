# Active work

The current milestone replaces the mail engine with Python, IMAP/POP3 reads and optional SMTP with local approval. Password is the default; Microsoft OAuth2 is an explicit optional choice. It starts with schema 3 and no compatibility or data-import requirement.

- [x] Write the Python engine, protected credentials, schema 3 settings and bounded IMAP/POP3 operations.
- [x] Add immutable message preparation, local review, single-use approval and uncertain-send handling.
- [x] Replace WinUI 3 setup, connection checks and confirmation using the Python management contract.
- [x] Replace MCP registration, Windows entry points and MSIX packaging sources for Python.
- [x] Replace repository documentation and local website source copy with the mail-only contract.
- [x] Add explicit Microsoft sign-in with public-client registration settings; keep Google OAuth out of scope.
- [x] Author synthetic test sources without executing them.
- [x] Add stable packaged CLI/MCP alias, shared profile reporting and same-process management sessions.
- [x] Add reference-only cleanup retry with separate UI feedback after committed operations.
- [x] Align local Store and Debug installation commands with the PromptMeUp MSIX recipe; keep generated packages and validation pending.
- [ ] Run relevant synthetic, UI, package and separately authorized provider checks only when requested. See [validation](../../docs/VALIDATION.md).

No build, test, live account check, installation or publication has validated this replacement. macOS Keychain and Linux Secret Service source paths remain untested; contributor fixes and validation through pull requests are welcome.
