# Changelog

## Unreleased

- Validate the complete local Python refactoring with 90 synthetic Windows tests and a local Debug 1.0.12.0 x64 source build; check bundled-runtime CLI and management startup with an isolated profile. Keep provider, desktop and cleanup limits in validation.
- Center account tab headers vertically while preserving the native separator and top-aligned settings form.
- Add a visible current-form connection check, an owned Mica progress window and explicit server-folder selection. Report actual TLS and sign-in results, offer a bounded optional message-read test, and distinguish an empty folder from a successful read. New password-based accounts share this flow before saving; a saved account can open the separately approved test-email flow.
- Replace the mail engine with Python 3.11+ and standard-library IMAP, POP3 and SMTP.
- Keep WinUI 3 for Windows setup and local message confirmation.
- Use passwords or app passwords by default, with an explicit Microsoft-only web authorization choice that requires a suitable app registration; never switch automatically.
- Explain the optional Microsoft connection as "Web authorization" in the app, local-management messages and reader-facing guides; retain the technical protocol and configuration keys.
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
- Fix the WinUI paired-control layout types and exclude stale `bin`/`obj` files from source discovery.
- Keep isolated builds free of compiler-server file locks and automatic HTTPS certificate setup; retain packaging diagnostics on failure.
- Declare multiple-instance support required by the console execution alias.
- Scan new external imports once; stop repeated antivirus scans during local builds and CI while retaining archive hashes and official-source checks.
- Split account setup into Account, Incoming and Outgoing tabs; move Save to the header and add compact colored action, navigation and provider icons.
- Group incoming tests and SMTP connection/test-message actions in their relevant tabs; move outgoing-request refresh to a header icon.
- Restore guided Codex detection and explicit connection setup through its installed CLI, with atomic configuration updates, replacement confirmation, concurrent-change checks and manual setup in Advanced.
- Remove decorative cards throughout WinUI and use standard Account, Incoming and Outgoing tabs. Show an explicit empty state and open the account form only after Create; add Cancel, a named edit selector and button tooltips.
- Size the initial window in logical pixels using the monitor's DPI, with a larger 1280 × 960 target limited to its work area.
- Anchor pages at the top left, bind their width to the visible scroll viewport and stack header/account actions and paired fields in narrow windows.
- Fix the account-page startup failure caused by assigning an application-owned resource dictionary to TabView. Use the stock WinUI TabView theme and sizing with native leading tab icons. Replace incomplete pages with an explicit retry state and report unexpected setup errors without incorrectly blaming Python.
- Hide Microsoft web authorization for Gmail, iCloud and unsupported server combinations. Require a fresh connection choice if a server edit invalidates the selected method; keep the engine's Microsoft-only endpoint validation.
- Keep saved-account message-reading and send tests disabled for pending edits; provide a separate current-form check before saving. Reflect committed saves and removals directly from their results instead of depending on a second account-list request.
- Add a stable packaged Python execution alias, shared package profile resolution and native review discovery without saved versioned package paths.
- Add persistent local management sessions for search/read references, profile reporting and pending-message cancellation.
- Keep committed outcomes when local protected-data cleanup fails; queue references for explicit retry and show a separate UI warning.
- Add synthetic tests for protocol, account, approval, authentication and current-form checks; all 90 pass locally on Windows. The latest WinUI source is compiled and installed in Debug 1.0.11.0; visual checks remain pending.

Debug MSIX 1.0.11.0 was built and signed from current source and installed locally on Windows x64 with status `Ok`. It includes the account-check and Mica UI changes. All 90 synthetic Python tests pass for the new engine source. Earlier 1.0.9.0 UI checks and nine Codex setup checks remain separate evidence. The new UI flow, live providers, native credential stores and packaged CLI/MCP entry points remain unvalidated. See [validation](docs/VALIDATION.md).
