# Third-party notices

AgentInbox source is MIT-licensed. Third-party components retain their own licenses; AgentInbox's license does not replace them.

## Python engine

The engine uses Python 3.11 or newer and its standard library, without third-party Python packages. Python and its bundled components have their own license and notices. A distribution containing a Python interpreter must include the license and notices supplied with that exact interpreter.

## Windows setup

The WinUI 3 setup project has two direct package references, with versions declared centrally:

| Package | Referenced version | Role |
| --- | --- | --- |
| [Microsoft.WindowsAppSDK](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.4.0) | 2.4.0 | Windows UI and platform components |
| [Microsoft.Windows.SDK.BuildTools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/10.0.26100.9169) | 10.0.26100.9169 | Build tools; `PrivateAssets="all"` |

This is the direct source inventory, not a restored transitive dependency graph or a list of files certified for distribution. The packages' supplied terms and notices govern their contents; do not assume every included binary uses the repository's MIT license.

The Windows app also uses .NET. Existing unmodified [.NET license](docs/licenses/dotnet-LICENSE.txt) and [.NET third-party notices](docs/licenses/dotnet-THIRD-PARTY-NOTICES.txt) are retained with their [source provenance](docs/licenses/SOURCES.md). The notices shipped in a new package must match the runtime actually included.

## Packaging requirements

For each approved MSIX build, collect the actual runtime and dependency inventory and preserve supplied licenses, notices and required attribution. Include Python, .NET, Windows App SDK components and any transitive components that are distributed.

Targeted Microsoft Windows Defender scans are required before importing or running new external packages and their extracted dependencies. See the [README policy](README.md#external-binary-security-policy).

No new runtime has been downloaded for this documentation update. No package restore, scan, build or distribution-license completeness check is claimed.

## Names and marks

OpenAI, Codex, Google, Gmail, Apple, iCloud, Microsoft, Outlook, Python and related names belong to their respective owners. Their use identifies technologies or connection requirements and does not imply endorsement.
