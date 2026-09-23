# Shared script modules

This folder contains reusable helpers and grouped modules used by the
`scripts/AgentInbox.ps1` entrypoint and its archived command implementations.

- `modules/MenuManager` provides the interactive menu controls.
- `windows-package-support.ps1` shares dependency notice handling between the
  Windows packaging scripts.
- `session-replay` keeps its Python engine, PowerShell inventory module, tests,
  and usage notes together as a self-contained tool module.

Command implementations exposed by the AgentInbox CLI live under
[`../archive`](../archive/).
