# Repository commands

Use PowerShell 7 and the `AgentInbox.ps1` dispatcher. Its default command is `help`; it does not open a menu or start work automatically.

```powershell
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command help
```

| Command | Purpose |
| --- | --- |
| `help` | Describe the current command surface |
| `package-msix` | Build a Windows MSIX from explicitly supplied, scanned runtime and dependency files |
| `build-store-msix` | Build unsigned local Release MSIX packages for x64 and ARM64 together |
| `install-debug-msix` | Build, sign and immediately install a local Debug MSIX |
| `clean-artifacts` | Remove one exact task-owned directory under `artifacts/work/` |

A command's availability does not authorize a build, installation or Store task. Debug 1.0.6.6 built and installed locally on Windows x64; see [validation](../docs/VALIDATION.md) for evidence and limits.

## Packaging inputs

Single-architecture packaging and Debug installation require:

- `-PythonArchive`: the official Python embeddable ZIP already staged locally.
- `-PythonDirectory`: its matching extracted directory, with no extra or changed files.
- `-PythonSource`: the official HTTPS download URL at `python.org`.
- `-PythonVersion`: the matching Python version, at least 3.11.
- `-PythonSha256`: the archive's expected SHA-256.
- `-DependencyManifest`: JSON naming every required local NuGet archive, including transitive dependencies, its official download URL and expected SHA-256.

Debug and x64 are the defaults. Use `-Architecture arm64` for ARM64. Use an explicit four-part `-Version` for installation. Supply `-CertificateThumbprint` for signing or `-Unsigned` for an intentionally unsigned package. Local Debug installation requires a signed package; its command can select a single existing trusted current-user signing certificate when the thumbprint is omitted.

## Local Store recipe

Follow the shared PromptMeUp command shape after explicit authorization:

```text
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command build-store-msix -Version X.Y.Z -PythonRuntimesManifest <runtime-manifest.json> -DependencyManifest <dependency-manifest.json>
```

This builds Release x64 and ARM64 unsigned MSIX packages locally, with package version `X.Y.Z.0`, under `artifacts/store/X.Y.Z.0/<architecture>/`. The runtime manifest supplies both architectures; the shared dependency manifest must contain the complete closure for both. The wrapper checks both runtime entries, archive hashes and destination directories before either offline build starts. It does not run antivirus scans.

Store builds are rejected inside GitHub Actions. They do not install, upload, publish or create tags. `-MakeAppxPath` can select an existing SDK tool; signing options and single-architecture switches do not apply to `build-store-msix`. See the [runtime manifest](../packaging/windows/README.md#local-store-build) and the separate signed Debug installation recipe.

The restore uses the supplied offline NuGet closure. Missing packages fail instead of triggering a network fallback. There is no implicit dependency download or installation. See [Windows packaging](../packaging/windows/README.md) for the manifest shape, output and signing requirements.

## First-import security scans

Scan a new external library or binary once when first introducing its archive and extracted files/dependencies. Reuse the evidence for unchanged inputs; a new third-party version or changed content is a new import. Build, test and packaging scripts do not start Defender scans or scan installed SDK/system trees. Packaging keeps its source and SHA-256 checks.

Unavailable, disabled, failed or unfinished scans and detections stop the import. Do not add exclusions or bypass a detection. Preserve the official source, version or SHA-256, scan command and completed clean result. No existing dependency is certified by these instructions. See the [repository policy](../README.md#external-binary-security-policy).

## Task-specific cleanup

Cleanup accepts only an exact `artifacts/work/msix-<32-character-task-id>` directory created by packaging. The directory must contain its matching `.agentinbox-task` ownership marker. Paths and descendants containing links or reparse points are rejected.

```text
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command clean-artifacts -TaskDirectory <absolute-task-directory>
```

Without `-TaskDirectory`, cleanup leaves output untouched. It does not wipe `artifacts/`, packages, input staging or another task's files. Packaging performs cleanup for its own transient directory and preserves its deliverables or essential failure record.

## Export a source plugin

`export-plugin.py` copies the Python engine into a fresh local plugin tree; it does not bundle an interpreter, install the plugin or validate a client connection.

```text
python -B scripts/export-plugin.py --data-dir <absolute-profile-directory>
```

The default destination is a new `artifacts/codex-source` directory. `--output` chooses another new directory under `artifacts/`, `--python` selects an existing absolute interpreter, and `--review-app` optionally selects an existing Windows review executable. The export contains machine-specific paths, not account data or passwords. See [client setup](../docs/CODEX_SETUP.md).

`commands/` holds dispatcher implementations. Existing `common/` utilities are retained shared helpers, not additional supported command entry points.
