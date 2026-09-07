# Windows setup preview

MailMeUp includes a WinUI 3 setup window and MSIX packaging. Windows x64 preview `0.1.1.10` was built, signed and installed as an upgrade on 2026-09-06. Release build, 113 shared .NET tests, the installed alias CLI/MCP smoke and a synthetic native-window launch without diagnostics passed. The update adds an explicit account connection check, fixes the WinUI vector-source error, an entry-point omission that left the setup process running without a window, an unavailable optional high-contrast change event, and replaces dark Mica with visible Desktop Acrylic. The owner will review the redesigned UI; its interactions and single-instance activation have not yet been exercised. Clean-machine installation, browser sign-in from the UI and Codex plugin loading still need runtime validation.

## Setup

The installed update uses native Desktop Acrylic, unnumbered visual progress, compact account pages and one sharing editor at a time. Additional desktop launches are designed to activate the existing setup window. See [wizard implementation](WIZARD_UI.md) and [validation](VALIDATION.md) for runtime coverage.

1. **Welcome:** understand read-only access and which requested information may reach the assistant's AI service. Tokens stay protected on the device.
2. **Connect accounts:** choose Google or Microsoft and finish sign-in in the browser. Repeat to add more accounts. **Check read access** exercises actual searches and sample detail reads for each enabled capability. Rows stay compact: a red **Try to reconnect** action appears for read failures, while the account menu contains Reconnect and Remove from device. Missing samples and limited checks are not presented as connection failures. Detailed outcomes go only to the local [diagnostic log](LOGGING.md); there are no verbose status rows or check-result banners. This preview still requires [your own provider app registration](APP_REGISTRATION.md); Google imports the downloaded Desktop client JSON, and Microsoft accepts the Application (client) ID.
3. **Choose what to share:** enable each account, mail and calendars. Choose all calendars or load calendar names and select individual ones. Save each account's choices. New accounts connected here start unshared; reconnecting preserves existing choices.
4. **Connect to Codex:** refresh configuration status and install the bundled local plugin. The UI offers manual preparation and commands if the native Codex CLI is unavailable. Existing direct MCP registrations require an explicit migration to avoid duplicate tools. See [Codex setup](CODEX_SETUP.md).

The interface is English-only. A persistent **Privacy & terms** entry opens the website and the MailMeUp, Google and Microsoft policy links. **About & support** remains available throughout setup. The welcome keeps the essential read-only and AI-service disclosures visible; further explanations appear on request.

Closing the setup window does not stop an MCP process started by Codex. Changes affect future reads, including previously issued local result references. They cannot retract data already returned in a conversation. The UI cannot grant read access that was not requested during provider sign-in; reconnect with that category selected first.

The **About & Support** button is available throughout setup. It shows a generated MailMeUp banner, the installed package version, the creator's website, the project repository and its GitHub support issues. Users can copy a small version/platform summary for a support request and follow the invitation to star the project on GitHub. Opening the dialog does not open a browser or submit information.

## Stable command

MSIX installs into a versioned directory. Codex uses the package's console execution alias instead:

```text
%LOCALAPPDATA%\Microsoft\WindowsApps\mailmeup.exe --stdio
```

The plugin writes the expanded absolute path, not a literal environment-variable expression. Keep the same package identity, publisher and alias across upgrades. Windows routes the alias to the installed version; a Codex process already running may need to reload to start the new version. If another app owns the alias or it is disabled, select MailMeUp under Windows **App execution aliases**.

## Local data

The setup window and console bridge share the same `MAILMEUP_DATA_DIR`, or the existing per-user `MailMeUp` data directory by default. MSIX file write virtualization is disabled so existing protected credentials and local plugin files remain visible to both executables and Codex. Registry virtualization remains unchanged.

An upgrade preserves this data. Uninstalling the MSIX does not erase the external data directory or remove an already installed Codex plugin. Remove the plugin in Codex and use the account-removal command before uninstalling if you also want to remove local account credentials. Local removal does not revoke consent at Google or Microsoft.

## Build the installer

Use the dedicated [Windows packaging script](../scripts/package-msix.ps1). It publishes a self-contained desktop app and CLI, creates MSIX logo assets from the existing logo, includes dependency notices, and optionally signs with an existing certificate. It does not execute tests or smoke checks, install the package, change certificate trust, or publish a release.

```powershell
pwsh -NoProfile -File scripts/package-msix.ps1 -Architecture x64
```

Without signing parameters the result is marked `.unsigned.msix` and is not ready for normal installation. See [MSIX developer details](../packaging/windows/README.md) for signing and platform prerequisites. Build the UI using `MailMeUp.Windows.slnx`; the original solution remains cross-platform.

Before distribution, validate the published, installed executable and alias with synthetic accounts and an isolated data directory, then clean install/update behavior. A successful package build alone does not validate OAuth, sharing, plugin loading or ARM64 execution.

The installed-desktop regression check creates a temporary schema-version-2 registry containing only an `example.test` account, launches the actual packaged executable and fails if the process exits during startup:

```powershell
$desktop = Join-Path (Get-AppxPackage MailMeUp.Desktop).InstallLocation 'MailMeUp.Desktop.exe'
python scripts/smoke-test-desktop.py $desktop
```

References: [Microsoft app execution aliases](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions#start-your-application-by-using-an-alias), [OpenAI MCP configuration](https://developers.openai.com/codex/mcp/).
