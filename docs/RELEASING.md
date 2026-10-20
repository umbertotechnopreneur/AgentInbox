# Release requirements

The Python engine and WinUI 3 changes have no validated release package.

Windows commercial distribution is MSIX only, for Windows 11 x64 and ARM64. Linux and macOS are source-only; do not publish compiled binaries or installers for them.

Obtain explicit authorization for the requested build and channel. Generic builds default to local Debug and do not authorize Store work, publication or tags.

Scan new external packages and their extracted dependencies before use. Record source, version or hash, command and result. Include notices for components actually shipped.

A Windows package must contain the Python runtime and engine needed by its entry points; it must not depend on an arbitrary system interpreter. Keep source and package versions consistent.

For an authorized Store build, follow the local PromptMeUp recipe:

```text
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command build-store-msix -Version X.Y.Z -PythonRuntimesManifest <runtime-manifest.json> -DependencyManifest <dependency-manifest.json>
```

It builds unsigned Release MSIX packages for both x64 and ARM64 under `artifacts/store/X.Y.Z.0/<architecture>/`. Supply official runtime archives and matching extracted directories for both architectures, plus the full offline NuGet closure. Both runtime entries and destinations are checked before the first build. Archives and extracted inputs must pass targeted Defender scans before use. See [packaging](../packaging/windows/README.md#local-store-build).

The `Manual Windows MSIX` workflow is for Debug artifacts only. Store packages are built locally, not through GitHub Actions. The local scripts retain packages, checksums and small packaging records; they do not install Store packages, upload, publish or create tags. An unsigned artifact is not an installed or approved release.

When authorized, validate packaged entry points, architecture, native review and installation separately. Local `install-debug-msix -Version <four-part-version>` uses an existing current-user certificate, installs immediately and checks the installed package. It does not authorize UI or live-mail checks.

No historical test result validates this engine. SMTP acceptance is not recipient delivery.

Retain only requested current packages, checksums and small verification records. Preserve pending uploads and requested rollback material until no longer needed. Follow cleanup rules for other output.

See [validation](VALIDATION.md) and [working agreement](../AGENTS.md).
