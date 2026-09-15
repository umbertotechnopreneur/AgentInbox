# Changelog

## Unreleased

- Clear the repository `artifacts/` directory before packaging or validation builds, with a shared cleanup script that refuses links and junctions. Document the same cleanup step for manual builds. Execution checks have not been run.

- Redesign all four Windows setup screens with a compact sidebar, fixed page headings, fewer cards and a shared Help menu. Welcome uses one introduction; Accounts adds providers through a centered dialog; Sharing shows saved account summaries and opens a focused editor; Codex presents one primary action with detailed diagnostics in a dialog. Separate Search, Usage and Limits into settings tabs, preserve save/discard and sequential calendar selection, and keep native scrolling/input and responsive spacing. Source changes only; not built, tested or installed.

- Add source-only Inbox scoping for mail search. MCP unread search now defaults to Inbox, unread state and the applied date range, with an explicit opt-out for broader searches. Gmail filters by `INBOX` before preview retrieval; Microsoft uses the Inbox collection. Return `inbox_only` and bind continuations to the selected scope. All Inbox categories and senders remain eligible. This increment is not built, tested or installed.

- Add source-only Windows Sharing controls for read limits and aggregate usage, with draft validation, Save/Discard/defaults, stale-save protection and separate saved/active settings. Refresh reads local counters without provider traffic; changing settings preserves usage and requires restarting participating processes. Extend the isolated demo and MCP status with synthetic/local usage respectively. Regression sources are added but not run; this increment is not built or installed.

- Add source-only shared provider request limits, cross-process cooldowns, Microsoft 429 recovery, cumulative read/MCP output budgets and 2,000-character default details. Reduce eager cross-account mail hydration, cache recent details and Microsoft folder identities, and fix Gmail preview attachment metadata and incomplete external text handling. Local controls are configurable through `read-guardrails.json`; the UI distinguishes budget exhaustion from reconnect failures. Synthetic regression sources are added but not compiled or run; these changes are not in installed `0.1.1.20`. See [read guardrails](docs/READ_GUARDRAILS.md).

- Build, sign and locally install Windows x64 MSIX `0.1.1.20` with CLI screen selection, isolated demo mode and the refined setup screens. An independent package-state read confirmed version `0.1.1.20`, Windows status `Ok` and a valid signature after the installation tool response was interrupted. Earlier 291-test and preview smoke evidence is unchanged; native UI interaction remains pending.
- Add `mailmeup ui --list-steps` and direct Welcome, Accounts, Sharing and Codex navigation with isolated `--demo` accounts. Preserve unsaved edits and the current page on ordinary activation. Refine Welcome cards, account grouping, sharing controls, narrow layouts and expandable Codex details. Windows x64 preview publication, 291 .NET tests and published CLI/MCP smoke passed; native interaction and installation of this increment remain pending.
- Build, sign and locally install Windows x64 MSIX `0.1.1.19` with bounded mail-search defaults and Google request-limit recovery. Desktop/CLI Release publication and signing succeeded; installed version `0.1.1.19`, status `Ok` and valid signature were confirmed. The owner-requested follow-up passed all 229 .NET tests; post-install UI and live-provider checks remain pending.
- Default mail searches without dates to the previous 14 days, with a global 1–365-day setting in the Windows Sharing page and a warning about longer searches. Preserve explicit date requests, freeze continuation windows and report effective bounds through MCP; include received-date constraints in Microsoft text searches. Included in `0.1.1.19` with passing synthetic regressions.
- Distinguish Google request limits from permission failures, pace per-account reads and retry transient throttling with bounded waits. Avoid reconnect guidance for rate-limit-only account checks. Included in `0.1.1.19` with passing synthetic regressions; live-provider validation remains pending.
- Build, sign and locally install Windows x64 MSIX `0.1.1.18` with Gmail no-content response handling, broader bounded calendar checks and the actionable Codex setup screen. Installed version/status and package signature were confirmed; unit tests, smoke checks and post-install runtime checks were not run.
- Make Connect to Codex show separate local command, native CLI, marketplace, plugin and direct MCP findings, with an attempt timestamp and copyable safe results. Replace blocked install buttons with state-specific guidance; distinguish an added marketplace from an installed plugin. Clear stale success on retries/cancellation and keep manual commands as a fallback, without running prompts or changing existing connections during inspection. Source and synthetic regressions await validation and packaging.
- Handle observed Gmail message-list HTTP 204 responses as empty pages, include `resultSizeEstimate` in the list projection, and keep required-detail, malformed-payload and HTTP-error paths strict. Remove the five-calendar check cutoff while retaining time/page limits, continuation handling and explicit coverage diagnostics. Source changes and synthetic regressions await validation and packaging.
- Build, sign and locally install Windows x64 MSIX `0.1.1.17` with correlated logs, bounded read checks and the minimal account UI. Its new regressions were compiled but not executed; the Google HTTP 204 parsing issue identified in owner-triggered logs was still present in that package.
- Simplify connected-account rows to an address, an optional red **Try to reconnect** action and an account menu. Keep all check diagnostics in the log; missing event samples and sampling limits no longer look like broken connections.
- Add correlated provider diagnostics with pseudonymous account keys, HTTP status, fixed endpoint/read phases, allowlisted error codes and exception types, without logging credentials or mail content.
- Replace minimal connection probes with bounded search/detail checks using the production readers. Preserve independent Mail/Calendar outcomes; show missing samples, incomplete checks, exceptions and cancellation without a false all-clear or stale green status.

