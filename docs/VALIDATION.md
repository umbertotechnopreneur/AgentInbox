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

## Local Debug MSIX

On 2026-10-19, Debug MSIX **1.0.6.4 x64** was built locally from commit `27ca249`, signed with the existing current-user certificate and installed over 1.0.6.3. Windows reports version `1.0.6.4`, architecture `X64` and package status `Ok`.

The build used .NET SDK 10.0.402, embedded Python 3.13.15 and the pinned WinUI dependencies. Offline restore and WinUI publish succeeded. The first install attempt returned `0x80073D02` because the old app was in use; installation then succeeded with `ForceApplicationShutdown`.

The retained installer is 104,603,525 bytes. Its SHA-256 matches the adjacent checksum:

```text
918867374f3ab127efe09c8b4dae8f430ff504b8df905e1864a1b060e9675302
```

The build exposed and corrected output-path validation, a WinUI layout type error, stale `obj` source discovery, compiler-server cleanup locks and the console alias's multiple-instance declaration. Matching `dotnet clean` and task-directory cleanup completed for the successful build.

That build completed 48 targeted Defender scans without detections under the previous policy. The owner then replaced that policy: scan new external imports once, reuse their evidence and never start antivirus scans from builds/tests or scan installed SDK/system trees. Packaging and CI sources now follow that rule; those later script/workflow changes have not been executed remotely or through another package build.

The MSIX, checksum, package record and installation record remain under ignored `artifacts/msix/debug/x64/1.0.6.4/`. Automatic approval review rejected removal of `artifacts/inputs/msix-1.0.6.4-9d8c3e21/` (526.78 MiB), `artifacts/recovered-20261015/` (135.22 MiB) and the tiny `artifacts/debug/1.0.6.3/` record. Those paths remain; this is not a completed artifact cleanup.

No GUI, bundled CLI/MCP startup or live mailbox test was run as part of this installation.

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
