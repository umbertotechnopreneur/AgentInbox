# Validation

## Inbox-scoped unread search — source increment

**2026-09-14 — not built, tested or installed.** Shared search contracts now carry an Inbox-only scope. MCP `search_unread_mail` defaults to this scope; general and date-range tools retain broader defaults and expose the same option. Gmail applies `INBOX` before preview retrieval, and Microsoft selects the Inbox collection. Results disclose `inbox_only`, and continuations bind to the selected scope. Date and unread filters still apply; all Inbox categories and senders remain eligible.

Synthetic regression sources cover provider request URLs, Inbox/date/unread combinations, messages carrying both Inbox and custom labels, archived and moved-message exclusion, broader opt-out, MCP defaults and reported scope, and continuation scope mismatch handling. They have not been compiled or executed.

No tests, builds, formatting, smoke checks, provider reads, installation or CI dispatch were performed for this increment. Earlier source, installed-package and test records below are historical evidence and do not establish validation of this change. Microsoft Inbox continuations currently require the same well-known Inbox collection path; a provider response that substitutes a canonical folder-ID path is rejected. Confirm real continuation URLs during owner-requested provider validation before broadening accepted paths. Published-executable and live-provider checks require an owner request.

## Read-limit editor and local usage — source increment

**2026-09-13 — not built, tested or installed.** The Windows Sharing source adds read-budget settings, aggregate usage, manual local refresh, protected drafts and restart guidance. Core/Storage expose validated settings management with an expected-settings comparison and atomic save, plus read-only ledger snapshots. Application/decorator/DI wiring, isolated demo state and MCP status fields are included. Status remains available with an exhausted or small content budget; usage never claims model-token measurement.

Synthetic regression sources cover local status without first-run writes, rolling counts/expiry times, saved versus active settings, concurrent save conflicts, preservation of the usage ledger, invalid/cancelled operations, application delegation, demo isolation and MCP privacy/budget behavior. These cases have not been compiled or executed. No tests, builds, formatting, provider reads, UI launch/control, installation, push or CI dispatch were performed. Installed `0.1.1.20` and earlier 291-test evidence do not contain or validate this increment. Native editor behavior and separate-process restart scenarios remain to be exercised when requested.

## Provider and output guardrails — source increment

**2026-09-12 — not built, tested or installed.** Shared profile attempt/read/output ledgers, provider leases and cooldowns, Microsoft 429 retries, adaptive mail hydration, detail/folder caches, smaller default details and Gmail MIME fixes are implemented in source. Synthetic regression sources cover admission/output caps, profile coordination and corruption, retry/cooldown behavior, cache reuse/cancellation/sharing, attachment parsing and resumable search budgets. They have not been compiled or executed.

No tests, builds, formatting, smoke checks, provider reads, UI interactions, installation, push or CI dispatch were performed for this increment. Earlier 291-test results and installed MSIX `0.1.1.20` do not validate or contain these changes. Separate-process runtime enforcement, packaged startup, real provider quota behavior and MCP client ingestion remain to be exercised when requested. See [read guardrails](READ_GUARDRAILS.md).

## Windows installation with CLI screen navigation

**Windows x64, 2026-09-12 — package 0.1.1.20.** Desktop and CLI Release publication, MSIX creation and signing succeeded using the existing publisher `CN=umber`. After the installation tool response was interrupted, an independent `Get-AppxPackage` read confirmed installed version `0.1.1.20`, status `Ok`, and the matching WindowsApps location. The signed MSIX has a valid signature. Installation was not repeated; certificate trust and account configuration were not changed.

This supersedes installed `0.1.1.19` and includes the UI/CLI increment described below. No tests, smoke suite, mailbox checks or native UI interactions were repeated for the install request. The 291-test and CLI/MCP smoke evidence below belongs to the preceding local preview. Build/signing output is in the local ignored `artifacts/msix-build-0.1.1.20.log`. No release was published and no PR was changed.

## CLI screen navigation and isolated desktop preview

**Windows x64, 2026-09-12 — local preview build.** Desktop and CLI Release publication passed. The output is in local ignored `artifacts/ui-preview/desktop`, with the matching executable under `cli/mailmeup.exe`. This increment was not packaged, installed or pushed; installed `0.1.1.19` and the earlier mail-search PR do not contain it.

