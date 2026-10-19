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

## Local Debug MSIX 1.0.7.0

On 2026-10-19, Debug MSIX **1.0.7.0 x64** was built locally from commit `2563b38`, signed with the existing current-user certificate and installed over 1.0.6.6. Windows reports version `1.0.7.0`, architecture `X64` and package status `Ok`.

The build used .NET SDK 10.0.402, embedded Python 3.13.15 and the pinned WinUI dependencies. Offline restore, WinUI publish, signing and installation succeeded. No antivirus scan was started by this build.

The retained installer is 104,632,407 bytes. The packaging checksum record reports this SHA-256:

```text
c86a2485dd3c1ed459aca242f92896e826818188177614f803614fd4b9e44d61
```

Earlier build failures were corrected as recorded in the changelog. Matching `dotnet clean` and task-directory cleanup completed for this successful build.

The earlier 1.0.6.4 build completed 48 scans without detections under the previous policy. The owner replaced that policy: scan new external imports once, reuse their evidence and never start antivirus scans from builds/tests or scan installed SDK/system trees. Evidence for the 22 unchanged imported archives and their extracted files is retained separately under ignored `artifacts/imports/first-import-scans-20261019.json`. Subsequent local builds follow the new rule; remote workflows remain unexecuted.

The requested MSIX, checksum, package record and installation record remain under ignored `artifacts/msix/debug/x64/1.0.7.0/`. The task-owned build directory was removed and `artifacts/work/` has no remaining build directories. Earlier automatic approval review rejected obsolete-artifact deletion with `blocked by policy`; that restriction was not bypassed or retried. Remaining obsolete paths are `artifacts/inputs/msix-1.0.6.4-9d8c3e21/` (526.78 MiB), `artifacts/recovered-20261015/` (135.22 MiB), the superseded installers under `artifacts/msix/debug/x64/1.0.6.4/` (99.76 MiB), `1.0.6.5/` (99.78 MiB) and `1.0.6.6/` (99.78 MiB), and the tiny `artifacts/debug/1.0.6.3/` record. This is not a completed artifact cleanup.

Static review covered the larger DPI-scaled initial window, standard WinUI tabs, provider-specific authentication choices and invalidated-choice handling. The engine's Microsoft-only endpoint validation was inspected and remains unchanged. No GUI, bundled CLI/MCP startup or live mailbox test was run as part of this installation. The earlier Python and guided-setup checks were not repeated for this UI change.

## Guided Codex setup checks

Nine local checks passed against the installed Codex CLI with a synthetic temporary `CODEX_HOME`: detection, registration, preservation of unrelated settings/comments, unchanged-connection recognition, replacement detection, refusal without confirmation, confirmed replacement, rejection of a stale detection snapshot and temporary-profile cleanup.

The final run completed in 0.88 seconds after the last service change. It did not start a mail server, change the owner's Codex configuration or claim a verified MCP connection. The temporary profile was removed and matching `dotnet clean` completed. The small harness, result and build records remain under ignored `artifacts/verification/assistant-20261019-6d8a492c/` (0.43 MiB). WinUI interaction and real client connection still need separate validation.

## Not yet validated

- Actual Windows Credential Manager and DPAPI calls; macOS Keychain and Linux Secret Service paths.
- Real IMAP/POP3 connections and non-marking/non-deleting behavior with providers.
- Microsoft app registration, live consent, tenant policy and token refresh.
- Real SMTP authentication, rejection/acceptance and recipient delivery.
- WinUI account setup, keyboard access, error feedback and native approval.
- CLI/MCP process startup, installed aliases, bundled runtime, shared profile and configuration after package updates.
- Debug MSIX creation, signing and installation on Windows ARM64.
- Local Store Release packages, package validation and submission.
- Linux/macOS execution and external MCP-client integration.

The CI source defines isolated Windows synthetic tests without build-time antivirus scans. No remote CI result is claimed. The optional Actions packaging workflow creates Debug artifacts only; the local Store recipe remains unvalidated.

Source inspection, synthetic tests, installed startup, authentication, server acceptance and recipient delivery are separate evidence. Results from the previous engine do not validate this implementation.
