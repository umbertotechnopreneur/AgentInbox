# Validation

Last updated: 2026-10-19.

The Python IMAP/SMTP engine and WinUI 3 changes are unvalidated source work. No build, test suite, formatter, linter, smoke check, live mailbox check, package installation or deployment has validated this replacement during this task.

Previous implementation results do not validate this engine. There is no measured memory-use claim or current certified provider, platform or MCP-client matrix.

## Verification source

Fifteen standard-library unittest cases have been authored under `python/tests/` but have not been imported or executed. They use synthetic `example.test` accounts, an in-memory credential store, fake mail connections and isolated profiles under `artifacts/tests/`. CI can set `AGENTINBOX_TEST_ROOT` to its own subdirectory under `artifacts/`; paths outside that root or through redirected directories are rejected.

The cases cover disabled SMTP, complete local previews, mismatched review challenges, changed message content, frozen attachment bytes, hidden Bcc headers, Unicode MIME, cancellation and approval reuse, outgoing submission leases, interrupted-send recovery, stale read references, recipient acceptance and uncertain DATA outcomes.

The CI source is configured to run these Python tests on Windows after a targeted Defender scan of its Python installation. That configuration is not a successful CI run. It does not establish WinUI behavior, provider access, MSIX installation or Linux/macOS compatibility.

The manual Windows MSIX workflow source prepares unsigned Debug or Store artifacts from explicitly supplied runtime and dependency inputs. It has not run for this replacement and does not publish a release, create a tag or submit to the Store.

## Checks required when authorized

- Python syntax and synthetic behavior with isolated data and `example.test` accounts.
- Strict schema rejection and no previous-data import.
- Protected credential storage, including unavailable and locked stores, on each platform.
- IMAP authentication, bounded searches and reads that do not mark messages read.
- SMTP disabled by default, complete local review, single-use approval, cancellation, changed content and concurrent approval.
- SMTP rejection, partial acceptance and uncertain outcomes without automatic duplicate sends.
- WinUI layout, keyboard access, error feedback and native confirmation.
- MSIX entry points, runtime bundling and installation on Windows x64 and ARM64.
- Linux/macOS source execution and local MCP-client integration.
- Separately authorized real-provider checks.

Source inspection, startup, authentication, SMTP acceptance and recipient delivery are different evidence. Record each accurately after checking it.
