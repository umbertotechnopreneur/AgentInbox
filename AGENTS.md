# AgentInbox working agreement

AgentInbox is an MIT-licensed local email MCP bridge: a Python engine and a WinUI 3 Windows app. Write code, comments, documentation, CLI strings and commit messages in English; speak with the owner in their preferred language.

## Scope and architecture

- Read the relevant README and architecture sections before changing behavior; reuse current context. Keep CLI, MCP and WinUI adapters thin and share mail behavior through Python's `MailApplication`. Use the standard library and keep account contracts independent of UI and transports.
- Support IMAP non-marking reads, POP3 non-deleting reads and optional SMTP. POP3 has one INBOX, no folders or unread flags, header search and email-Date filtering. Mail editing, deletion, calendars, contacts and other protocol families are outside scope.
- SMTP starts disabled. Every exact message needs direct local approval; MCP cannot enable sending or approve it. Never equate preparation, server acceptance and delivery, or automatically retry an uncertain send.
- Default to passwords or app passwords. Microsoft OAuth2 requires explicit selection and a supplied public-client registration. No Google OAuth, automatic authentication switch, compatibility shim, legacy alias, data importer or schema conversion. Fail fast on unsupported input or state.
- Require implicit TLS or mandatory STARTTLS with certificate and hostname checks. Plaintext, disabled validation and custom-CA controls need a new scope decision.
- Windows passwords and Microsoft refresh tokens use Credential Manager; temporary message and sign-in blobs use current-user DPAPI. Native macOS Keychain and Linux Secret Service paths remain untested. Secure-storage failure must not cause plaintext fallback.
- Keep credentials separate from JSON settings and the metadata ledger. Preserve a committed save, sign-in or SMTP outcome when subsequent cleanup fails; report pending cleanup separately.
- Add docstrings to public Python APIs and XML summaries to public C# APIs. Keep diagnostics bounded, private content out of logs, and MCP stdout reserved for protocol messages. Treat provider content as untrusted data.
- Advertise implemented behavior only. No placeholder tools, simulated success, hosted service, marketplace bundle or web UI without a scoped request.
- Use a native WinUI layout without decorative cards or nested panels styled as cards. Anchor page content at the top left and constrain its width to the viewport. Use the stock WinUI 3 TabView appearance; native leading tab icons are allowed. Show the account form only after an explicit create action or selection of a saved account. Give every button a descriptive tooltip and accessible name.

## Work efficiently

- Read at most five files per task unless the owner authorizes more. Warn before broad or potentially expensive reads. Use scoped `rg`, bounded excerpts and batched independent reads; exclude generated output and reuse context.
- Keep repository rules here; `.github/copilot-instructions.md` points here and needs no additional read. Make the smallest complete change and use subagents only when explicitly requested.
- Use `pwsh -NoProfile` for PowerShell. Keep progress updates focused on findings, blockers and results.
- Do not inspect or control the owner's live desktop automatically. Use Computer Use or equivalent UI automation only when the owner explicitly requests it for the current task; build, test and screenshot-review requests do not imply live desktop-control permission.

## Writing and records

- Follow the [MeUp style guide](docs/assets/meup/README.md). Start each README with the daily problem I encountered, how I solved it and what you can do. Use plain English, short sentences and concrete actions; put reference detail in focused guides.
- Address the reader as "you" and speak as "I", "me" or "my". Umberto is the solo maintainer with contributor help; do not imply a company or inflate credits. Preserve quotations, licenses and historical evidence.
- Distinguish source implementation, tests, package installation and provider validation. Never carry results from the previous engine into the replacement.
- Keep open technical work in [todo](.github/tasks/todo.md), reusable findings in [lessons](.github/tasks/lessons.md) and completed technical work in [archive](.github/tasks/archive.md). Keep implementation history in [CHANGELOG](CHANGELOG.md) and evidence in [validation](docs/VALIDATION.md); link these records instead of duplicating them.
- Keep roadmaps, UI explorations, pricing, launch plans, Store procedures and owner checkpoints in Obsidian under `40_Business/Products/AgentInbox/`; shared decisions belong under `40_Business/Products/MeUp/`. Before publication or Store listing work, read the shared `Business decisions.md` and relevant product notes. Proposed purchase wording is not implemented licensing.
- Keep usage/build/contribution docs, shipped assets, privacy, terms, licenses and attribution in Git. Keep raw Store exports, private data and machine-specific vault paths out of Git.