- Fix Microsoft text, sender and recipient mail searches by separating Graph `$search` from `$filter`/`$orderby`. Preserve local folder exclusions, structured filters and continuation through excluded pages; classify HTTP 400 as an invalid request.
- Allow the documented rolling diagnostic files during stateless CLI/MCP smoke validation while continuing to reject database, configuration, credential or other first-run state.
- Increase the visual size of Windows package icons by generating the exact MSIX tile assets from the tighter original artwork.
- Add per-account **Reconnect** and **Remove from device** actions to the Windows setup UI, with confirmation, preserved sharing choices on reconnection, and clear separation between local credential removal and provider consent.
- Add bounded Serilog diagnostics shared by the Windows setup app and CLI/MCP process, with daily local files, operation timings, independent mail/calendar check totals and safe failure categories for comparing UI and Codex behavior.
- Build, sign and locally install Windows x64 MSIX `0.1.1.15` with account recovery, Serilog diagnostics, the larger package icon and the Microsoft search fix; the installed package reports status `Ok`.
- Add a Windows-only **Check read access** action that verifies every locally connected Google and Microsoft account with minimal Mail/Calendar requests, reports Mail and Calendar independently, silently refreshes tokens when possible, and marks reconnect-required capabilities without exposing provider response content.
- Redesign the Windows setup wizard with native Acrylic, a shared title bar, unnumbered visual progress, local Google/Microsoft SVG logos and generated illustrations.
- Replace long account cards with searchable, paged rows and one sharing editor at a time. Reveal calendar choices, provider registration, privacy details and manual Codex setup on request; retain explicit saving and protect unsaved changes.
- Redirect additional desktop launches to the existing setup window, restoring it when minimized. CLI/MCP processes remain independent.
- Load bundled Google and Microsoft SVGs with WinUI's `SvgImageSource`, so the setup window can be created instead of falling back to the local-storage error screen.
- Explicitly start the setup window after registering the primary Windows App SDK instance; the custom entry point previously kept an invisible process alive without calling the window-start path.
- Treat high-contrast change notifications as optional when the desktop WinRT event is unavailable, instead of replacing the setup UI with its fallback window.
- Replace the dark Mica backdrop with Desktop Acrylic because Windows transparency is enabled but Mica was too subtle on the owner's dark desktop.
- Build, sign and locally install the redesigned wizard and connection check as Windows x64 MSIX `0.1.1.10`. Release build, 113 shared .NET tests, published and installed-alias CLI/MCP smoke, and a synthetic native-window launch without diagnostics passed; desktop rendering and interaction checks remain pending with the owner.

- Fix the packaged WinUI startup crash when an account database already exists by using SQLitePCL directly instead of triggering the Microsoft.Data.Sqlite application-data probe. Add a synthetic installed-desktop startup smoke test for this regression.
- Auto-format staged C# files before commits, apply style fixes during local validation and keep CI formatting checks read-only.
- Add an English About & Support dialog with a generated MailMeUp banner, app-version copying, creator website and GitHub links, support issues, and an invitation to star the project.

