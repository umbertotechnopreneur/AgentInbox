# Windows MSIX packaging

The MSIX contains the WinUI 3 setup app, Python mail engine and a matching embedded Python runtime. Windows commercial distribution is MSIX only, for Windows 11 x64 and ARM64.

The current packaging source has not been run. No build, targeted scan, signing result, installation or runtime behavior is validated by this document.

## Prerequisites and inputs

After explicit owner authorization, use Windows with PowerShell 7, the required .NET SDK, Windows SDK tools and available Microsoft Windows Defender. The scripts do not install these tools.

Stage the runtime archive, extracted runtime and NuGet inputs under a task-specific `artifacts/` directory. Supply these inputs through `scripts/AgentInbox.ps1`:

| Parameter | Required value |
| --- | --- |
| `-PythonArchive` | Local official Python embeddable ZIP |
| `-PythonDirectory` | Matching extracted directory without added or modified files |
| `-PythonSource` | Official HTTPS `python.org` URL for that archive |
| `-PythonVersion` | Matching version, at least 3.11 |
| `-PythonSha256` | Expected 64-character SHA-256 |
| `-DependencyManifest` | Local JSON describing the complete offline NuGet closure |

The Python URL must identify `/ftp/python/<version>/python-<version>-embed-amd64.zip` for x64 or the corresponding `arm64.zip`. The script checks the archive hash, compares the extracted files with the archive and checks the executable architecture before packaging.

The dependency manifest has this shape; replace every placeholder:

```json
{
  "packages": [
    {
      "archive": "<absolute-path-to-package.nupkg>",
      "source": "https://api.nuget.org/v3-flatcontainer/<id>/<version>/<id>.<version>.nupkg",
      "sha256": "<expected-64-character-SHA-256>"
    }
  ]
}
```

List every package required for the chosen architecture, including transitive and runtime dependencies. The two direct WinUI references alone are not a complete closure. Official NuGet URLs must use an accepted NuGet host. Restore uses only the staged local feed and isolated task caches; missing dependencies fail without network fallback.

## Defender is required before use

The source runs explicit targeted Defender scans on imported archives and extracted files, required tool directories, restored packages and the final payload before packaging. It records the official source, version or hash, scan command and result.

Real-time protection alone is insufficient. An unavailable, disabled, failed or unfinished scan, or a detection, stops the operation. Do not use exclusions, allowlisting or disabled protection to bypass a result. These requirements do not certify existing dependencies and do not grant permission to build or install.

## Build or install

Use the dispatcher with the required inputs above. Its command defaults to `help`.

```text
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command package-msix <required-inputs> -Unsigned
```

`package-msix` defaults to `-Channel Debug -Architecture x64`. It publishes the desktop payload, includes Python source and runtime, preserves supplied notices and creates the MSIX. It does not run tests, launch the app, install the package or publish it.

Supply `-CertificateThumbprint <thumbprint>` instead of `-Unsigned` to sign with an existing valid current-user certificate whose subject matches the manifest publisher. The script does not create certificates or change certificate trust. Optional `-MakeAppxPath` and `-SignToolPath` select existing SDK tools.

For an explicitly requested local Debug installation:

```text
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command install-debug-msix <required-inputs> -PackageVersion <four-part-version> -CertificateThumbprint <thumbprint>
```

This command builds and signs the package, immediately calls installation, then checks the installed version, architecture and package status. It does not perform UI or live-mail checks. If no thumbprint is supplied, it requires exactly one suitable existing trusted current-user code-signing certificate.

`-Channel Store` uses Release configuration and is accepted only in GitHub Actions. Store work requires explicit authorization. Neither channel creates a tag, publishes a GitHub Release or submits to the Store. An unsigned MSIX is a staging artifact.

## Output and cleanup

Deliverables go under:

```text
artifacts/msix/<debug-or-store>/<x64-or-arm64>/<version>/
```

The directory contains the MSIX, matching SHA-256 and `package-record.json`. A successful Debug installation adds `installation.json`. On failure, a small `failure.json` preserves essential diagnostics. Existing version directories are not overwritten.

Transient output uses a unique `artifacts/work/msix-<task-id>/` directory with a matching `.agentinbox-task` marker. Packaging cleans its own build intermediates and task directory after use. Cleanup errors must be reported rather than treated as success.

Manual cleanup requires that exact generated directory:

```text
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command clean-artifacts -TaskDirectory <absolute-task-directory>
```

The cleaner rejects paths outside the exact task naming and ownership rules, plus links and reparse points. Omitting the directory preserves output. It never performs a general wipe of `artifacts/` or removes pending packages.

Preserve requested installers, pending uploads, rollback packages and essential failure evidence. Remove superseded task-owned output only when its retention reason has ended, following the [repository working agreement](../../AGENTS.md).

See [validation](../../docs/VALIDATION.md), [dependencies](../../docs/DEPENDENCIES.md) and [client setup](../../docs/CODEX_SETUP.md). The separate [source-plugin exporter](../../scripts/README.md#export-a-source-plugin) is not a Windows binary distribution.