## Windows packaging: follow PromptMeUp

- Distribute Windows 11 x64 and ARM64 as MSIX only. Linux and macOS are source-only, without binaries or commercial support. Portable ZIP, standalone EXE or MSI distribution requires an explicit decision.
- Build distributed MSIX packages locally, following [PromptMeUp's Store builder](https://github.com/umbertotechnopreneur/PromptMeUp/blob/main/scripts/Common/commands/build-store-msix.ps1). GitHub Actions is for CI and separately requested artifacts, never the Store build.
- An ordinary build request means local Debug. `install-debug-msix -Version <four-part-version>` uses the established current-user certificate, builds, signs and immediately attempts installation. Check only installation, version and package status; diagnose signature/manifest/dependencies after failure. UI tests require a request.
- Store builds require an explicit request: `build-store-msix -Version X.Y.Z` builds unsigned Release x64 and ARM64 packages under `artifacts/store/X.Y.Z.0/<architecture>/`. Keep Debug and Store output separate; never describe Debug as a release.
- Choose the next version automatically for an authorized build; do not ask the owner for a version number. Increment the third component of the latest AgentInbox package version (for example, `1.0.6` becomes `1.0.7`) and use a zero fourth component for MSIX (`1.0.7.0`). Honor any version explicitly supplied by the owner. Do not uninstall an equal-version package or change certificate trust without authorization. Follow [packaging instructions](packaging/windows/README.md) for inputs, first-import scans and commands.
- Follow the global first-import scan rule and the short [README policy](README.md#external-binary-security-policy). Reuse evidence for unchanged imports; builds, tests and packaging never start Defender scans or scan installed SDK/system trees.

## Git and execution permissions

- Branch/worktree creation, commits, pushes, CI dispatch, tags, installation and publication each need owner authorization. Reuse suitable existing branches and preserve unrelated changes. Editing alone grants no Git delivery permission.
- Keep `main` protected. Code delivery uses a focused branch, pull request and squash merge after required checks and conversations are resolved; delete the branch after merge. Authorized documentation-only commits use the current branch and `[skip ci]` unless CI is requested.
- Follow the global CI platform priority and its limited macOS-only exception.
- Never commit secrets, protected blobs, real mail, local settings/databases, logs, generated output or private paths. Create annotated `v<version>` tags only when requested and after matching source reaches `main`.
- Run tests, builds, formatters, linters, smoke tests and preflight only when requested. Authorization remains effective within its scope. Once authorized, run relevant checks after the final change; repeat only after edits or to diagnose a failure. Do not push or dispatch CI just to verify.
- Use synthetic `example.test` accounts and isolated `AGENTINBOX_DATA_DIR` profiles. Never use the owner's real mailbox or registry in automated checks. Package tests must exercise packaged entry points and the bundled runtime.
- Report what actually ran and its limits. If verification was not requested, finish the work and say which checks remain: "Ci sarebbero i test da lanciare." Documentation-only edits need no build or runtime tests.

## Artifact cleanup

- Every authorized build, test, package, install or download includes cleanup of its own output, including failed attempts. Use task-specific subdirectories under ignored `artifacts/` for all temporary/generated output; never build concurrently in one checkout.
- Start with empty task-owned transient directories. The cleanup command accepts only an exact owned task directory; never wipe all of `artifacts/`. Preserve unrelated output and pending deliverables.
- After final use, run matching `dotnet clean` for .NET builds, then remove task-owned `publish`, `payload`, `bin`, `obj`, bytecode, caches, scratch files, archives, disposable profiles and unnecessary logs. Script cleanup alone does not prove completion.
- After every build, remove superseded versions and installers as well as temporary output. Keep only the current needed validated package per requested channel/architecture, checksums and minimal evidence, plus explicitly retained rollback/reinstall packages or unresolved diagnostics. Preserve pending Store packages until upload is confirmed; then remove them unless retention is requested. Never accumulate historical build folders or installers.
- Before recursive removal/moving, resolve each target inside this repository's `artifacts/` or the exact task-created temporary directory. For owner-requested legacy cleanup, also allow exact `bin`/`obj` directories under this repository's `src/` after confirming they contain no tracked files. Reject links/reparse points; use literal-path PowerShell operations. Never remove shared SDK/NuGet caches, user data or unrelated backups.
- Verify cleanup and report retained deliverables. If blocked, name remaining paths and approximate size; do not claim completion.