The requested .NET suite passed **291 tests, zero failures and zero skips** after correcting an xUnit assertion-analyzer failure in a new synthetic test. Added cases cover CLI arguments and executable resolution, desktop activation parsing, demo data and session isolation, unsupported authentication/provider reads, and preference validation. Parser tests do not establish native activation or rendering. Evidence: local ignored `artifacts/test-ui-step-preview.log` and `artifacts/test-results/ui-step-preview.trx`.

The published CLI returned all four screens for `ui --list-steps --json` without creating its isolated data directory. The existing CLI/MCP smoke suite also passed against this executable using temporary data directories, including nine tools and empty reads. Evidence: `artifacts/smoke-ui-step-preview.log`; publication logs are `artifacts/ui-preview-desktop-build.log` and `artifacts/ui-preview-cli-build.log`.

No desktop window was launched or controlled, no real mailbox or credential store was exercised, and no Codex connection was changed. Native layout, focus, small-window behavior, existing-instance navigation, dialog deferral and save/discard interactions remain pending. The demo application uses synthetic `example.test` accounts and in-memory choices; its UI blocks sign-in, provider setup/read checks and real Codex actions.

## Windows update with bounded mail searches and Google recovery

**Windows x64, 2026-09-11 — package 0.1.1.19.** Desktop and CLI Release publication, MSIX creation and signing succeeded. The package uses the existing local publisher `CN=umber` and package family. After the installation tool response was interrupted, an independent package-state read confirmed version `0.1.1.19`, status `Ok`, and a valid package signature; installation was not repeated. It supersedes `0.1.1.18` and contains the search preferences, UI, provider date bounds and rate-limit recovery described in the earlier source record below.

The dedicated packaging script does not compile or run tests. No unit tests, formatter, smoke suite, live mailbox request, desktop launch or UI interaction check was executed for this build/install request. No certificate trust, provider permissions or account configuration was changed. New MCP processes use the installed update through the stable alias; this was not exercised as an alias smoke check. Build evidence is in the local ignored `artifacts/msix-build-0.1.1.19.log`; no private runtime logs or signed binaries are committed.

**Subsequent owner-requested tests, same day:** `dotnet test MailMeUp.slnx -c Release` compiled the synthetic regression project and passed **229 tests, zero failures and zero skips**. This includes default-window/persistence/cursor cases, Google retry/classification/pacing, Microsoft search bounds and MCP status/notifications. Results are in the local ignored `artifacts/test-results/mail-search-limits.trx`. Tests use synthetic data and do not establish real Google/Microsoft behavior or desktop interaction.

The existing synthetic CLI/MCP smoke suite also passed against the actual installed `0.1.1.19` executable, with exit code 0: CLI options, JSON, private stderr logs, nine MCP tools, empty reads, invalid references and a diagnostic-only first run. Every child process used a temporary isolated `MAILMEUP_DATA_DIR`; no real account or UI was accessed. Evidence: local ignored `artifacts/smoke-mail-search-limits-0.1.1.19.log`. This checked the packaged executable directly, not the Windows execution alias. No application source changes were needed after installation.

## Default mail period and request-limit recovery — source changes

**2026-09-11 — not built or installed.** An inspection of existing owner-triggered logs identified a Gmail 403 with `rateLimitExceeded` during a burst of detail reads; other reads succeeded immediately before and after it. The previous status-only classifier presented that response as denied access. This is diagnostic evidence from the earlier operation, not a new live-provider check.

The source now distinguishes request limits, shares per-account Google read pacing/cooldowns and applies bounded retries. Undated mail searches use a persisted 14-day default, configurable from 1 to 365 days in the Windows Sharing page. Search results expose effective dates; continuations retain their window and reject a changed default. Microsoft text/address searches receive date constraints inside KQL in addition to local result filtering. MCP status exposes the saved period without reading a mailbox.

