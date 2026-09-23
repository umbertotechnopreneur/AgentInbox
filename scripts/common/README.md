# Internal scripts

This folder contains shared PowerShell menu helpers used by `scripts/AgentInbox.ps1`.
The launcher remains the single public entrypoint. Add a file here only when a
task is cohesive enough to run independently behind a launcher command.

Reusable interactive presentation helpers live under `modules`, including the
repository-local `MenuManager` port. AgentInbox owns its public menu and command
dispatch in `scripts/AgentInbox.ps1`.

`Compare-OriginalBaseline.ps1` is the read-only implementation behind the
`baseline` launcher command. It batch-hashes the original source tree and
reports the remaining inherited-file merge surface without invoking Gradle.

`Invoke-SessionReplay.ps1` and the `session-replay` folder implement the
`replay-day` launcher command. They remain internal; use the public workflow in
[`scripts/session-replay.md`](../session-replay.md) for Local/S3 planning,
strict JSON settings, byte-identified staging, per-image fail-fast remote
execution, reserved-code negative evidence caching, Markdown/JPEG output
interpretation, the plain-language `README.md` created at each run root, and
the verified Excel evidence workbook created directly at each completed run
root. The `overlays` directory remains JPEG-only.
Its action and settings menus reuse the shared QSee banner,
footer, bootstrap, and prefixed `MenuManager` helpers.
