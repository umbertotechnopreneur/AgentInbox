# Validation

Last updated: 2026-10-19.

## Local Python tests

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