Synthetic regression sources cover quota/permission distinctions, retry limits and cancellation, shared per-account pacing, preference persistence and corruption, date overrides, frozen/invalidation cursor behavior, deterministic fixture dates, Microsoft UTC date boundaries and MCP status disclosure. They have not been compiled or executed. No tests, builds, formatters, smoke checks, mailbox checks, UI launch, packaging, installation or CI dispatch were run for these edits. The installed `0.1.1.18` preview does not contain them. Desktop rendering/save/discard and real-provider compatibility remain pending owner-requested validation.

## Owner-requested plugin mail reads after the Windows update

**2026-09-07 and 2026-09-08.** After installing `0.1.1.18`, owner-requested reads through the local MailMeUp plugin listed all five shared accounts and returned unread-mail search results with `coverage_complete: true` and no failed accounts. The subsequent review followed a search continuation, queried individual accounts and read selected message bodies successfully. Empty unread results from individual accounts also completed without errors. Reads did not mark messages as read or modify provider data. No account identities or message content are included in this record.

These observations validate the exercised plugin search, continuation and selected-detail paths, not every mailbox query, the desktop read-access button, calendar coverage or credential recovery. The newly added synthetic regressions remain unexecuted; no additional tests or builds were run for the commit/push request.

## Windows update with Gmail response handling and Codex setup

**Windows x64, 2026-09-07 — package 0.1.1.18.** The desktop and CLI Release publications, MSIX creation and signing completed successfully. The upgrade from `0.1.1.17` was confirmed independently: Windows reports installed version `0.1.1.18`, status `Ok`, and a valid package signature. The installation tool response was interrupted, but the subsequent package-state query confirmed completion; installation was not repeated.

This package includes the Gmail list-only HTTP 204 handling, removal of the five-calendar sampling cutoff and the actionable Codex configuration screen described below. Tests, formatting, smoke checks, live mailbox checks and automated UI inspection were not run for this build/install request. The added regression tests were not built by the packaging script. Runtime behavior after the update remains to be exercised; build and installation do not establish mailbox access or a successful Codex connection. The source-only records below describe the state before this package.

## Actionable Codex setup — source changes

**2026-09-07 — not built or installed.** Connect to Codex now renders separate observations for the Windows alias, native CLI, local marketplace, plugin and direct MCP registration. An added marketplace does not imply an installed plugin. Inspection attempts each Codex configuration query independently, exposes safe exit-code/unsupported-response/timeout outcomes, and keeps installation blocked when required configuration is unknown. Guidance is specific to the detected state; retries and cancellation clear old success. Partial installation does not reuse pre-installation observations as current results.

Synthetic parser and presentation cases were added for marketplace-only setup, missing/unsupported marketplace responses, conflicting sources, plugin enablement, direct/plugin coexistence and unknown/interrupted states. These tests and source edits have not been compiled or executed. No CLI prompt, live configuration query, provider check, formatter, build, packaging or installation was run for this increment. Native UI rendering, clipboard interaction and cancellation still require owner-approved validation. The owner's screenshot confirms manual addition of the local marketplace, not plugin installation or a successful MCP connection.

## Gmail no-content responses and calendar sampling — source changes

**2026-09-07 — not built or installed.** The source now normalizes HTTP 204 only for Gmail message-list reads, includes the result-size estimate in that list's field projection, and rejects no-content responses for required JSON reads. Empty/malformed HTTP 200 responses and HTTP errors remain failures. The local five-calendar cutoff was removed; checks iterate discovered calendars within the existing deadline and can follow bounded empty event pages to a sample. Logs report actual calendar coverage.

Synthetic regressions were added for explicit list-only no-content handling, empty/malformed HTTP 200 responses, error statuses, caller cancellation, checking a sixth calendar, a failure or absent sample on that calendar, continuation limits and the calendar deadline. These new changes and tests have not been compiled or executed. No formatting, smoke, live-account checks, package build or installation was run for this follow-up. Version `0.1.1.17` remains the last installed package; its historical evidence below does not validate this source fix.

## Correlated diagnostics, real read checks and minimal account UI

