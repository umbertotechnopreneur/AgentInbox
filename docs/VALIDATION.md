# Validation

Last updated: 2026-10-20.

## Local refactoring validation (2026-10-20)

The local refactoring, including the account-check files that had not yet been committed, passed all **90 synthetic Python tests** on Windows with Python 3.13.15 in 1.949 seconds. The command was `python -B -m unittest discover -s python/tests -t python -v`, with isolated data, test and temporary directories, no user-site imports and no bytecode. Network and credential boundaries were mocked; no real mailbox was used.

Debug MSIX **1.0.12.0 x64** was built locally from that working-tree source with the unchanged, previously scanned official Python and NuGet inputs. Offline restore and WinUI publish succeeded. The unsigned package is 104,681,573 bytes; its SHA-256 was checked against the generated checksum. Matching Debug x64 `dotnet clean` and removal of the unique packaging work directory completed without a warning. No dependency download, import scan, signing, installation or desktop control ran.

The extracted package's bundled Python runtime passed `-B -m agentinbox --help` and `-B -m agentinbox manage` with an isolated `list_accounts` request and an empty account list. This checks packaged CLI and management startup, not the installed execution alias, MCP startup, WinUI behavior, native credentials or live providers.

Automatic approval review rejected the checked cleanup command with `blocked by policy` before execution. No alternate removal method was attempted. The owner then confirmed a separate cleanup, and `artifacts/` was verified empty: the package, checksum, import staging and original local evidence are no longer retained. The observed test/build results are recorded above; a short replacement result record is under ignored `artifacts/verification/main-consolidation-ci-20261020/`. Twenty ignored legacy `src/*/bin` and `src/*/obj` directories still contain about 2.3 MiB; their paths are recorded there in `legacy-output.json` and cleanup remains open.

The owner selected the new `Python source tests (Windows)` check as the required CI for this replacement, retaining pull requests and conversation resolution. Earlier .NET CI results do not validate the Python replacement. Linux and macOS remain source-only and unvalidated.

The first replacement CI run stopped before tests because PATH resolved `python.exe` to a WindowsApps execution alias that could not be hashed. Runtime selection now excludes those aliases, checks for a complete standard library and considers preinstalled Python locations and the runner tool cache. The revised preparation step passed locally without downloads or scans; the PR CI records the remote result.

## Local Debug MSIX 1.0.11.0 (installed)

Debug MSIX **1.0.11.0 x64** was built from the current working-tree source, signed with the existing current-user certificate and installed over 1.0.9.0. Windows reports version `1.0.11.0`, architecture `X64` and package status `Ok`. This package includes the account-check flow and owned Mica window described below. No automated, UI or live-provider tests ran as part of this build and installation.

The build reused the unchanged official Python 3.13.15 and NuGet inputs from the 1.0.10.0 build, including their existing first-import scan evidence. No new dependency import or antivirus scan was needed. The packaging script completed matching Debug x64 `dotnet clean` and removed its unique build-task directory without a cleanup warning.

The retained installer is 104,700,379 bytes, with SHA-256 `ac3a2e6c81b30c9f3a65ae14d57865fe30611c06ad2c18451db2d5f01c67e0ed`. The MSIX, checksum, package record and successful installation record are under ignored `artifacts/msix/debug/x64/1.0.11.0/`.

Automatic approval review rejected the checked removal of the superseded `artifacts/msix/debug/x64/1.0.10.0/` directory with `blocked by policy`. About 100 MiB remain there. No alternate deletion method was attempted. Earlier blocked input-staging and test-log cleanup recorded below also remains pending.

## Current account-check source

On 2026-10-19, all **90 synthetic Python tests passed** on Windows with Python 3.13.15 in 1.701 seconds. The full suite used `python -B -m unittest discover -s python/tests -t python -v`, disabled user-site imports and bytecode, and isolated profile and temporary paths under ignored `artifacts/verification/account-checks-20261019/`. Network and native credential boundaries were mocked. No real mailbox, message or credential was used.

The thirteen new cases cover unsaved checks without persistence, changed-host credential isolation, stale revisions, opt-in progress records, exact quoted/literal folder names, POP3's single INBOX, optional sample reads versus empty folders, invalid input types, failed TLS, rejected IMAP sign-in and rejected SMTP sign-in. A sample-read result returns no message content. Existing transport, approval, storage and cleanup tests also passed.

The WinUI source now includes vertically centered account tab headers, a visible current-form check, an owned Mica progress window, explicit server-folder selection, optional sample reading and a link to the separately approved test-email flow after a confirmed save. **This WinUI source is compiled, packaged and installed in Debug 1.0.11.0; it has not been exercised on the live desktop.** Earlier UI evidence below does not validate these changes. No antivirus scan or external import was needed for this test run.

