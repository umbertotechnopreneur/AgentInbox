# Release requirements

GitHub releases distribute portable Release ZIPs for Windows 11 x64 and ARM64. Microsoft Store distribution uses separate Release MSIX packages. Debug MSIX packages are local development builds, not release assets. Linux and macOS are source-only; do not publish compiled binaries or installers for them. See [validation](VALIDATION.md) for evidence and limits of each build.

Obtain explicit authorization for the requested build and channel. Generic builds default to local Debug and do not authorize Store work, publication or tags.

Scan new external packages and their extracted dependencies before use. Record source, version or hash, command and result. Include notices for components actually shipped.

A Windows package must contain the Python runtime and engine needed by its entry points; it must not depend on an arbitrary system interpreter. Keep source and package versions consistent.

## GitHub portable releases

Build locally from the current source with the same official-runtime and offline-dependency manifests used by Store packaging:

```text
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command build-portable -Version X.Y.Z -PythonRuntimesManifest <runtime-manifest.json> -DependencyManifest <dependency-manifest.json>
```

The shared Windows build engine restores only from staged inputs, compiles self-contained Release x64 and ARM64 sequentially, bundles Python, source, licenses, a terminal launcher and a short README, and creates ZIPs under `artifacts/portable/X.Y.Z.0/<architecture>/`. It omits MSIX manifests, installer registration and Debug symbols. The ZIP is unsigned; its checksum is recorded beside it. Settings and credentials stay in the Windows user profile, and moving the application folder requires updating an existing assistant launcher path.

Publish only the version-matched ZIPs and checksums, with factual validation notes. Create the annotated `vX.Y.Z` tag after the matching source reaches `main`. Do not upload local profiles, signing credentials, import archives or raw build logs. Public release notes must distinguish compilation/archive checks from native UI, architecture-host and live-provider validation. Store submission is a separate authorization.

## Microsoft Store packages

For an authorized Store build, follow the local PromptMeUp recipe:

```text
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command build-store-msix -Version X.Y.Z -PythonRuntimesManifest <runtime-manifest.json> -DependencyManifest <dependency-manifest.json>
```

It builds unsigned Release MSIX packages for both x64 and ARM64 under `artifacts/store/X.Y.Z.0/<architecture>/`. Supply official runtime archives and matching extracted directories for both architectures, plus the full offline NuGet closure. Both runtime entries and destinations are checked before the first build. Archives and extracted inputs must pass targeted Defender scans before use. See [packaging](../packaging/windows/README.md#local-store-build).

The `Manual Windows MSIX` workflow is for Debug artifacts only. Store packages are built locally, not through GitHub Actions. The local scripts retain packages, checksums and small packaging records; they do not install Store packages, upload, publish or create tags. An unsigned artifact is not an installed or approved release.

When authorized, validate packaged entry points, architecture, Windows configuration and installation separately. Local `install-debug-msix -Version <four-part-version>` uses an existing current-user certificate, installs immediately and checks the installed package. It does not authorize UI or live-mail checks.

No historical test result validates this engine. SMTP acceptance is not recipient delivery.

Retain only requested current packages, checksums and small verification records. Preserve pending uploads and requested rollback material until no longer needed. Follow cleanup rules for other output.

See [validation](VALIDATION.md) and [working agreement](../AGENTS.md).