**Windows x64, 2026-09-07 — package 0.1.1.17.** The solution build, Windows desktop build, Release publications, MSIX creation and signing succeeded. The Windows build reported zero warnings and errors. The installed package reports version `0.1.1.17`, status `Ok`, and the MSIX signature is valid. The new synthetic diagnostic/privacy and read-check regressions compiled; unit tests, formatting checks, smoke tests and automated real-provider checks were not run for this increment. Post-install rendering/interaction of the minimal rows remains unverified.

The owner exercised the preceding `0.1.1.16` check through the app. Local logs at 16:03 and 16:04 (+07:00) show two Google unread/date searches receiving HTTP 204 and failing JSON parsing, then being classified as `Unknown`. Their earlier general search and message-detail requests returned HTTP 200. This response-handling issue was unresolved in `0.1.1.17`; reconnecting is not supported as its cause or cure by this evidence. Another Google calendar check hit the application's five-calendar sampling cap (`ResultLimit`), not a sign-in failure. Both Microsoft accounts passed mail sample reads; their calendar checks completed with `SearchOnly` evidence because some checked windows had no detail sample. These are owner-triggered observations, not an automated validation run or proof that all reads work.

Version `0.1.1.17` retains detailed diagnostic outcomes but removes verbose status rows and check-result banners. Missing samples and sampling limits are not shown as broken connections. Rows show only the owner-requested red **Try to reconnect** action for read failures, with normal account actions in a menu. Provider data, credentials and account identities were not copied into this validation record.

## Microsoft mail search regression and real-account verification

**Windows x64, 2026-09-07 — package 0.1.1.15.** Microsoft text searches previously combined `$search` and `$filter` and failed on both real Microsoft accounts while unread/date listings and Calendar worked. Three new query-construction regression cases failed on the previous code. After separating the query modes, all **122 .NET tests passed**, including nine new cases for query compatibility, local exclusions/filters, continuation through empty filtered pages and HTTP 400 classification.

The CI-equivalent formatting check, Release build, CLI/MCP smoke, dependency inventory and repository preflight passed locally. The newly published native CLI also passed isolated synthetic smoke. Signed MSIX creation and installation succeeded; installed version `0.1.1.15` reports `Ok`, with a valid signature.

With the owner's explicit permission, the installed executable passed **34 real-provider checks, zero failures and three skips**, across three Google and two Microsoft accounts. Microsoft text search, bounded message detail, continuation and mixed-account search all passed. The run covered five mail samples, four event samples and 16 calendars. One Microsoft calendar had no events in the 30-day window (detail and continuation skipped); the other had no next event page (continuation skipped). No mailbox identities, credentials or content were printed. Provider data was not modified; normal protected token refresh and local diagnostics remain possible.

The standalone published executable separately passed 27 checks, zero failures and three skips against its four visible accounts. Its default runtime data context differs from the installed executable's five-account context, so the installed run is the verification for the owner's app. These are bounded summary/detail consistency checks, not an independent comparison with Outlook/Gmail. Clean-machine installation, deliberate revocation/reconnect and post-install desktop interaction remain untested. Remote CI results for this fix are recorded on the PR, separately from these local results.

## Account recovery, CI smoke and package icon

**Windows x64, 2026-09-07 — package 0.1.1.14.** The CI-equivalent `scripts/validate.ps1 -SkipUnitTests -CheckFormatting` command passed formatting, Release build, CLI/MCP smoke, dependency inventory and repository preflight. The build completed with zero warnings and zero errors. The smoke test permits only the documented rolling files below `logs`; database, configuration, credential and any other first-run state still fail validation.

The desktop and CLI publications, signed MSIX packaging and local upgrade installation completed successfully. The installed `MailMeUp.Desktop_0.1.1.14_x64__kqhwqwq9w6r3m` package reports status `Ok`, and its package signature is valid. The 44-pixel package asset now has a measured nontransparent visual extent of 38 by 37 pixels, up from the more heavily padded source. Unit tests, live provider reads and post-install visual inspection of the Windows app list were not run.

## Serilog diagnostics — build and installation

**Windows x64, 2026-09-07 — package 0.1.1.12.** The Release desktop and CLI publications, signed MSIX packaging and local upgrade installation completed successfully with the Serilog diagnostics included. The installed `MailMeUp.Desktop_0.1.1.12_x64__kqhwqwq9w6r3m` package reports status `Ok`.

