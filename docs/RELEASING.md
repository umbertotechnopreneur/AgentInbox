# Release requirements

The Python engine and WinUI 3 changes have no validated release package.

Windows commercial distribution is MSIX only, for Windows 11 x64 and ARM64. Linux and macOS are source-only; do not publish compiled binaries or installers for them.

Obtain explicit authorization for the requested build and channel. Generic builds default to local Debug and do not authorize Store work, publication or tags.

Scan new external packages and their extracted dependencies before use. Record source, version or hash, command and result. Include notices for components actually shipped.

A Windows package must contain the Python runtime and engine needed by its entry points; it must not depend on an arbitrary system interpreter. Keep source and package versions consistent.

The `Manual Windows MSIX` workflow is manually dispatched from `main`. Its inputs identify the Debug or Store channel, x64 or ARM64 architecture, package version, official Python embeddable version and SHA-256, and a JSON inventory of the complete NuGet dependency closure with official URLs and hashes. Downloaded archives and extracted files must pass the targeted Defender scans before the offline packaging step uses them.

The workflow source produces an unsigned MSIX, checksum and small packaging record. It does not create a tag or GitHub Release, publish a package or submit to the Microsoft Store. An unsigned build artifact is not an installed or approved release.

When authorized, validate packaged entry points, architecture, native review and installation separately. Release MSIX files are created through GitHub Actions. Local Debug MSIX work may use the existing current-user certificate when explicitly requested.

No historical test result validates this engine. SMTP acceptance is not recipient delivery.

Retain only requested current packages, checksums and small verification records. Preserve pending uploads and requested rollback material until no longer needed. Follow cleanup rules for other output.

See [validation](VALIDATION.md) and [working agreement](../AGENTS.md).
