# Validation

Last updated: 2026-10-22.

## Latest installed source; subsequent changes pending

Provider-choice persistence, shared Microsoft reading/sending sign-in, local Microsoft error details, plain setup text, the Connect Codex page and approved VibeWare logo were added after Debug 1.0.15.0. The centered mailbox prompts, + action and redundant Codex-status removal also remain newer than that package. None of these subsequent changes has been built, installed or checked in the live UI.

The approved VibeWare source image was visually inspected and copied byte-for-byte. Source and repository copy have matching SHA-256 `84321fe9f77ccc083813e02c02ff3716ad50a45ed000b8663c5a2d416e2ba5af`; the original MIT notice is included. This verifies the asset copy, not the rendered WinUI footer. New synthetic cases for provider round trips, rejected metadata, retained secrets, Microsoft reply redaction and cleanup failures were written but not run. No Microsoft service request was made; the precise cause of the owner's sign-in refusal is still unknown.

The configuration-only Windows redesign, direct MCP sending, provider guidance and bundled Microsoft app identity were built from source and installed as Debug 1.0.15.0 x64. Synthetic tests and visual checks have not run for these changes. New synthetic cases cover direct dispatch, duplicate IDs, permission changes before DATA, BCC/Unicode/attachments, unresolved submissions, metadata failures and old-schema rejection. Current-form SMTP, stage reporting and diagnostic regressions also remain unrun. Earlier approval-model test results do not validate this new sending path.

The source now removes local approval and pending-message storage. Historical tests of that former flow do not validate direct sending. Website Terms/Privacy source edits remain local and unpublished. Existing public product-policy URLs returned HTTP 200 with the correct page titles during this work; that check does not validate the edited policy content in production.

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