No tests, MCP smoke run or live provider read were run for this increment.

## Connection check and wizard — build and installed package validation

**Windows x64, 2026-09-06 — package 0.1.1.10.** The installed update adds the explicit **Check connections** action to the Acrylic wizard. It verifies each local Google/Microsoft account even when sharing is off, silently refreshes OAuth/MSAL access where possible, checks enabled Mail and Calendar services with minimal requests, and discards every response body. It reports safe categories only.

- **113 .NET tests passed**, with zero failures or skips, in Release configuration. The two new synthetic tests cover checking all local accounts, safe sign-in-required handling, and an unavailable provider checker. This suite covers shared application behavior; it does not exercise the desktop UI.
- The Desktop Release build passed with zero warnings and errors.
- Self-contained Desktop and CLI publication, MakeAppx packaging and signing succeeded. Build log: `artifacts/msix-build-0.1.1.10.log`.
- Authenticode reported a valid package signature. `MailMeUp.Desktop_0.1.1.10_x64__kqhwqwq9w6r3m` replaced `0.1.1.9` and reports status `Ok`. The existing publisher and signing certificate were retained; no trust-store changes were made.
- A launch of the installed desktop executable with an isolated synthetic `MAILMEUP_DATA_DIR` remained running with a synthetic existing account registry. The process was then closed. This checks package startup, not visual rendering, interaction or live provider access. Result: `artifacts/smoke-desktop-0.1.1.10.log`.
- The published package CLI and installed `%LOCALAPPDATA%\Microsoft\WindowsApps\mailmeup.exe` alias both passed the CLI/MCP smoke suite, including nine tools, bounded error notifications, redirected output and a stateless first run. Results: `artifacts/smoke-cli-0.1.1.10.log` and `artifacts/smoke-alias-0.1.1.10.log`.

At the owner's request, no Computer Use automation or desktop visual inspection was performed; the owner will review the rendering. The button has not been clicked against a real account, so silent refresh, expired/revoked consent, network errors, status display and reconnect flow remain live-validation work. Future automated runtime checks must use synthetic `example.test` accounts and `MAILMEUP_DATA_DIR` for isolation. No real mailbox checks, remote CI, release tags or publication were performed for this update.

## Windows onboarding changes

**Windows x64, 2026-09-06 — package 0.1.1.4.** The owner reported that the installed `0.1.1.3` setup window terminated immediately when its existing account registry was opened. Windows Error Reporting recorded `0xC000027B` in `Microsoft.UI.Xaml.dll`; the crash dump placed the managed thread in the Microsoft.Data.Sqlite static application-data probe before the first database connection.

- **111 .NET tests passed**, with zero failures or skips, in Release configuration. This includes persisted sharing, revoked references/cursors, in-flight restrictions, safe error categories and 20 MCP adapter notification cases. Results: `artifacts/test-results/mailmeup-tests.trx`.
- Runtime account storage now calls SQLitePCL directly while retaining schema version 2 and existing database compatibility. The nine focused account-store tests passed, including concurrent initialization, updates, deletion and unsupported-schema rejection.
- A new desktop startup smoke creates only a synthetic `example.test` account database. It reproduced the installed `0.1.1.3` crash with exit code `3221226107`, then the published and installed `0.1.1.4` executables stayed open for their observation periods. No new MailMeUp crash event was recorded for the installed fixed build.
- Self-contained Desktop and CLI publication, MakeAppx packaging and signing completed successfully. Build log: `artifacts/msix-build-0.1.1.4.log`.
- The published `payload/cli/mailmeup.exe` passed the CLI/MCP smoke suite, including nine tools, bounded error notifications, redirected output and a stateless first run.
- `MailMeUp.Desktop_0.1.1.4_x64__kqhwqwq9w6r3m` replaced `0.1.1.3` and reports status `Ok`. The existing local signing certificate was already trusted; no trust-store changes were made.
- The same smoke suite passed through `%LOCALAPPDATA%\Microsoft\WindowsApps\mailmeup.exe`, preserving the alias path rather than resolving its reparse point.
- The `0.1.1.3` window and About & Support dialog were inspected visually before the startup regression was reproduced with an existing registry. The banner, English text, website/repository/support/star links and installed version appeared; the copy button returned the expected version/platform summary.