The suite removed its disposable profiles. Automatic approval review rejected removal of the remaining `tests.log` (16,294 bytes) and empty `profiles/` and `tmp/` directories with `blocked by policy`. The ignored task directory retains these and the small `result.json` record, including tested source hashes. No other deletion method was attempted; final transient cleanup remains pending.

## Earlier local Python tests

**77 tests passed, with no failures or errors**, on Windows using the existing Python 3.13.15 installation. The suite completed in 1.659 seconds on its first run; no test or implementation change was needed to obtain this result.

After a targeted Defender scan of the runtime directory completed with exit code 0 and no detections, the following command ran from the repository root:

```text
python -B -m unittest discover -s python/tests -t python -v
```

The run disabled user-site imports and bytecode output. `AGENTINBOX_DATA_DIR`, `AGENTINBOX_TEST_ROOT` and temporary-directory variables pointed inside a fresh task directory under ignored `artifacts/verification/`. Disposable profiles and unnecessary logs were removed after use; minimal scan and result records remain there.

Tests exercise real application/storage behavior with synthetic `example.test` accounts and fake external boundaries. Coverage includes:

- Account permissions, revision changes, strict input types and same-process management sessions.
- Immutable review, approval reuse, attachment snapshots, Bcc handling and Unicode MIME.
- SMTP envelope decisions, interrupted or uncertain submission, and bounded/malformed replies.
- IMAP literal, line, aggregate-response and deadline limits.
- POP3 UIDL identity, bounded header/body reads, unsupported filters and incomplete date coverage.
- Microsoft device-flow pacing, scope selection, refresh rotation, stale revisions and consumed flows.
- Atomic settings limits, cleanup failures, durable retry references and preservation of committed outcomes.
- Windows Credential Manager/DPAPI routing with mocked native functions.

These results do not prove network interoperability, native credential protection, GUI behavior or packaged startup.

After the Microsoft wording changes, the same 77 synthetic tests passed again on 2026-10-19 in 1.701 seconds. They used an isolated task profile; its empty scratch directory was removed afterward. No provider or live desktop was involved.

## Local Debug MSIX 1.0.10.0 (not installed)

On 2026-10-19, Debug MSIX **1.0.10.0 x64** was built from current source commit `7cd237c`, signed with the existing current-user certificate and verified with `Get-AuthenticodeSignature` (`Valid`). The MSIX is 104,688,604 bytes and has SHA-256 `1efce56c10a43df9516e8188abdc9a1ed936cf2d51d88359e0284c9d8fd5741f`. The package, checksum and package record are under ignored `artifacts/msix/debug/x64/1.0.10.0/`.

The build used .NET SDK 10.0.402, the official Python 3.13.15 embeddable archive and 23 NuGet packages obtained from the official feed. The Python archive hash and every NuGet archive hash were checked against the staged manifest. Before use, targeted Defender scans of the archive-only Python folder, extracted Python folder and NuGet archive/extraction folder completed without detections. A direct scan of the Python ZIP had reported `skipped`; it was not accepted as evidence, so the folder containing only that ZIP was scanned separately. The commands, sources, hashes and results are recorded in ignored `artifacts/inputs/1.0.10.0-source-20261019/first-import-scans.json`. The package build itself started no antivirus scan.

Offline restore, WinUI publish, MSIX creation, signing and matching `dotnet clean` succeeded. A read-only package check found the `1.0.10.0` manifest, current Python source wording and bundled runtime. This is a local Debug package, not a Store build. It was **not installed**: Windows still reports AgentInbox 1.0.9.0 X64 with status `Ok`. The new UI, packaged CLI/MCP and live mail providers were not exercised.

The packaging script removed its task-owned `artifacts/work/msix-*` build directory. The earlier ignored artifact tree was empty when this task began. This task retains the compressed official inputs for reproducibility: 23 NuGet archives (481.1 MiB), the Python ZIP (10.5 MiB), manifest and scan evidence. Cleanup of the unneeded staging copies was **blocked by automatic approval review** with `blocked by policy`, both for recursive removal and a checked file-by-file removal of the `restore/` directory. Removal of empty `work/` and `verification/` parents was also rejected. The remaining unneeded task-owned paths are `artifacts/inputs/1.0.10.0-source-20261019/nuget/` (1,077.6 MiB), `restore/` (481.4 MiB) and `python/` (20.3 MiB). No other deletion mechanism was used; cleanup is incomplete.

## Local Debug MSIX 1.0.9.0

