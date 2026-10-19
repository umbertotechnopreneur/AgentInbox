# Shared script modules

This folder contains shared standalone development utilities. The current
`scripts/AgentInbox.ps1` mail packaging entry point does not load them.

- `modules/MenuManager` provides the interactive menu controls.
- `session-replay` keeps its Python engine, PowerShell inventory module, tests,
  and usage notes together as a self-contained tool module.

Command implementations exposed by the AgentInbox CLI live under
[`../commands`](../commands/).