All current automated reads used synthetic `example.test` data or empty temporary directories with `MAILMEUP_DATA_DIR`; the owner's account registry and mailboxes were not used. Browser links were not opened. Live UI sign-in, Codex plugin installation, concurrent alias callers, clean-machine deployment and ARM64 runtime behavior remain untested.

Earlier the same day, preview `0.1.1.2` was built and signed without installation or runtime tests. Compilation errors in the initial WinUI controls and a required alias manifest attribute were corrected before that first successful package build. The shared Hosting project and separate MSIX dependency graphs are included in the current source.

**Windows x64, 2026-09-05. Pre-alpha and read-only.** Local automated checks use synthetic `example.test` data and isolated storage. Authorized live reads use local protected credentials; reports contain counts and outcomes only.

## Earlier CLI local results

- Locked restore, formatting and Release build passed with zero warnings or errors.
- **68 .NET tests passed:** storage, account isolation, failed reconnect, selective removal, transactional MSAL cache updates, credential locking/cancellation, partial reads, continuation limits and calendar boundaries.
- Windows protected credential I/O and separate-process lock contention passed with synthetic data. Timeout tests cover cancellation classification; they do not wait for the actual 30-second deadline.
- **24 manual-runner regression tests passed**, without starting MailMeUp or accessing real accounts. They cover CI refusal, 0/1-account rejection, dynamic 2/3/4-account runs, individual failures, missing consent, empty calendars, batching and sanitized output.
- CLI/MCP process checks passed for the seven original read-only tools, empty first-run reads, invalid references, JSON and private diagnostics on stderr.
- The later unread/date-range mail tools, structured filters and default Spam/Junk plus Trash/Deleted exclusions are not covered by the validation runs recorded here.
- Dependency inventory and repository data/link checks passed.

## Earlier CLI real-provider results

Both the development executable and the published Windows x64 executable from `de1fae7` passed **27 checks, with 0 failures and 3 skips** per run, across **two Google and two Microsoft accounts**:

- Four bounded mail summary/detail samples and mail continuation reads.
- Seven calendars, mixed-provider searches and three bounded event summary/detail samples.
- One Microsoft account had no events in the checked window: its event detail and continuation checks were skipped. Another had no next event page, so that continuation check was skipped.
- All four accounts remained available across new processes. No identities, provider IDs, message/event content or credentials were printed.

These checks compare summaries with fetched details. They do **not** independently compare results with Gmail, Outlook or calendar UI screens. No real account was removed or its grant revoked to simulate faults.

The manual runner is local-only: `python scripts/real-provider-check.py <path-to-mailmeup>`. It enumerates accounts, requires at least two and refuses recognized CI environments before process startup. Live checks are excluded from CI.

At the owner's request, CI also skips unit-test execution. It retains build, read-only formatting verification, isolated protocol smoke and repository checks. Local `scripts/validate.ps1` applies style fixes before building and runs the unit suite unless `-SkipUnitTests` is supplied. The repository pre-commit hook applies the same formatter to staged C# files.

## Packaging and CI

The Windows x64 package from `de1fae7` passed native smoke checks before and after ZIP extraction, then the real-provider checks above. Account metadata matched exactly between the previous `971a5b8` executable and this build: all four accounts were preserved and usable. Diagnostics checks used synthetic data and printed no credentials or private content. This was an update on the development workstation; a clean Windows installation remains untested.

Earlier foundation CI produced six portable packages; see [the recorded run](https://github.com/umbertotechnopreneur/MailMeUp/actions/runs/33943689252). That historical run does not validate the current source. Current-turn results above are local, not remote CI results.

Windows ARM64 was published previously but not executed on hardware. **Real macOS and Linux flows are not tested:** no machines are available, and the current MVP makes no support claim for them.

## Still to check

- Deliberate real expiry, revoked access, reconnect and lost connectivity.
- Independent known-result comparison, including recurring/cancelled events, all-day dates and time zones.
- Clean Windows installation and independent pilot use.

Google remains External in Testing. No provider-write scopes, releases or release tags were added.
