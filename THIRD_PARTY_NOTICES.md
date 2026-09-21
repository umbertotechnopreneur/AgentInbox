# Third-party notices

AgentInbox source is MIT-licensed. Dependencies retain their own licenses; AgentInbox's license does not replace them.

The exact restored CLI dependency inventory is in `docs/DEPENDENCIES.md` in source and `DEPENDENCIES.md` in portable packages. `scripts/export-notices.py` derives it from NuGet metadata and fails validation if it becomes stale.

Windows MSIX packages additionally include the Windows App SDK / WinUI runtime. Their combined desktop and CLI inventory is generated during packaging as `licenses/DEPENDENCIES.md`, with each restored dependency's metadata and supplied license/notice texts under `licenses/packages/`. The portable CLI inventory does not describe those Windows UI dependencies.

Included upstream license texts under `docs/licenses/` (or `licenses/` in a portable package) cover:

- .NET runtime and Microsoft packages: MIT, with upstream third-party notices.
- Microsoft.Extensions.AI: MIT.
- ModelContextProtocol: Apache-2.0.
- Spectre.Console: MIT.
- Serilog and its hosting, logging and console integrations: Apache-2.0.
- SQLitePCLRaw: Apache-2.0; its bundled SQLite engine has separate public-domain provenance described by that upstream project.

Portable packaging also copies each restored package's `.nuspec` and available license/notice files, preserving package-specific metadata and notices. Test dependencies are development-only and are not distributed with the executable. Review new dependency licenses and notices when updating packages.

OpenAI, Codex, Google, Gmail, Microsoft, Outlook and related names are trademarks of their respective owners. Their use identifies interoperability and does not imply endorsement.
