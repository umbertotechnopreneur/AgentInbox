# Windows MSIX packaging

The MSIX contains the WinUI 3 setup app, Python mail engine and a matching embedded Python runtime. Windows commercial distribution is MSIX only, for Windows 11 x64 and ARM64. The stable `AgentInbox.exe` execution alias points to the bundled Python runtime: use `AgentInbox.exe -B -m agentinbox mcp` or `AgentInbox.exe -B -m agentinbox manage --session`. Start-menu launch opens the native app.

The current packaging source has not been run. No build, targeted scan, signing result, installation or runtime behavior is validated by this document.

## Prerequisites and inputs

After explicit owner authorization, use Windows with PowerShell 7, the required .NET SDK, Windows SDK tools and available Microsoft Windows Defender. The scripts do not install these tools.

Stage runtime archives, extracted runtimes and NuGet inputs under a task-specific `artifacts/` directory. For single-architecture packaging or Debug installation, supply these inputs through `scripts/AgentInbox.ps1`:

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
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command install-debug-msix <required-inputs> -Version <four-part-version> -CertificateThumbprint <thumbprint>
```

This command builds and signs the package, immediately calls installation, then checks the installed version, architecture and package status. It does not perform UI or live-mail checks. If no thumbprint is supplied, it requires exactly one suitable existing trusted current-user code-signing certificate.

## Local Store build

Follow the local PromptMeUp recipe after explicit Store authorization:

```text
pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command build-store-msix -Version X.Y.Z -PythonRuntimesManifest <runtime-manifest.json> -DependencyManifest <dependency-manifest.json>
```

The wrapper builds both x64 and ARM64 with Release configuration, using unsigned MSIX packages and package version `X.Y.Z.0`. It does not sign, install, upload, publish or create tags. Store builds run locally and are rejected inside GitHub Actions. The optional `-MakeAppxPath` selects an existing SDK tool.

The runtime manifest contains exactly these fields; replace every placeholder:

```json
{
  "runtimes": {
    "x64": {
      "archive": "<absolute-x64-archive.zip>",
      "directory": "<absolute-extracted-x64-directory>",
      "source": "https://www.python.org/ftp/python/<version>/python-<version>-embed-amd64.zip",
      "version": "<three-part-python-version>",
      "sha256": "<expected-64-character-SHA-256>"
    },
    "arm64": {
      "archive": "<absolute-arm64-archive.zip>",
      "directory": "<absolute-extracted-arm64-directory>",
      "source": "https://www.python.org/ftp/python/<version>/python-<version>-embed-arm64.zip",
      "version": "<three-part-python-version>",
      "sha256": "<expected-64-character-SHA-256>"
    }
  }
}
```

Both runtime entries, paths, Python versions, hashes and new destination directories are checked before either architecture starts. The shared dependency manifest must contain the full offline closure for both architectures. Each engine invocation performs the required Defender scans before integrating its inputs. Package paths are returned only after both builds succeed; if the second build fails, the first completed package is preserved and reported. An unsigned MSIX is a build artifact, not an installed or approved release.

The Store recipe accepts a three-part `-Version` with a nonzero first component and appends `.0`. Every package-version component must be from 0 to 65535. The single-architecture engine also rejects Store versions whose first component is zero or fourth component is nonzero.

Installed MCP configuration uses `%LOCALAPPDATA%\Microsoft\WindowsApps\AgentInbox.exe`, not an executable path inside a versioned package directory. The alias and native app share `%LOCALAPPDATA%\Packages\<package-family>\LocalState\AgentInbox` unless `AGENTINBOX_DATA_DIR` explicitly selects another absolute directory. Native outgoing review resolves the current installed desktop executable. These entry points and update behavior still require authorized package validation.

## Output and cleanup

Deliverables go under:

```text
artifacts/msix/debug/<x64-or-arm64>/<four-part-version>/
artifacts/store/<four-part-version>/<x64-or-arm64>/
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
