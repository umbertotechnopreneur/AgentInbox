# Completed technical work

Keep short completed-work records here. Open work stays in [todo](todo.md), reusable findings in [lessons](lessons.md), changes in [CHANGELOG](../../CHANGELOG.md) and verification evidence in [validation](../../docs/VALIDATION.md).

## 2026-10-20

- Validated the complete local Python refactoring with all 90 synthetic tests passing on Windows. Built unsigned Debug MSIX 1.0.12.0 x64 from source, checked its checksum and exercised CLI help and isolated account listing through the bundled runtime. Matching build cleanup completed; broader generated-output cleanup was rejected by automatic approval review and remains in todo. No installation, desktop control or real-provider test ran. See validation for the evidence and limits.

## 2026-10-19

- Built, signed and installed Debug MSIX 1.0.11.0 x64 from current source, including the account-check and Mica UI. Windows reports status `Ok`. Matching `dotnet clean` and build-task cleanup completed; removal of the superseded 1.0.10.0 package was blocked by automatic approval review. No UI or provider tests ran during installation.
- Centered account tab headers vertically while preserving the native separator and top-aligned form. Added a shared current-form check with real TLS/sign-in progress, exact server-folder choices, optional bounded sample reading, an owned Mica result window and explicit test-email review after a confirmed save. See the Windows setup guide for the final flow.
- Passed all 90 synthetic Python tests, including thirteen new account-check and folder/progress cases. No real provider or native credential store was used. The new WinUI source was uncompiled at that point and remains visually unverified; final cleanup of a small test log and two empty directories was blocked and remains in the active todo.
- Replaced the mail source with a Python standard-library engine for IMAP, POP3 and optional SMTP, with local approval of each exact outgoing message. Aligned WinUI, CLI, MCP and documentation with the mail-only contract and explicit Microsoft authentication choice.
- Completed the source audit of bounded protocol reads, strict settings, persistent management sessions, stable packaged entry points and cleanup after committed outcomes. See the changelog for individual changes.
- Aligned the MSIX scripts with local packaging and built, signed and installed Debug 1.0.9.0 on Windows x64. Package status is `Ok`; packaged desktop startup and basic account-form navigation were checked with an isolated profile. Packaged CLI/MCP behavior remains untested.
- Ran all 77 synthetic Python tests successfully on Windows with Python 3.13.15. Live providers and native credential APIs still require separate evidence.
- Condensed the working agreement and kept the active todo limited to remaining validation.
- Centralized first-import scans and CI priority in the global agreement. Removed repeated build/CI antivirus scans; later workflow changes remain unexecuted.
- Added the three-tab account editor, colored icons, header actions and guided Codex configuration. Nine isolated setup checks pass; the revised interface is included in 1.0.6.5.
- Removed decorative cards and added explicit empty/create/cancel/edit states, button tooltips and saved-settings test availability. Fixed the runtime failure in 1.0.8.0 by giving each native TabView its own resources. Actual UI checks covered empty/create/cancel flow, all tabs, Gmail authentication filtering, compact and maximized layout, page recreation and error recovery. DPI-scaled initial sizing and provider-specific authentication choices remain in place.
- Replaced mandatory owner version selection with automatic patch increments for authorized builds, using a zero fourth MSIX component.
- Renamed the optional Microsoft connection to "Web authorization" in user-facing app, engine and guide text while keeping the technical protocol and configuration keys. Re-ran all 77 synthetic Python tests successfully.
- Built and signed Debug MSIX 1.0.10.0 from current source on Windows x64. Package signature and checksum are valid; it was not installed. See validation for the first-import scan evidence and cleanup limitation.
