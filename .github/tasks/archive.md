# Completed technical work

Implementation history is in [CHANGELOG](../../CHANGELOG.md), evidence in [validation](../../docs/VALIDATION.md), and remaining work in [todo](todo.md).

## 2026-10-22 — source changes

- Removed the Microsoft browser-address allowlist, kept sign-in available with pending changes, added save-before-sign-in and a bounded private local trace, and added native field-level validation. These are source changes only; see validation for execution limits.
- Replaced Username labels/errors with email wording, added blank sign-in email defaults at save/check time, clarified local Codex configuration and implemented a user-triggered MCP launcher check with no mail-tool calls. These changes are newer than installed 1.0.16.0; execution remains pending.
- Added provider persistence, shared Microsoft sign-in presentation, local redacted Microsoft replies, plain setup wording and the Connect Codex title. Copied the approved VibeWare artwork and license, linked the footer to the manifesto and recorded display rules. Built source checkpoint `2f72826`, signed and installed Debug 1.0.16.0 x64 with status `Ok`. The owner excluded automated tests and is trying the installed app directly; UI/provider validation remains pending. See validation for the retained installer and blocked cleanup.
- Added provider-specific sign-in instructions and official links, Microsoft OAuth/account-type defaults and the recovered public client ID in application settings. Built source checkpoint `a4cb4be`, signed and installed Debug 1.0.15.0 x64 with status `Ok`. Runtime checks and live registration validation remain pending; see validation for the retained installer and blocked superseded-package cleanup.
- Restricted Windows setup to accounts, permissions and connecting Codex. Removed native/Python message review, the preparation queue and desktop send actions; MCP now submits directly with durable status and duplicate prevention.
- Added Acrylic, a connection list with visible renaming, form-level Save, shared IMAP/SMTP dialogs, colored tabs, clearer Codex setup and product-policy/footer links.
- Removed Copilot instructions and consolidated repository records. Updated repository policies and local website policy sources. Debug 1.0.14.0 was subsequently built, signed and installed on x64 with status `Ok`. Tests and UI checks remain unrun; website changes remain unpublished. Added standing authorization for frequent local checkpoint commits. See validation for retained packages and blocked cleanup.

## 2026-10-21

- Implemented current-form SMTP checks and local, redacted server diagnostics; built and installed Debug 1.0.13.0 x64. Added package-scoped app shutdown for later authorized installations.
- Completed an explicitly authorized, unauthenticated Gmail STARTTLS handshake. The owner subsequently reported fixing the SMTP password.

## 2026-10-20

- Passed 90 synthetic tests for the then-current Python implementation. Built unsigned Debug 1.0.12.0 and checked isolated CLI/management startup through the bundled runtime.
- Fixed Windows CI interpreter discovery to exclude WindowsApps execution aliases. Owner cleanup removed historical artifacts; blocked legacy cleanup remains open.

## 2026-10-19

- Replaced the engine with standard-library Python IMAP/POP3/SMTP, strict settings, protected credentials, explicit Microsoft sign-in, bounded reads and durable cleanup. The original local-approval send model was implemented and tested then; it was replaced on 2026-10-22.
- Added native account tabs, explicit create/cancel states and guided Codex registration. Fixed the TabView resource-ownership failure observed in 1.0.8.0.
- Ran the historical 77- and 90-test suites, nine isolated Codex setup checks and limited isolated UI checks. Built/installed Debug 1.0.9.0 and 1.0.11.0; built/signed 1.0.10.0 without installing it.
- Aligned local MSIX packaging, automatic version increments, one-time import scans and artifact cleanup with the working agreement.
