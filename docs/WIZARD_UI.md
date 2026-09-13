# Wizard implementation

The wizard follows the [approved concepts](design/wizard-2026-09-06/README.md). The earlier `0.1.1.10` package passed its Release build, 113 shared .NET tests, installed CLI/MCP smoke and a synthetic native-window startup check. Subsequent package records and current limits are in [validation](VALIDATION.md).

The current source adds CLI screen selection, an isolated UI preview and further layout refinements. Windows x64 desktop/CLI publication, 291 synthetic .NET tests and published CLI/MCP smoke passed on September 12, 2026. Native rendering and interaction remain untested. These changes are included in the locally installed Windows x64 MSIX `0.1.1.20`; its signature is valid and Windows reports package status `Ok`.

## Open screens from the CLI

```powershell
mailmeup ui --list-steps
mailmeup ui --list-steps --json
mailmeup ui --step welcome --demo
mailmeup ui --step accounts --demo
mailmeup ui --step sharing --demo
mailmeup ui --step codex --demo
```

The step list returns screen names, descriptions and commands without starting the desktop app or application services. `mailmeup ui` opens Welcome in a new window or activates the current page in an existing window. An explicit `--step` requests that page; unsaved sharing choices or search-period changes must be saved or discarded before navigation. Opening a page does not start sign-in, provider reads or Codex installation. CLI success reports a launch request, not a rendered-window check.

`--demo` uses a separate desktop instance with synthetic `example.test` accounts and in-memory sharing, calendars and search preferences. A persistent preview banner identifies this mode. Sign-in, provider setup, provider reads and real Codex actions are disabled. Demo values survive screen changes and reset when the demo window exits; production accounts, credentials and settings are not loaded. Diagnostic logs use a separate temporary directory.

The launcher finds the desktop executable in the installed package or next to the CLI. For a source build, select it explicitly:

```powershell
mailmeup ui --step sharing --demo --desktop-path 'C:\Build\MailMeUp.Desktop.exe'
```

Listing screens works without the Windows desktop runtime. Opening a screen requires Windows and a built desktop executable. `--list-steps` may be combined with `--json`, but not with launch options.

## Native shell and content

MainWindow uses Window.SystemBackdrop with DesktopAcrylicBackdrop and extends native content into the title bar. Transparent page surfaces let the material remain visibly blurred, rather than relying on the subtler dark Mica tint. Light and dark resources share a restrained teal/mint accent; high contrast keeps system colors and hides decorative artwork.

Navigation uses names, connected icons, completion state and an active selection without step numbers. Completion follows visited stages, connected accounts, reviewed sharing and observed local plugin configuration; it does not claim that Codex has loaded the tools.

The footer stays outside page scrolling. The connected-accounts and sharing lists show every account; the calendar picker uses search and pagination when needed. Narrow layouts collapse navigation labels and show the sharing list or editor separately. Scrolling remains available for small windows, long content and accessibility text sizes rather than clipping controls.

Current source refinements shorten Welcome copy, make connected-account counts visible, group sharing controls in an account card and keep detailed Codex checks expandable beneath a clear next action. Provider buttons, account actions and sharing summaries adapt to narrow layouts. These changes retain the native Acrylic shell and existing artwork.

## Progressive disclosure

One selected account owns the sharing draft. Categories appear after sharing is enabled; calendar scope appears only with granted calendar access. Save persists through IMailMeUpApplication. Switching account or leaving sharing protects unsaved choices; users can save or discard explicitly.

The Accounts heading includes **Check read access**. It checks every locally connected account, rather than only accounts shared with Codex. Each provider renews its protected OAuth/MSAL access token silently when possible, then makes minimal Mail and Calendar requests without reading the response body. Mail and Calendar are reported independently, so a connected account can show one capability as available and the other as needing attention. Failure guidance is derived from a safe category; provider response content and credential details never reach the UI.

Each connected-account row also exposes **Reconnect** and **Remove** actions. Reconnect starts provider sign-in for the same account and preserves its saved sharing choices; the result warns if a different provider identity was selected. Remove requires confirmation and deletes only the local account metadata and protected credential cache. It does not delete provider data or revoke provider consent, so the account can be connected again later.

Calendar discovery occurs only after choosing individual calendars. The picker filters and pages names in memory, retains saved IDs absent from discovery, and only returns a draft selection. An empty discovery cannot silently replace saved choices. Saving the account applies that draft; OAuth grants and provider data remain unchanged.

Privacy/terms, sharing explanations, provider registration and manual Codex commands open dedicated dialogs. Essential AI-service disclosure remains on Welcome. The Codex page exposes individual command, CLI, marketplace, plugin and direct MCP findings inside expandable connection details. It distinguishes an added marketplace, installed configuration and tools loaded by a running session. Blocked installation offers state-specific guidance. Each attempt clears stale results; safe results can be copied without raw command output, credentials or account identities. Manual commands remain a fallback; no CLI prompt is executed. Plugin preparation and installation require explicit UI actions and are disabled in demo mode. Native visual validation remains pending.

## Desktop instance lifetime

Program registers a stable Windows App SDK AppInstance key before constructing the WinUI application or application services. A second launch grants foreground activation to the registered process, redirects activation on a worker while pumping the STA, and exits. Redirection has a bounded wait and does not create a fallback second window on failure.

The first application handles activation on its dispatcher, restores a minimized window and brings it forward. The key is released when the application message loop exits. Only MailMeUp.Desktop participates; CLI and MCP executables retain independent process lifetimes.

Startup and redirected launches validate navigation arguments before applying them. Requests received while the window is starting, busy or displaying a dialog wait until navigation can safely proceed; unsaved drafts remain protected. Direct page selection does not mark skipped stages complete. Normal setup and demo preview use different instance keys.

## Bundled artwork

- [Hero](../resources/wizard/hero.png) and [navigation illustration](../resources/wizard/rail.png), created with built-in image_gen; [prompts](../resources/wizard/GENERATION.md).
- [Google](../resources/providers/google.svg) and [Microsoft](../resources/providers/microsoft.svg) vectors; [CDN provenance](../resources/providers/README.md).

The package consumes local assets through explicit Content entries. Runtime logo downloads are unnecessary.

## References

- [Microsoft: system backdrops](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)
- [Microsoft: single-instance WinUI applications](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-single-instance)
