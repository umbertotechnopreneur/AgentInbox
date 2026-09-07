# Active work

- The approved Acrylic wizard and desktop single-instance activation are included in locally installed Windows x64 MSIX `0.1.1.10`, with local artwork/provider SVGs, unnumbered progress, account pagination, a selected-account sharing editor and an explicit **Check connections** action. The action checks every local account with minimal Mail/Calendar provider requests, silently refreshes OAuth/MSAL tokens where possible and reports only safe reconnect guidance. The updates fix vector logos loaded as bitmap sources, a custom entry point that left the process running without starting the setup window, and an unavailable optional high-contrast event. Desktop Acrylic replaces dark Mica to make the enabled system transparency visible. Release build, 113 shared .NET tests, package signing/upgrade, the installed alias CLI/MCP smoke and a synthetic native-window launch without diagnostics passed.
- The owner requested to review rendering personally, without Computer Use. Desktop startup, duplicate launches (including a minimized window), adaptive layout, search/pagination, calendar selection, unsaved choices and existing sharing restrictions remain untested for this redesign. Use synthetic accounts and an isolated data directory for future automated checks.
- Earlier `0.1.1.4` synthetic existing-registry startup and earlier About/banner inspection do not validate the redesigned UI. Validate clean-machine deployment, additional DPI settings, Google/Microsoft UI sign-in and local Codex plugin installation when requested.
- Exercise simultaneous installed-alias callers and an MSIX upgrade without changing the Codex command; redirected MCP stdio already passed on the installed alias.
- Synthetic sharing and caller-notification regressions passed, including cached references and in-flight reads. Validate the corresponding live account scenarios only when requested.
- Preserve the package identity and signing publisher across upgrades; do not publish or install on the owner's machine without a request.

- The earlier Windows CLI source passed 68 .NET tests, 24 manual-runner regressions and 27 real-provider checks; three event checks were skipped. These historical live checks are separate from the current synthetic/UI validation. See [validation](../../docs/VALIDATION.md).
- Windows x64 package `de1fae7` passed native and extracted smoke checks, live reads and preservation of all four accounts. Clean installation remains.
- Compare known mail and calendar examples independently, including recurrence, cancellation, all-day dates and time zones.
- Deliberately check real expiry, revoked access, reconnect and lost connectivity when requested. Existing synthetic fault tests do not validate those live paths.
- Check installation on a clean Windows machine and start a small pilot. Windows ARM64 is build-only; macOS/Linux runtime testing is outside the current MVP.
- Keep email and calendar access strictly read-only. Provider writes need a separate explicit decision.
- Validate the new unread/date-range mail tools, structured filters and Spam/Junk plus Trash/Deleted exclusions against synthetic and real provider cases.
- Maintain the progress plan in the versioned [docs/MVP_PLAN.md](../../docs/MVP_PLAN.md); do not maintain separate output copies.
