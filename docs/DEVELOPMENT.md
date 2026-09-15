# Development

To work on MailMeUp, you'll need the .NET SDK listed in `global.json`, PowerShell 7 and Python 3.10+. People using a packaged build don't need these development tools.

After cloning, install the Git hook once:

```powershell
pwsh -NoProfile -File scripts/install-git-hooks.ps1
```

Before each commit, the hook runs `dotnet format` on staged C# files and stages the formatting changes. If you've staged only part of a C# file, it stops so it doesn't accidentally include your other edits.

When you're ready to run the full checks:

```powershell
pwsh -NoProfile -File scripts/validate.ps1
```

This restores the dependencies pinned in the lock files, formats code, builds with warnings treated as errors, runs tests and checks the actual CLI/MCP process. It also checks dependency notices and local documentation links. It uses no real account credentials. To check formatting without changing files, use `scripts/format.ps1 -Check`; CI uses the matching validation option.

Agents working in this repository run checks only when the owner explicitly asks, as described in [AGENTS.md](../AGENTS.md).

Each validation or package run clears `artifacts/` before building. Before a manual `dotnet build` or `dotnet publish`, run `pwsh -NoProfile -File scripts/clean-artifacts.ps1` once. Keep any packages or logs you want to retain outside `artifacts/`; do not run builds concurrently in the same checkout.

To run an app you've already built:

```sh
dotnet run --project src/MailMeUp.Cli -c Release --no-build -- status
```

## Dependency updates

Package versions are in `Directory.Packages.props`. After changing a version:

```powershell
dotnet restore MailMeUp.slnx --force-evaluate
python scripts/export-notices.py
pwsh -NoProfile -File scripts/update-portable-locks.ps1
```

Review both the standard lock files and `eng/locks/<runtime>/`. Portable builds have their own dependency lists because they also include platform runtimes and packaging tools.

Point `MAILMEUP_DATA_DIR` at a separate folder when experimenting. Keep code and docs in English. Google and Microsoft access stays read-only; local settings and account storage can still save files.

See [contribution guidance](../CONTRIBUTING.md) and [release steps](RELEASING.md).
