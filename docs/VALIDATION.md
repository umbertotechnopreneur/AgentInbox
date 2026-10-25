# Validation

Last updated: 2026-10-26.

## Linux/macOS source hardening — not executed

Source changes preserve the selected Python interpreter path during plugin export, including virtual-environment symlinks; reject nonexecutable POSIX interpreters and profile paths that are existing files before creating export output; and document management CLI setup and native credential prerequisites. Linux now treats an empty `XDG_DATA_HOME` like an unset value. Unsupported operating systems fail explicitly before profile selection or credential access. Native storage rejects NUL-containing values before a C string can truncate them, declares native return types, and reports unavailable macOS frameworks through a bounded application error.

Added synthetic regression cases for backend routing, missing native dependencies, unsupported platforms, NUL values, Linux profile selection and exporter path validation. The interpreter-symlink and execute-permission cases require POSIX and are skipped on Windows. These cases have not been run. Reviewed the source diff only; no tests, formatters, linters, builds, exports, installation, native credential calls, provider requests or CI/CD ran. No external binaries or dependencies were imported. Linux/macOS execution and end-to-end MCP integration remain unverified.

The path decisions were checked against the official [Python virtual-environment documentation](https://docs.python.org/3.11/library/venv.html) and [XDG Base Directory specification](https://specifications.freedesktop.org/basedir/latest/). The existing nullable schema and caller-owned attributes follow the [libsecret store API](https://gnome.pages.gitlab.gnome.org/libsecret/func.password_storev_sync.html); no schema migration was introduced.

## Product wording across entry points — source only

Updated README/setup guides, repository wording instructions, Windows UI/package descriptions, Python and PowerShell help, plugin metadata/export copy and MCP initialization/tool descriptions to lead with multiple compatible mailboxes and selected folders through one MCP connection. Provider examples include Gmail, Microsoft, iCloud and GoDaddy when compatible IMAP/POP3 access is available. Copy describes dozens of IMAP folders across accounts while preserving the implemented twenty-folder selection limit per account and POP3's single INBOX. Mail policy and permissions did not change.

Reviewed the text diffs and the currently published AgentInbox product page. The public page still leads with Codex and describes the former send-approval flow. Updated website source copy and SEO in English, Italian and Vietnamese separately; those edits have not been published. No build, installation, CLI execution, automated tests, live UI check, provider operation or CI/CD ran for this wording follow-up. The installed 1.0.23.0 does not contain the new copy.

## Account permissions on the first tab — installed Debug 1.0.23.0

Built and signed locally from source checkpoint `74537b7`, then installed through `install-debug-msix` on 2026-10-25. Windows reports Debug `1.0.23.0`, architecture x64 and package status `Ok`. Enable receive and Enable send now appear under How to connect in Account. Enabling send also prepares SMTP configuration in the unsaved draft. No automated tests, application launch, UI interaction, mail-provider operation or CI/CD ran for this installation. Existing account data was preserved; no account permissions were edited by the build.

The installer is 105,461,798 bytes, SHA-256 `1b90710ac0fe66e2b3d76e59076a63a21243602ebf5129d343fdf72270716747`, retained under `artifacts/msix/debug/x64/1.0.23.0/` with checksum, package/installation records and import evidence. Python 3.13.15 and twenty NuGet archives were recovered from the official sources in the retained 1.0.22.0 import record. All twenty-one SHA-256 values matched. Existing first-import scans were reused; no new scan ran.

Packaging completed its matching clean without a warning. Its exact `artifacts/work/msix-844cc97b8bad429e83a491641aefcb52/` directory was verified absent. The task-owned recovery directory `artifacts/work/install-1.0.23.0-20261025/`, including archives, extracted inputs and the helper script, and superseded `artifacts/msix/debug/x64/1.0.22.0/` were removed and verified absent. Only the current package and minimal evidence were retained for this channel.

## Published configuration changes — installed Debug 1.0.22.0

The owner authorized publishing all nineteen previously local commits through [PR #36](https://github.com/umbertotechnopreneur/AgentInbox/pull/36), plus a local build and installation. The PR remains open; merge was not requested. The required [Windows CI run](https://github.com/umbertotechnopreneur/AgentInbox/actions/runs/39960504558) passed 108 synthetic Python tests against source checkpoint `0713396`. These checks do not validate real providers, the installed MCP alias or WinUI rendering.

Built and signed locally from `0713396`, then installed with `install-debug-msix`. Windows reports Debug `1.0.22.0`, x64 and status `Ok`. This includes the simpler Connect Codex page and two-line VibeWare link. No local automated tests, application launch, live UI interaction or mail-provider requests ran in this delivery. The retained installer is 105,461,645 bytes, SHA-256 `ad14fb3a3d85ee5ed4af1caf3d31cd5a5b7ed53b90deb9e0850371a8b7a88cd9`, under `artifacts/msix/debug/x64/1.0.22.0/`, with checksum, package/installation records and import evidence.

The former build-input directory was already absent. Recovered Python 3.13.15 and five NuGet archives from official sources matched previously recorded SHA-256 values and reused their earlier scan evidence. For the other fifteen required NuGet archives, the original individual fingerprints were unavailable: archives and extracted contents were staged separately and received one explicit targeted `Start-MpScan -ScanType CustomScan -ScanPath <task inputs/recovered>` before use. Defender events 1000/1001 identify completed scan `{A7EBEFE7-6FCD-4423-A374-8DAC5FE88706}` with no detected threats. Sources, hashes, scope and completion are retained in the package's `first-import-evidence.json`. The initial recovery helper collected unused framework/lower-version alternatives; those were removed before scanning. No application was reconstructed from installed or prebuilt application binaries, and no scan covered installed SDK/runtime trees or generated output.

Packaging ran its matching clean without warnings and removed `artifacts/work/msix-0cfab9d7a034483b8710073137fcd945/`. The delivery scratch directory, downloaded archives, extracted inputs, helper scripts and PR draft were removed after final use; only the current package and minimal evidence remain. Cleanup and retention were verified after installation.

## Toolbar and branded Mica dialogs — installed Debug 1.0.21.0

Built and signed locally from source checkpoint `37c4a40`, then installed through `install-debug-msix` on 2026-10-23. Windows reports version `1.0.21.0`, architecture x64 and package status `Ok`. This includes the inline Add/delete/refresh/Test All toolbar, Mica dialog title bars with the app logo, colored stage symbols, per-account failure/cancellation marks and clickable session result details. Timeout and explicit cancellation have different messages. No automated tests, application launch, UI interaction, MCP launch or mail-provider requests ran. The earlier Outlook check's provider cause remains unknown: no persisted check diagnostics or recent AgentInbox Application errors were found. No previous provider success is assumed.

The first build failed with CS1503 in the new title-bar container because `Grid.SetRow` requires a `FrameworkElement`; the content cast was corrected before rebuilding. Both attempts ran their matching clean and left no cleanup warning. Their exact task directories, `artifacts/work/msix-179fe65644fa4e299cd6a0ace7094825/` and `artifacts/work/msix-cfad727397de4861badaf0d61570f7c3/`, were verified absent. The failed package output was removed before retrying, and superseded `artifacts/msix/debug/x64/1.0.20.0/` was verified absent after installation; no deletion by this task is claimed.

The retained installer is 105,461,598 bytes, SHA-256 `90a77b0383c543b13d1a9ed44ba6e33f6440f6f56b763d34d42fc7b5edf09bb1`, under `artifacts/msix/debug/x64/1.0.21.0/`, with checksum, package and installation records and unchanged first-import reuse evidence. The existing Python 3.13.15 and NuGet inputs were reused; no downloads or new scans ran. The previously blocked input directory `artifacts/inputs/debug-1.0.18.0-20261022/` remains at 502,715,034 bytes (479.4 MiB). The older package directory `artifacts/msix/debug/x64/1.0.18.0/` is now absent; this task did not remove it. See [todo](../.github/tasks/todo.md) for pending runtime checks.

## Mica results and Test all — installed Debug 1.0.20.0

Built and signed locally from source checkpoint `9e15072`, then installed through `install-debug-msix` on 2026-10-23. Windows reports version `1.0.20.0`, architecture x64 and package status `Ok`. This includes Mica operation dialogs, sequential saved-account checks, transient success marks, CDN provider favicons, simplified Codex setup and footer links. No tests, app launch, live UI interaction, MCP launch or mailbox/provider operation ran. Compilation and installation do not establish visual or provider correctness. See [todo](../.github/tasks/todo.md).

The installer is 105,459,913 bytes, SHA-256 `23b6b653a14b40fc626fa8f7ed99248533843dd612c03d01fe4639a963dbf820`, retained under `artifacts/msix/debug/x64/1.0.20.0/` with its checksum, package and installation records and unchanged first-import reuse evidence. Python 3.13.15 and the pinned NuGet inputs were reused without dependency downloads or new scans. HEAD requests returned HTTP 200 for the three fixed Gmail/iCloud/Outlook favicon URLs; this does not verify WinUI rendering. The icons are fetched at runtime, with a native fallback.

Packaging ran its matching `dotnet clean` without a cleanup warning. Its exact `artifacts/work/msix-8ad10585ec024a73b6f5ce6f144f24fc/` directory and the superseded `artifacts/msix/debug/x64/1.0.19.0/` were removed and verified absent. Previously blocked cleanup was not bypassed: `artifacts/inputs/debug-1.0.18.0-20261022/` remains at 502,715,034 bytes (479.4 MiB), and `artifacts/msix/debug/x64/1.0.18.0/` remains at 105,453,405 bytes (100.6 MiB).

## Configuration-model refactor — installed Debug 1.0.19.0

Source checkpoint `3214c1c` separates account records, unsaved drafts, selection, validation, notices, Microsoft sign-in, connection-check progress and Codex setup state from native controls. Views bind to these models; only rendering, native lifecycle, focus, browser and clipboard adapters remain in the windows. Returning to Accounts preserves the draft rather than reloading over it. Windows loads server defaults from the same Python presets operation used by the CLI.

Python account/preset contracts are now explicit, and one process-local `SessionState` holds references, continuations and the access-token cache used by `MailApplication` and its Microsoft service. Reference expiry/capacity and persisted account/send contracts are unchanged. New synthetic cases describe isolation, bounded expiry and omission of private cache data from diagnostic representations; they have not been run.

Built and signed locally from source checkpoint `e0c7789`, then installed through `install-debug-msix` on 2026-10-23. Windows reports version `1.0.19.0`, architecture `X64` and status `Ok`. No tests, formatting, application launch, live UI interaction, MCP launch or provider operation ran. Compilation and installation do not validate binding updates or model lifecycles. Outstanding checks are in [todo](../.github/tasks/todo.md).

The retained installer is 105,459,611 bytes, SHA-256 `73197feada8ef19075b652d7a8838cd7fe1f27c4dd7777c87993db00c89bc718`, under `artifacts/msix/debug/x64/1.0.19.0/`, with checksum, package/installation records and unchanged first-import evidence. The build reused Python 3.13.15 and the existing NuGet inputs; no downloads or new scans were needed.

Packaging completed its matching clean and reported no cleanup warning. Its task-owned `artifacts/work/msix-729f4106f02144928c08f79a2056b3e3/` directory was removed and verified absent. Automatic approval review rejected guarded removal of superseded `artifacts/msix/debug/x64/1.0.18.0/` with `blocked by policy`; about 100.6 MiB remain. No alternate removal was attempted. The previously blocked input directory below also remains.

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
