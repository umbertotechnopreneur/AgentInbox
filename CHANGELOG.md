# Changelog

## Unreleased

- Explain Gmail, iCloud, Outlook.com and Microsoft 365 requirements beneath the provider preset, with official links. Microsoft presets select OAuth and the corresponding account type. Load the recovered public Microsoft app ID from desktop application settings instead of asking each user for it. These changes are source-only pending a new build and checks; registration permissions remain unvalidated.
- Make the Windows app configuration only. Send requested messages directly through Codex/MCP with `send_email`; remove local review, pending messages, the review subprocess and desktop test-send controls. Record metadata and reject duplicate request IDs without automatic retries. Send ledger schema 4 rejects older formats without conversion; account settings remain schema 3.
- Replace the account dropdown with a responsive connection list and keep **Connection name** above the editor. Use Acrylic, native tabs with colored icons and a bold selected label, and a persistent form save bar.
- Use the same owned progress/result window for IMAP and SMTP. Report actual settings, TLS and sign-in stages, support cancellation and show selectable, bounded server diagnostics with credentials redacted. SMTP can check unsaved settings without an incoming password or enabled sending.
- Simplify **Your assistant** with clear Codex connection states, a copyable first prompt and collapsed manual settings. Link the footer to the product Terms/Privacy and VibeWare manifesto.
- Remove Copilot instructions and consolidate technical records. Align account, sending, privacy and developer guides with the current behavior.
- Keep the standard-library Python IMAP/POP3/SMTP engine, explicit Microsoft web authorization, strict TLS, non-marking/non-deleting reads, bounded search and protected credentials. Unsupported settings fail without compatibility shims or imports.
- Preserve committed account/sign-in outcomes when credential cleanup fails; report cleanup separately. Keep passwords and refresh tokens in Credential Manager and temporary sign-in state in DPAPI.
- Use stable packaged Python aliases, persistent management sessions and atomic Codex registration with replacement confirmation and concurrent-change checks.
- Keep Windows distribution MSIX-only, with local Debug and explicit Store Release workflows. Authorized installs may force-close AgentInbox. New dependency imports are scanned once; builds do not trigger scans. Windows CI excludes execution aliases when finding Python.

The current redesign and direct-send source are built and installed as Debug 1.0.14.0 x64. Synthetic tests and UI checks for these changes remain unrun. Local checkpoint commits are now preauthorized at coherent steps. Historical builds, checksums and test results are in [validation](docs/VALIDATION.md); completed milestones are in [archive](.github/tasks/archive.md).
