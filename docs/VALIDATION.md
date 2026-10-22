# Validation

Last updated: 2026-10-22.

## Pending configuration-model refactor

Source checkpoint `3214c1c` separates account records, unsaved drafts, selection, validation, notices, Microsoft sign-in, connection-check progress and Codex setup state from native controls. Views bind to these models; only rendering, native lifecycle, focus, browser and clipboard adapters remain in the windows. Returning to Accounts preserves the draft rather than reloading over it. Windows loads server defaults from the same Python presets operation used by the CLI.

Python account/preset contracts are now explicit, and one process-local `SessionState` holds references, continuations and the access-token cache used by `MailApplication` and its Microsoft service. Reference expiry/capacity and persisted account/send contracts are unchanged. New synthetic cases describe isolation, bounded expiry and omission of private cache data from diagnostic representations; they have not been run.

Only source inspection was performed. No build, installation, tests, formatting, live UI interaction, MCP launch or provider operation ran for this refactor. Installed Debug 1.0.18.0 predates it; that package does not validate the new bindings or model lifecycles. Outstanding checks are in [todo](../.github/tasks/todo.md).

## Debug MSIX 1.0.18.0 — 2026-10-22

Microsoft sign-in logs now use collapsed native expanders in the account page and dialog. The sign-in code is centered with a Copy icon beside it; copying the code is separate from copying the redacted diagnostics. Built and signed locally from source checkpoint `1aa7491`, then installed through `install-debug-msix`. Windows reports version `1.0.18.0`, architecture `X64` and status `Ok`. No automated tests, application launch, UI checks or mail-provider requests ran in this task.

The owner's installed-app trace shows authorization pending, then HTTP 200 from the token endpoint, followed by the local permission rejection and temporary-data cleanup. It does not capture the returned scope values and does not establish which permission comparison failed. Source now accepts additional previously granted scopes for the same mail resource while requiring every requested mail permission; missing/wrong-resource cases include bounded, redacted required/returned details. Microsoft documents that earlier grants can be returned together. Synthetic cases cover extra grants, missing reading permission, no unintended sending, redaction and cleanup; they were not run. No live service request was made by this task.

The retained installer is 105,437,582 bytes, SHA-256 `e99d706bacc6df5ba9bba75ac564f619cfd6ed09ef7489a911b6de459bb544a3`, under `artifacts/msix/debug/x64/1.0.18.0/`, with checksum, package/installation records and import-reuse evidence. Earlier installers and inputs were already absent before this task. Python 3.13.15 and twenty NuGet archives were recovered from their official URLs, and each SHA-256 matched the previous 1.0.17.0 import record. These are unchanged imports covered by the earlier targeted first-import scans; no new scan ran. The first download orchestration attempt failed before downloading because PowerShell's parallel pipeline does not support `-ErrorAction`; the corrected attempt verified every archive before building.

Packaging reported no cleanup warning. Its exact task-owned directory, `artifacts/work/msix-edc4977956e941e487ec524b0dc8b382/`, was removed and verified absent. Automatic approval review rejected guarded removal of `artifacts/inputs/debug-1.0.18.0-20261022/` with `blocked by policy`; 502,715,034 bytes (about 479.4 MiB) remain. No alternate removal was attempted. The retained package folder contains the minimal import-reuse record independently of these archives.

## Debug MSIX 1.0.17.0 — 2026-10-22

Microsoft sign-in now saves valid pending account settings, keeps its button available, and streams bounded local details into the page and sign-in dialog. Required form fields use native descriptions, theme error borders and first-error focus. The browser address no longer uses a domain/path allowlist; a valid HTTPS address returned by the verified Microsoft endpoint is accepted. The reported address failure came from the former local guard; the exact rejected address was not captured and no real sign-in has been verified. Source cases were updated for the new address contract and private diagnostics.

The email field labels, sign-in email defaults, account/connection errors and Connect Codex presentation were also revised. The new Test connection action checks the saved launcher with MCP initialization and tools/list, without tools/call. It has a deadline, bounded replies, no retained child diagnostics and explicit process cleanup. No automated test, launcher execution, application launch, live UI check or provider operation ran during this build/install task.

Built and signed locally from source checkpoint `f426a32`, then installed through `install-debug-msix`. Windows reports version `1.0.17.0`, architecture `X64` and status `Ok`. The retained installer is 105,462,895 bytes, SHA-256 `cae508836aa8cd9c2cab16e9d6c2ee728b2f3b2f1ebabf218bd7b2a0f1a692e9`, under `artifacts/msix/debug/x64/1.0.17.0/`, with checksum and package/installation records. The build reused unchanged Python 3.13.15 and previously scanned NuGet inputs. No downloads or new scans were needed.

