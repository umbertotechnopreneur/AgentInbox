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

## Local Debug MSIX 1.0.9.0

On 2026-10-19, Debug MSIX **1.0.9.0 x64** was built locally from commit `1eaf1c5`, signed with the existing current-user certificate and installed over 1.0.8.0. Windows reports version `1.0.9.0`, architecture `X64` and package status `Ok`.

The build used .NET SDK 10.0.402, embedded Python 3.13.15 and the pinned WinUI dependencies. Offline restore, WinUI publish, signing and installation succeeded. No antivirus scan was started by this build.

The retained installer is 104,641,178 bytes. The packaging checksum record reports this SHA-256:

```text
60332d079f5e1318f6a4dffd1ab5570cdbcdd76169172968593607316e68a75e
```

Earlier build failures were corrected as recorded in the changelog. Matching `dotnet clean` and task-directory cleanup completed for this successful build.

The earlier 1.0.6.4 build completed 48 scans without detections under the previous policy. The owner replaced that policy: scan new external imports once, reuse their evidence and never start antivirus scans from builds/tests or scan installed SDK/system trees. Evidence for the 22 unchanged imported archives and their extracted files is retained separately under ignored `artifacts/imports/first-import-scans-20261019.json`. Subsequent local builds follow the new rule; remote workflows remain unexecuted.

The requested MSIX, checksum, package record and installation record remain under ignored `artifacts/msix/debug/x64/1.0.9.0/`. Its matching `dotnet clean` and packaging-directory removal completed; `artifacts/work/` has no remaining build directories. Automatic approval review rejected the separate UI-test cleanup command before execution with `blocked by policy`. The `diagnostic`, `corrected` and `native-tabs` directories under `artifacts/ui-check-1.0.9/` remain at approximately 1,906 MiB each (5.58 GiB total), together with the small isolated profile and build script. Their matching clean was part of the rejected command and did not run. The block was not bypassed or retried.

Earlier blocked cleanup also leaves `artifacts/recovered-20261015/` (135.22 MiB), superseded installers 1.0.6.4 through 1.0.8.0 (about 100 MiB each), and the small `artifacts/debug/1.0.6.3/` record. The unchanged offline inputs under `artifacts/inputs/msix-1.0.6.4-9d8c3e21/` (526.78 MiB) were reused for this build. Cleanup is incomplete.

## Account UI regression checks

Version 1.0.8.0 passed build and installation but failed when opening Accounts. The owner reported the failure, and an isolated run reproduced it. A diagnostic build established that resolving the tab resource dictionary succeeded, but assigning that application-owned dictionary to `TabView.Resources` threw `ArgumentException` (`0x80070057`). This was a UI construction error, not a Python failure.

The corrected source creates each native TabView and its local resources through a DataTemplate. Actual Windows UI interaction verified:

- Empty state hides the account editor and Save/Cancel; Create explicitly opens the form, and Cancel restores the empty state.
- All three tabs change content and display a visible selected background and border.
- Gmail exposes password authentication only and fills its incoming server. Optional SMTP fields open while sending stays disabled; no credentials were entered or saved.
- A narrow window (approximately 814 pixels wide in the capture) stacks paired fields and header actions. Maximizing keeps the content at the top left and limits its width.
- Navigating away and back recreates the page. An intentionally incompatible synthetic profile produces a complete error page without partial form actions; explicit retry succeeds after correcting that test profile, and the editor opens again.

After installation, the packaged 1.0.9.0 executable was launched with its bundled runtime and an isolated `AGENTINBOX_DATA_DIR`. Visual interaction verified startup, empty state, explicit Create, the Incoming tab and Cancel. No unexpected setup error was logged. Small diagnostic and result records are under ignored `artifacts/verification/ui-1.0.9/`.

These checks did not save accounts or credentials, send mail, test live providers, exercise every keyboard path, or verify light/high-contrast themes. The earlier Python and guided-setup checks were not repeated because their implementation did not change.

## Guided Codex setup checks

Nine local checks passed against the installed Codex CLI with a synthetic temporary `CODEX_HOME`: detection, registration, preservation of unrelated settings/comments, unchanged-connection recognition, replacement detection, refusal without confirmation, confirmed replacement, rejection of a stale detection snapshot and temporary-profile cleanup.

The final run completed in 0.88 seconds after the last service change. It did not start a mail server, change the owner's Codex configuration or claim a verified MCP connection. The temporary profile was removed and matching `dotnet clean` completed. The small harness, result and build records remain under ignored `artifacts/verification/assistant-20261019-6d8a492c/` (0.43 MiB). WinUI interaction and real client connection still need separate validation.

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