- Add a centered WinUI 3 setup window with a privacy welcome, Google/Microsoft browser sign-in, multiple accounts, sharing choices and guided local Codex plugin installation.
- Keep the setup interface in English, with persistent links to the website and the MailMeUp, Google and Microsoft privacy policies and terms.
- Share application service composition between CLI/MCP and the Windows UI; keep the MCP process separate from the setup window.
- Store local account, mail and calendar sharing separately from OAuth grants, with new UI-connected accounts initially unshared and read restrictions enforced in running MCP sessions.
- Add Windows MSIX packaging with a stable `mailmeup.exe` console app execution alias and optional signing with an existing certificate.
- Ask callers to notify the user in plain English when MailMeUp cannot read a mailbox, including actionable, sanitized error details and partial-coverage reporting.

The desktop, plugin and sharing behavior remains a preview. Windows x64 MSIX `0.1.1.12` is locally installed as an upgrade. Earlier `0.1.1.4` startup checks and earlier About dialog inspection do not validate the redesigned UI. Clean-machine installation, UI sign-in and Codex plugin loading remain pending. See [validation](docs/VALIDATION.md).

## 0.1.1 — Mail search ergonomics

- Add dedicated unread and received-date-range mail tools with sender/recipient contains and attachment filters; exclude Gmail Spam/Trash and Microsoft Junk/Deleted Items by default.
- Return read status and attachment presence in compact mail results, and run independent account searches with bounded concurrency.
- Preserve existing credentials after failed reconnect validation or metadata persistence, and coordinate credential sessions across processes.
- Bound provider reads and continuation work; report timeouts, removed accounts and incomplete calendar discovery as partial coverage.
- Handle calendar null fields and all-day/time-zone boundaries without silently guessing missing event times.
- Harden the manual provider runner with CI refusal, per-account outcomes and bounded calendar batches.
- Add synthetic recovery, credential-session, calendar boundary and pagination regressions, plus isolated tests for the manual provider runner.
- Keep unit-test execution local on request; CI retains build, isolated protocol smoke and repository checks without live accounts.
- Add a Spectre.Console CLI with a compact linked banner, emoji section dividers, readable results, next steps and sign-in activity feedback, without cards or panels.
- Preserve redirected JSON and add explicit `--json`, `--no-color`, `--no-animation`, configurable log levels and Ctrl+C cancellation.
- Route bounded application diagnostics through `ILogger<T>` and Serilog to stderr for both CLI and MCP.
- Add a concise MVP plan with progress indicators and a provider app registration guide.
- Record the owner's requirement to run tests and other checks only when explicitly requested.
- Add local Google and Microsoft provider app setup commands.
- Store public provider IDs separately from credentials and protect the Google Desktop client secret with the operating system.
- Add interactive multi-account sign-in, mail/calendar scope choices, protected Google token slots and a protected MSAL cache.
- Add local account removal and SQLite schema version 2 for non-secret granted read categories.
- Add compact cross-account Gmail/Microsoft mail search and bounded selected-message reading.
- Add Google/Microsoft calendar discovery, combined agenda search and bounded appointment details.
- Handle nullable Microsoft event location and online-meeting fields during read-only detail retrieval.
- Add an opt-in real-provider runner that enumerates connected accounts dynamically and stays outside CI.
- Keep provider identifiers behind short in-memory references and report partial account coverage.
- Extend dependency notice generation to support legacy NuGet license metadata.

The current Windows source passed 68 .NET tests and 24 manual-runner regressions. Both the development and published executables passed 27 real-provider checks across four accounts, with three skipped event checks per run. The package from `de1fae7` passed smoke checks before and after extraction, and an update preserved all four accounts. Clean Windows installation remains. macOS and Linux are outside current runtime validation.

## 0.1.0-alpha.1 — Foundation

- Establish the .NET 10 solution and separate application, storage, security, provider, MCP and CLI modules.
- Add CLI discovery commands and working `get_status` / `list_accounts` MCP tools over stdio.
- Add SQLite metadata persistence, schema checks and account isolation tests.
- Add CI, portable release automation and protocol smoke tests.
- Document product scope, OAuth/token design, compact search contracts and Codex setup.
- Add the MailMeUp brand guide and generated concept artwork.
- Include Google Calendar and Microsoft appointments in the architecture and roadmap, with separate capability readiness.
- Make the read-only scope explicit in status output, the README and concise product documentation.

Account authentication, credential storage implementations, mail and calendar operations were not included in this foundation release.
