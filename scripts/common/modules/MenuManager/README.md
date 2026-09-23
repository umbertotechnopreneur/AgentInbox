# MenuManager

`MenuManager` is the repository-local copy of shared interactive PowerShell
menu helpers used by `scripts/AgentInbox.ps1`.

The launcher imports the manifest with the `AgentInbox` prefix, so the public
commands are exposed to the script as:

- `Read-AgentInboxMenuChoice`
- `Read-AgentInboxIndexChoice`
- `Write-AgentInboxMenuItem`
- `Write-AgentInboxMenuSectionHeader`
- `Write-AgentInboxBackItem`
- `Wait-AgentInboxForEnter`
- `Resolve-AgentInboxMenuConsoleWidth`
- `Write-AgentInboxMenuCompactSectionHeader`
- `Write-AgentInboxMenuItemCompact`
- `Write-AgentInboxMenuItemCompactPair`

`Read-AgentInboxIndexChoice` is used by the AgentInbox launcher
because it supports multi-digit input and treats `0` or `Esc` as cancel. Entry
17 also closes the launcher. Raw-key input is preferred in an interactive
console, with a `Read-Host` fallback for other hosts.

Copied session-replay helpers can reuse the same prefixed menu writers, numeric
choice reader, back item, and wait helper for both its action submenu and its
strict JSON settings editor. It does not maintain a separate menu input model.

The module is an internal implementation detail. `scripts/AgentInbox.ps1` is
the only public launcher entrypoint.
