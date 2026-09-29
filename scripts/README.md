# AgentInbox repository commands

Use `AgentInbox.ps1` as the entrypoint for routine development and packaging tasks.
Run it without arguments for the interactive menu, or pass `-Command` in automation:

```powershell
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command help
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command validate
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command package-msix -Channel Debug -Architecture x64 -CertificateThumbprint '<thumbprint>' -TimestampServer 'http://timestamp.example.test'
```

`commands/` contains the implementations selected by the entrypoint. `common/`
contains reusable helpers and modules. Keep argument handling and the menu in
`AgentInbox.ps1`, and invoke command implementations through its dispatcher so
automation and interactive use follow the same path. Some commands, such as
provider checks, require an explicit executable path and are CLI-only.

Package creation clears `artifacts/` before writing a new package. Read the
[Windows MSIX instructions](../packaging/windows/README.md) before packaging or
installing; the entrypoint does not authorize a build or a Store release by itself.
