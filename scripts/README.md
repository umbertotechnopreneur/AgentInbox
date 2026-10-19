# Repository commands

Use PowerShell 7 and the `AgentInbox.ps1` dispatcher. Its default command is `help`; it does not open a menu or start work automatically.

```powershell
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command help
```

| Command | Purpose |
| --- | --- |
| `help` | Describe the current command surface |
| `package-msix` | Build a Windows MSIX from explicitly supplied, scanned runtime and dependency files |
| `install-debug-msix` | Build, sign and immediately install a local Debug MSIX |
| `clean-artifacts` | Remove one exact task-owned directory under `artifacts/work/` |

A command's availability does not authorize a build, installation or Store task. The current script changes have not been executed: no build, scan, installation or runtime validation is claimed.

## Packaging inputs

Both packaging commands require:

- `-PythonArchive`: the official Python embeddable ZIP already staged locally.
- `-PythonDirectory`: its matching extracted directory, with no extra or changed files.
- `-PythonSource`: the official HTTPS download URL at `python.org`.
- `-PythonVersion`: the matching Python version, at least 3.11.
- `-PythonSha256`: the archive's expected SHA-256.
- `-DependencyManifest`: JSON naming every required local NuGet archive, including transitive dependencies, its official download URL and expected SHA-256.

Debug and x64 are the defaults. Use `-Architecture arm64` for ARM64. Use an explicit four-part `-PackageVersion` for installation. Supply `-CertificateThumbprint` for signing or `-Unsigned` for an intentionally unsigned package. Local Debug installation requires a signed package; its command can select a single existing trusted current-user signing certificate when the thumbprint is omitted.

Store packages require explicit authorization and run only in GitHub Actions. The scripts do not publish anything.

The restore uses the supplied offline NuGet closure. Missing packages fail instead of triggering a network fallback. There is no implicit dependency download or installation. See [Windows packaging](../packaging/windows/README.md) for the manifest shape, output and signing requirements.

## Targeted security scans

Before importing or running external libraries or binaries, scan their archives and extracted files with Microsoft Windows Defender. The packaging source enforces targeted scans before use, checks archive hashes and records official sources and scan results. Real-time protection alone is insufficient.

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
