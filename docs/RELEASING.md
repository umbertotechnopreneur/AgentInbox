# Releases

GitHub distributes source and executable archives. There is no hosted deployment or marketplace.

## Packages

Each release contains a complete Windows x64 portable ZIP, a complete Windows ARM64 portable ZIP and a signed Windows x64 MSIX. Both ZIPs include the native setup app, a separate CLI/MCP executable and their runtimes. See [portable setup](WINDOWS_PORTABLE.md). It does not publish Linux or macOS archives.

Only Windows x64 is tested for the current MVP. Windows ARM64 is build-only.

Each package includes the application, .NET runtime, license notices, version/commit information and a SHA-256 checksum. The release also includes the MSIX public certificate for devices that must trust its signing identity.

A native smoke test runs when the runner matches the target CPU. Other packages explicitly record that native execution was not tested.

## Rehearse

Run **Actions > Windows release > Run workflow** on `main`. It builds the three release assets as workflow artifacts, without creating a release.

Local Windows example, from a clean committed checkout:

```powershell
pwsh -NoProfile -File scripts/package-windows-portable.ps1 -Runtime win-x64
```

Output is under `artifacts/`. Packaging clears that directory before building, including previous packages and logs. Copy anything you want to retain elsewhere first; build one target at a time in each checkout.

Complete Windows ZIPs use committed dependency graphs under `eng/locks/windows-portable/`. Refresh them with `scripts/update-portable-locks.ps1 -Runtime win-x64,win-arm64` after adding modules or changing dependencies. The older `scripts/package.ps1` remains available for CLI-only archives.

## Publish later

1. Update version, changelog and validation evidence.
2. Merge passing code into `main`.
3. Only when authorized, push the matching `v<Version>` tag.
4. Review the generated draft release and manually publish it.

Dispatch never publishes a release. A matching tag creates a draft release for review; publish it manually after reviewing the assets. A tag must match the project version and point to code reachable from `main`.

## Current limits

The current pre-alpha reads real accounts after local provider setup and sign-in. ZIP checksums detect changes but are not publisher signatures. The MSIX job signs with a certificate supplied only at workflow runtime.

No provider credentials belong in builds or release artifacts.

Release builds intentionally contain no Google or Microsoft app credentials. Each user imports their own Google Desktop client file and Microsoft client ID after extraction. Do not inject OAuth client secrets through GitHub environment variables or repository secrets: a value embedded in a desktop executable can be extracted.

Before the MSIX job can run, add these repository secrets: `MSIX_CERTIFICATE_BASE64` (the Base64-encoded PFX), `MSIX_CERTIFICATE_PASSWORD`, `MSIX_PUBLISHER` (the certificate subject, for example `CN=umber`), `MSIX_PUBLISHER_DISPLAY_NAME`, and `MSIX_TIMESTAMP_SERVER`. The workflow imports the PFX into the ephemeral runner’s CurrentUser store, signs the package, then removes the imported certificate and temporary PFX. Keep the private key out of the repository; ship only the generated `.cer` when users need to trust a private signing identity.
