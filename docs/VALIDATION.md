# Validation

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
