# Validation

Last updated: 2026-10-19.

The Python IMAP/POP3/SMTP engine, optional Microsoft OAuth2 and WinUI 3 changes are unvalidated source work. No build, test suite, formatter, linter, smoke check, live mailbox check, package installation or deployment has validated this replacement during this task.

Previous implementation results do not validate this engine. There is no measured memory-use claim or current certified provider, platform or MCP-client matrix.

## Verification source

Standard-library unittest sources have been authored under `python/tests/` but have not been imported or executed. They use synthetic accounts, fake credential and transport boundaries, and isolated profiles under `artifacts/tests/`. CI can set `AGENTINBOX_TEST_ROOT` to its own subdirectory under `artifacts/`; paths outside that root or through redirected directories are rejected.

The authored cases cover disabled SMTP, complete local previews, mismatched review challenges, changed message content, frozen attachment bytes, hidden Bcc headers, Unicode MIME, cancellation and approval reuse, outgoing submission leases, interrupted-send recovery, stale read references, recipient acceptance and uncertain DATA outcomes. Additional sources cover POP3 framing, UIDL requirements and incomplete date coverage; Windows Credential Manager routing and failures; and Microsoft sign-in flow, account revision and refresh handling. None of these cases has been executed during this task.

The CI source is configured to run these Python tests on Windows after a targeted Defender scan of its Python installation. That configuration is not a successful CI run. It does not establish WinUI behavior, provider access, MSIX installation or Linux/macOS compatibility.

The manual Windows MSIX workflow source prepares unsigned Debug artifacts from explicitly supplied runtime and dependency inputs. Store packages use the separate local `build-store-msix -Version X.Y.Z` recipe for Release x64 and ARM64. Neither path has run for this replacement or publishes a release, creates a tag or submits to the Store.

Source review also led to a stable packaged execution alias, a persistent management-session mode, explicit profile reporting and a shared cleanup-retry queue. These source changes have not been exercised. No installed alias, package update, native review launch or cleanup-retry result is certified here.

## Checks required when authorized

- Python syntax and synthetic behavior with isolated data and `example.test` accounts.
- Strict schema rejection and no previous-data import.
- Protected credential storage, including unavailable and locked stores, on each platform.
- Cleanup failures after committed saves, removals, sign-in and send outcomes; reference-only queue persistence, retries and separate UI feedback.
- CLI one-shot and session behavior, same-process search/read references, cancellation and secret-free profile reporting.
- IMAP authentication, bounded transport responses and searches, and reads that do not mark messages read.
- POP3 INBOX-only reads, absent unread flags, header/date filtering and no deletion.
- Explicit Microsoft device sign-in, registration and tenant-policy failures, token storage and refresh, cancellation, and absence of automatic authentication fallback.
- Windows Credential Manager secrets and DPAPI temporary blobs; native macOS Keychain and Linux Secret Service paths remain untested.
- SMTP disabled by default, complete local review, single-use approval, cancellation, changed content and concurrent approval.
- SMTP response bounds, rejection, partial acceptance and missing or invalid final replies without automatic duplicate sends.
- WinUI layout, keyboard access, error feedback and native confirmation.
- MSIX alias and runtime bundling, shared packaged profile, native review, and installation on Windows x64 and ARM64; saved MCP configuration after a package update.
- Linux/macOS source execution and local MCP-client integration.
- Separately authorized real-provider checks.

Source inspection, startup, authentication, SMTP acceptance and recipient delivery are different evidence. Record each accurately after checking it.
