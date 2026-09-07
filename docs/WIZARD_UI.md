# Wizard implementation

The source follows the [approved concepts](design/wizard-2026-09-06/README.md) and is included in the locally installed Windows x64 package `0.1.1.10`. Release build, 113 shared .NET tests and the installed CLI/MCP smoke passed. A synthetic installed launch created a native window without startup diagnostics. Desktop visual and interaction checks remain with the owner; see [validation](VALIDATION.md).

## Native shell and content

MainWindow uses Window.SystemBackdrop with DesktopAcrylicBackdrop and extends native content into the title bar. Transparent page surfaces let the material remain visibly blurred, rather than relying on the subtler dark Mica tint. Light and dark resources share a restrained teal/mint accent; high contrast keeps system colors and hides decorative artwork.

Navigation uses names, connected icons, completion state and an active selection without step numbers. Completion follows visited stages, connected accounts, reviewed sharing and observed local plugin configuration; it does not claim that Codex has loaded the tools.

The footer stays outside page scrolling. The connected-accounts and sharing lists show every account; the calendar picker uses search and pagination when needed. Narrow layouts collapse navigation labels and show the sharing list or editor separately. Scrolling remains available for small windows, long content and accessibility text sizes rather than clipping controls.

## Progressive disclosure

One selected account owns the sharing draft. Categories appear after sharing is enabled; calendar scope appears only with granted calendar access. Save persists through IMailMeUpApplication. Switching account or leaving sharing protects unsaved choices; users can save or discard explicitly.

The Accounts heading includes **Check read access**. It checks every locally connected account, rather than only accounts shared with Codex. Each provider renews its protected OAuth/MSAL access token silently when possible, then makes minimal Mail and Calendar requests without reading the response body. Mail and Calendar are reported independently, so a connected account can show one capability as available and the other as needing attention. Failure guidance is derived from a safe category; provider response content and credential details never reach the UI.

Each connected-account row also exposes **Reconnect** and **Remove** actions. Reconnect starts provider sign-in for the same account and preserves its saved sharing choices; the result warns if a different provider identity was selected. Remove requires confirmation and deletes only the local account metadata and protected credential cache. It does not delete provider data or revoke provider consent, so the account can be connected again later.

Calendar discovery occurs only after choosing individual calendars. The picker filters and pages names in memory, retains saved IDs absent from discovery, and only returns a draft selection. An empty discovery cannot silently replace saved choices. Saving the account applies that draft; OAuth grants and provider data remain unchanged.

Privacy/terms, sharing explanations, provider registration and manual Codex commands open dedicated dialogs. Essential AI-service disclosure remains on Welcome. The Codex page distinguishes installed configuration from tools loaded by a running session. Plugin preparation and installation still require explicit UI actions.

## Desktop instance lifetime

Program registers a stable Windows App SDK AppInstance key before constructing the WinUI application or application services. A second launch grants foreground activation to the registered process, redirects activation on a worker while pumping the STA, and exits. Redirection has a bounded wait and does not create a fallback second window on failure.

The first application handles activation on its dispatcher, restores a minimized window and brings it forward. The key is released when the application message loop exits. Only MailMeUp.Desktop participates; CLI and MCP executables retain independent process lifetimes.

## Bundled artwork

- [Hero](../resources/wizard/hero.png) and [navigation illustration](../resources/wizard/rail.png), created with built-in image_gen; [prompts](../resources/wizard/GENERATION.md).
- [Google](../resources/providers/google.svg) and [Microsoft](../resources/providers/microsoft.svg) vectors; [CDN provenance](../resources/providers/README.md).

The package consumes local assets through explicit Content entries. Runtime logo downloads are unnecessary.

## References

- [Microsoft: system backdrops](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)
- [Microsoft: single-instance WinUI applications](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-single-instance)