Packaging reported no cleanup warning. Its exact task-owned directory, `artifacts/work/msix-dae273938e574e698adb50c3b5081cf4/`, was removed and verified absent. Automatic approval review rejected guarded removal of superseded `artifacts/msix/debug/x64/1.0.16.0/` with `blocked by policy`; about 100.6 MiB remain. No alternate removal was attempted. Earlier cleanup blocks remain unresolved.

The owner's screenshot supplies a specific Microsoft refusal: `invalid_client`, `AADSTS70002`, with a requirement to mark the app as mobile. Microsoft's desktop configuration guide confirms the existing registration's public-client flow setting as the fix for that explanation. No registration was changed and no Microsoft service probe ran. See [Microsoft setup](AUTHENTICATION.md#sign-in-with-microsoft).

## Earlier implementation evidence

Provider-choice persistence, shared Microsoft reading/sending sign-in, local Microsoft error details, plain setup text, the Connect Codex page and approved VibeWare logo are included in installed Debug 1.0.16.0. This also includes the centered mailbox prompts, + action and redundant Codex-status removal. The owner requested installation for hands-on use and explicitly excluded automated tests. No live UI or provider checks ran in this build/install task.

The approved VibeWare source image was visually inspected and copied byte-for-byte. Source and repository copy have matching SHA-256 `84321fe9f77ccc083813e02c02ff3716ad50a45ed000b8663c5a2d416e2ba5af`; the original MIT notice is included. This verifies the asset copy, not the rendered WinUI footer. New synthetic cases for provider round trips, rejected metadata, retained secrets, Microsoft reply redaction and cleanup failures were not run locally. No Microsoft service request was made; the precise cause of the owner's sign-in refusal is still unknown.