On 2026-10-19, Debug MSIX **1.0.9.0 x64** was built locally from commit `1eaf1c5`, signed with the existing current-user certificate and installed over 1.0.8.0. Windows reports version `1.0.9.0`, architecture `X64` and package status `Ok`.

The build used .NET SDK 10.0.402, embedded Python 3.13.15 and the pinned WinUI dependencies. Offline restore, WinUI publish, signing and installation succeeded. No antivirus scan was started by this build.

At the time, its installer was 104,641,178 bytes, with SHA-256 `60332d079f5e1318f6a4dffd1ab5570cdbcdd76169172968593607316e68a75e`. Matching `dotnet clean` and packaging task-directory cleanup completed. Its ignored installer and evidence files were no longer present at the start of the 1.0.10.0 task.

## Stock TabView source build (not installed)

On 2026-10-19, the account editor was changed to use the WinUI 3 TabView theme and default sizing without local tab resources. Its three tabs use native leading icons without forced colors. An isolated offline Debug x64 restore and publish completed for that source. It reused unchanged imported packages and started no antivirus scan. The source preview did not include a new MSIX or a visual check; the later 1.0.10.0 package build is recorded above.

Earlier build failures were corrected as recorded in the changelog.

The earlier 1.0.6.4 build completed 48 scans without detections under the previous policy. The owner later replaced that policy with a one-time first-import scan. The previous input archives, scan record, installers and UI-build directories were no longer present at the start of the 1.0.10.0 task; their historical validation results above do not certify this task's new imports.

## Account UI regression checks

Version 1.0.8.0 passed build and installation but failed when opening Accounts. The owner reported the failure, and an isolated run reproduced it. A diagnostic build established that resolving the tab resource dictionary succeeded, but assigning that application-owned dictionary to `TabView.Resources` threw `ArgumentException` (`0x80070057`). This was a UI construction error, not a Python failure.

The corrected source creates each native TabView and its local resources through a DataTemplate. Actual Windows UI interaction verified:

- Empty state hides the account editor and Save/Cancel; Create explicitly opens the form, and Cancel restores the empty state.
- All three tabs change content and display a visible selected background and border.
- Gmail exposes password authentication only and fills its incoming server. Optional SMTP fields open while sending stays disabled; no credentials were entered or saved.
- A narrow window (approximately 814 pixels wide in the capture) stacks paired fields and header actions. Maximizing keeps the content at the top left and limits its width.
- Navigating away and back recreates the page. An intentionally incompatible synthetic profile produces a complete error page without partial form actions; explicit retry succeeds after correcting that test profile, and the editor opens again.

After installation, the packaged 1.0.9.0 executable was launched with its bundled runtime and an isolated `AGENTINBOX_DATA_DIR`. Visual interaction verified startup, empty state, explicit Create, the Incoming tab and Cancel. No unexpected setup error was logged. Its ignored diagnostic files were no longer present at the start of the 1.0.10.0 task.

These checks did not save accounts or credentials, send mail, test live providers, exercise every keyboard path, or verify light/high-contrast themes. At that time, the earlier Python and guided-setup checks were not repeated because their implementation did not change.

## Guided Codex setup checks

Nine local checks passed against the installed Codex CLI with a synthetic temporary `CODEX_HOME`: detection, registration, preservation of unrelated settings/comments, unchanged-connection recognition, replacement detection, refusal without confirmation, confirmed replacement, rejection of a stale detection snapshot and temporary-profile cleanup.

The final run completed in 0.88 seconds after the last service change. It did not start a mail server, change the owner's Codex configuration or claim a verified MCP connection. The temporary profile was removed and matching `dotnet clean` completed. Its ignored harness and results were no longer present at the start of the 1.0.10.0 task. WinUI interaction and real client connection still need separate validation.

## Not yet validated

- Actual Windows Credential Manager and DPAPI calls; macOS Keychain and Linux Secret Service paths.
- Real IMAP/POP3 connections and non-marking/non-deleting behavior with providers.
- Microsoft app registration, live consent, tenant policy and token refresh.
- Real SMTP authentication, rejection/acceptance and recipient delivery.
- Account saving/editing with native credentials, complete keyboard access, light/high-contrast themes and native send approval.
- CLI/MCP process startup, installed aliases, default shared profile and configuration after package updates.
- Debug MSIX creation, signing and installation on Windows ARM64.
- Local Store Release packages, package validation and submission.
- Linux/macOS execution and external MCP-client integration.

The CI source defines isolated Windows synthetic tests without build-time antivirus scans. No remote CI result is claimed. The optional Actions packaging workflow creates Debug artifacts only; the local Store recipe remains unvalidated.

Source inspection, synthetic tests, installed startup, authentication, server acceptance and recipient delivery are separate evidence. Results from the previous engine do not validate this implementation.
