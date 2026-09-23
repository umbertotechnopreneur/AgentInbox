# Releases

GitHub distributes source and executable archives. There is no hosted deployment or marketplace.

## Packages

Each release contains complete Windows x64 and ARM64 portable ZIPs. Both include the native setup app, a separate CLI/MCP executable and their runtimes. The workflow creates portable ZIPs only; Store packages are prepared and submitted manually. See [portable setup](WINDOWS_PORTABLE.md). The workflow does not publish Linux or macOS archives.

Only Windows x64 is tested for the current MVP. Windows ARM64 is build-only.

Each package includes the application, .NET runtime, license notices, version information and a SHA-256 checksum; ZIPs also record the source commit.

A native smoke test runs when the runner matches the target CPU. Other packages explicitly record that native execution was not tested.

## Rehearse

Run **Actions > Windows release > Run workflow** on `main`. It builds the portable ZIPs without creating a release or an MSIX package.

Local Windows example, from a clean committed checkout:

```powershell
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command package-portable -Runtime win-x64
```

Output is under `artifacts/`. Packaging clears that directory before building, including previous packages and logs. Copy anything you want to retain elsewhere first; build one target at a time in each checkout.

Complete Windows ZIPs use committed dependency graphs under `eng/locks/windows-portable/`. Refresh them with `pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command update-locks` after adding modules or changing dependencies.

## Publish later

1. Update version, changelog and validation evidence.
2. Merge passing code into `main`.
3. Only when authorized, push the matching `v<Version>` tag.
4. Review the generated draft release and manually publish it.

Dispatch never publishes a release. A matching tag creates a pre-release draft for review, or updates the ZIP assets of an existing draft while preserving its notes. Published releases are never overwritten. Publish manually after reviewing the assets. A tag must match the project version and point to code reachable from `main`.

## Current limits

The current pre-alpha reads real accounts after local provider setup and sign-in. ZIP checksums detect changes but are not publisher signatures. Store publication is a manual owner action.

No provider credentials belong in builds or release artifacts.

Release builds intentionally contain no Google or Microsoft app credentials. Each user imports their own Google Desktop client file and Microsoft client ID after extraction. Do not inject OAuth client secrets through GitHub environment variables or repository secrets: a value embedded in a desktop executable can be extracted.

Prepare any Store package manually outside the release workflow. Keep signing credentials out of the repository and release assets.