The required Windows check on [PR #35](https://github.com/umbertotechnopreneur/AgentInbox/pull/35) ran 102 synthetic cases. Its [initial run](https://github.com/umbertotechnopreneur/AgentInbox/actions/runs/39653414220) failed during cleanup because the old-ledger test fixture left its SQLite handle open. The fixture now closes that handle explicitly before cleanup; application behavior and the installed 1.0.16.0 payload are unchanged by this test-only fix. Current check results are attached to the PR.

The configuration-only Windows redesign, direct MCP sending, provider guidance and bundled Microsoft app identity were first built from source and installed as Debug 1.0.15.0 x64. New synthetic cases cover direct dispatch, duplicate IDs, permission changes before DATA, BCC/Unicode/attachments, unresolved submissions, metadata failures and old-schema rejection. They run in the required PR check described above; local tests and visual checks remain unrun. Earlier approval-model test results do not validate this new sending path.

The source now removes local approval and pending-message storage. Historical tests of that former flow do not validate direct sending. Website Terms/Privacy source edits remain local and unpublished. Existing public product-policy URLs returned HTTP 200 with the correct page titles during this work; that check does not validate the edited policy content in production.

## Debug MSIX 1.0.16.0 — 2026-10-22

Built and signed locally from source checkpoint `2f72826`, then installed through `install-debug-msix`. Windows reports version `1.0.16.0`, architecture `X64` and status `Ok`. No automated tests, application launch, UI control or provider operations ran.

The retained installer is 105,449,345 bytes, SHA-256 `ec2df557aca2e5b2a3f5466daaea5cc17f5eb4a33bc33fc22956b80a28a7575f`, under `artifacts/msix/debug/x64/1.0.16.0/`, with checksum and package/installation records. The build reused unchanged Python 3.13.15 and previously scanned NuGet inputs. No downloads or new scans were needed.

Packaging reported no cleanup warning. Its exact task-owned directory, `artifacts/work/msix-d82447cdb9894b4183064b0465c3acca/`, was removed and verified absent. Automatic approval review rejected the guarded removal of superseded `artifacts/msix/debug/x64/1.0.15.0/` with `blocked by policy`; about 99.8 MiB remain. No alternate removal was attempted. Earlier cleanup blocks remain unresolved.

## Debug MSIX 1.0.15.0 — 2026-10-22

Built and signed locally from source checkpoint `a4cb4be`, then installed through `install-debug-msix`. Windows reports version `1.0.15.0`, architecture `X64` and status `Ok`. This package includes provider-specific guidance, automatic Microsoft OAuth selection and the public Microsoft client ID in desktop application settings. No application launch, UI automation, synthetic tests or provider operations ran; the recovered registration's device flow and mail permissions remain unvalidated.

The retained installer is 104,649,885 bytes, SHA-256 `a9f696a532e7eec8b412a98b7c87559fbf3cfe86bdaa43091a1bb2063f9a059d`, under `artifacts/msix/debug/x64/1.0.15.0/`, with checksum, package/installation records and reused first-import evidence. Python 3.13.15 and the NuGet inputs were unchanged; no downloads or new scans were needed.

Matching `dotnet clean` completed without a cleanup warning. The task-owned `artifacts/work/msix-fe2bdacbab854df58c61a63153f89cd7/` directory was removed and verified absent. Automatic approval review blocked the guarded removal of superseded `artifacts/msix/debug/x64/1.0.14.0/` with `blocked by policy`; about 99.8 MiB remain. No alternate deletion was attempted. Earlier cleanup blocks remain unresolved.

## Debug MSIX 1.0.14.0 — 2026-10-22

Built and signed locally from the current working-tree source, then installed through `install-debug-msix` with package-scoped forced shutdown enabled. Windows reports version `1.0.14.0`, architecture `X64` and status `Ok`. No application launch, UI automation, synthetic tests or provider operations ran.

The retained installer is 104,647,982 bytes, SHA-256 `ecaaab77097de7d78d358c8a6f2175a27d13654507cd2deabca19aa38e2d2e76`, under `artifacts/msix/debug/x64/1.0.14.0/`, with its checksum, package/installation records and copied first-import evidence. The build reused unchanged Python 3.13.15 and NuGet inputs previously scanned for 1.0.13.0; no downloads or new scans were needed.

Matching `dotnet clean` completed without a cleanup warning. The task-owned `artifacts/work/msix-753d62d178d44cd08883f53aae9b582d/` directory was removed and verified absent. Automatic approval review blocked the checked removal of superseded `artifacts/msix/debug/x64/1.0.13.0/` with `blocked by policy`; about 99.8 MiB remain, with no alternate removal attempted. The previously blocked input and legacy-output cleanup below also remains open.

## Provider evidence — 2026-10-21

An explicitly authorized, unauthenticated probe of `smtp.gmail.com:587` completed greeting, EHLO (`250`), mandatory STARTTLS (`220`, TLS 1.3 with certificate/hostname validation), and post-TLS EHLO (`250`) using the bounded SMTP transport. It did not authenticate, inspect saved credentials, read mail or send a message.

The minimal record is `artifacts/verification/smtp-starttls-20261021/result.json`, with the network address removed. The owner later reported correcting the password and successful SMTP authentication. This is owner-reported evidence, not an automated authenticated test or delivery check.

## Historical synthetic checks

All runs used Windows and Python 3.13.15, synthetic `example.test` accounts, mocked network/native credentials, isolated profiles and no bytecode/user-site imports. The suite command was `python -B -m unittest discover -s python/tests -t python -v`.

| Date | Result | Scope |
| --- | --- | --- |
| 2026-10-19 | 77 passed, 1.659 s | Account policy, strict input, IMAP/POP3 bounds, original approval flow, SMTP outcomes, Microsoft device flow, storage/cleanup and mocked vault routing |
| 2026-10-19 | 77 passed, 1.701 s | Same suite after Microsoft wording changes |
| 2026-10-19 | 90 passed, 1.701 s | Added current-form checks, exact folder names, optional sample reads, progress and failure cases |
| 2026-10-20 | 90 passed, 1.949 s | Complete then-current Python refactoring, including uncommitted account-check source |

The 2026-10-20 extracted package runtime also passed CLI help and an isolated `manage` account listing. This did not exercise the installed alias, MCP integration or native credentials. The owner subsequently emptied `artifacts/`; original historical logs and package evidence were removed. A replacement record is under `artifacts/verification/main-consolidation-ci-20261020/`.

Nine earlier Codex setup checks passed in 0.88 s with a synthetic temporary `CODEX_HOME`: detection, registration, preservation of unrelated settings/comments, matching-entry recognition, replacement detection, refusal without confirmation, confirmed replacement, stale-snapshot rejection and cleanup. They did not change the owner's configuration or start a mail server. The harness is no longer retained.

## Historical local packages

All packages below are Debug x64 source builds, not Store releases. Builds used .NET SDK 10.0.402 and embedded Python 3.13.15. Installation status describes that run, not a fresh query of the current machine.

| Version | Source/date | Result | Bytes | SHA-256 |
| --- | --- | --- | ---: | --- |
| 1.0.9.0 | `1eaf1c5`, Oct 19 | Signed, installed, X64 `Ok` | 104641178 | `60332d079f5e1318f6a4dffd1ab5570cdbcdd76169172968593607316e68a75e` |
| 1.0.10.0 | `7cd237c`, Oct 19 | Signed, signature `Valid`, not installed | 104688604 | `1efce56c10a43df9516e8188abdc9a1ed936cf2d51d88359e0284c9d8fd5741f` |
| 1.0.11.0 | Working tree, Oct 19 | Signed, installed, X64 `Ok` | 104700379 | `ac3a2e6c81b30c9f3a65ae14d57865fe30611c06ad2c18451db2d5f01c67e0ed` |
| 1.0.12.0 | Working tree, Oct 20 | Unsigned, checksum checked, not installed | 104681573 | Original checksum removed by owner cleanup |
| 1.0.13.0 | Working tree, Oct 21 | Signed, installed, X64 `Ok` | 104658616 | `3f5c15ec3b1002afded4adac948d57bf1f5f8c8fcc1d963ac11c17231ad8039a` |

Version 1.0.13.0 includes the current-form SMTP button fix and local server diagnostics. Its first installation returned `0x80073D02` because the app was open; the same package installed after the owner closed it. The later `-ForceApplicationShutdown` installer change was not exercised in that run. The MSIX, checksum, package/installation records and import-scan evidence are retained under `artifacts/msix/debug/x64/1.0.13.0/`.

Version 1.0.11.0 included the earlier Mica account-check UI. Version 1.0.10.0 had a read-only manifest/source/runtime inspection. An earlier isolated stock-TabView Debug restore/publish also passed without packaging or UI validation. These do not validate the current Acrylic window.

## Historical UI checks

Version 1.0.8.0 built and installed but failed opening Accounts. An isolated reproduction identified `ArgumentException` (`0x80070057`) from assigning an application-owned resource dictionary to `TabView.Resources`. The fix avoided reparenting; later source uses the stock TabView.

Isolated Windows UI checks covered empty/create/cancel, all three tabs, password-only Gmail configuration, optional SMTP fields with sending disabled, narrow/maximized layouts, page recreation, an incompatible synthetic profile and explicit retry. Packaged 1.0.9.0 startup also exercised empty state, Create, Incoming and Cancel with its bundled runtime and an isolated profile. No accounts/passwords were saved, and no mail was sent. Complete keyboard, light/high-contrast and native credentials were not checked. Those diagnostic files were later removed with historical artifacts.

## Imports and cleanup

New inputs for 1.0.10.0 and 1.0.13.0 came from official Python and NuGet sources: Python 3.13.15 and a 23-package closure. Targeted Defender scans covered the archive-only Python folder, extracted Python and NuGet archives/extraction; all completed without detections. For 1.0.10.0, a skipped direct ZIP scan was not counted: the containing archive-only folder was scanned successfully. Version 1.0.11.0/12.0 reused unchanged imports. Builds started no scans.

The original 77-test run followed a runtime scan under the earlier policy; 1.0.6.4 had 48 historical clean scans. Those records do not certify new imports or justify repeating scans of installed runtimes. Current first-import sources, hashes, commands and results are in `first-import-scans.json` beside 1.0.13.0.

Matching `dotnet clean` and task-owned packaging cleanup completed for the recorded builds. Automatic approval review blocked some checked deletions with `blocked by policy`. Earlier blocked staging, logs and packages were subsequently removed by the owner when `artifacts/` was emptied. Current unresolved paths are:

- `artifacts/inputs/debug-1.0.13.0-20261021/`: about 1,783.9 MiB. Removal was blocked after the successful build/install; no alternate method was attempted.
- Twenty ignored legacy `src/*/bin` and `src/*/obj` directories: about 2.3 MiB. Inventory is in `artifacts/verification/main-consolidation-ci-20261020/legacy-output.json`; checked removal was blocked.

## Remaining evidence

Current synthetic tests, Windows visual behavior, actual Credential Manager/DPAPI calls, real IMAP/POP3 reading, Microsoft consent/refresh, direct SMTP submission/delivery, installed MCP alias/update behavior, ARM64, Store packages and Linux/macOS execution require their own authorized checks. See [todo](../.github/tasks/todo.md).

CI defines isolated Windows Python tests. Its interpreter discovery was corrected after a WindowsApps alias caused an early failure; local preparation passed without downloads/scans. No remote CI result is claimed here. Previous-engine checks do not validate this implementation.
