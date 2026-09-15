# Windows desktop MSIX

Source for the WinUI 3 setup app and the existing CLI in one MSIX. On 2026-09-06 Windows x64 preview `0.1.1.3` was built, signed with the owner's existing local certificate (`CN=umber`) and installed. CLI/MCP smoke checks passed through both the published executable and installed alias; the About UI and banner were inspected. See [validation](../../docs/VALIDATION.md) for the remaining limits. The main `MailMeUp.slnx` remains independent of Windows UI tooling; `MailMeUp.Windows.slnx` includes the desktop app.

After the owner authorizes a build, use Windows with the .NET 10 SDK and Windows SDK tools:

```powershell
pwsh -NoProfile -File scripts/package-msix.ps1
```

The script publishes self-contained .NET and Windows App SDK payloads, creates package logos from the existing artwork, copies dependency notices and calls MakeAppx. It does not run tests or launch MailMeUp. Restore creates or updates dedicated graphs under `eng/locks/msix/win-<architecture>/`, preserving the normal cross-platform and portable package lock files. Review the MSIX graph changes before committing. Windows x64 is the default; pass `-Architecture arm64` for an ARM64 package. ARM64 runtime support remains untested.

Output goes into `artifacts/msix/mailmeup-<version>-win-<architecture>/`. Before building, the script clears the entire repository `artifacts/` directory, including previous packages and logs. Copy anything you want to retain elsewhere first, and do not run builds concurrently in the same checkout. The default file ends in `.unsigned.msix` and cannot be installed normally. The script does not create certificates, change trust stores, install packages, contact accounts, register plugins, or publish releases.

To sign during an explicitly authorized package build, supply an existing code-signing certificate in `CurrentUser\My`, its exact subject as `Publisher`, and an RFC 3161 timestamp service:

```powershell
pwsh -NoProfile -File scripts/package-msix.ps1 -PackageVersion 0.1.2.0 -Publisher 'CN=Your signing identity' -CertificateThumbprint '<40 hexadecimal characters>' -TimestampServer 'https://your-timestamp-service.example'
```

Replace all example values. Successful signing also exports the public certificate to a `.cer` file beside the MSIX; it contains no private key and does not install trust. A signed package still needs a chain trusted by the target Windows device. Keep using the same signing identity for subsequent updates. Signing, installation, clean-device launch, provider login and upgrades require separate validation. `-MakeAppxPath` and `-SignToolPath` accept explicit Windows SDK tool paths when they cannot be discovered.

## Stable Codex command

Windows manages the versioned installation directory. The manifest registers the console execution alias `mailmeup.exe`; Codex must use that alias, never a path inside `C:\Program Files\WindowsApps`.

The setup app uses the absolute alias path under the current user's local application directory, normally `%LOCALAPPDATA%\Microsoft\WindowsApps\mailmeup.exe`, with `--stdio` as its argument. The `.exe` name, package identity and application IDs remain stable across upgrades. Windows resolves the alias to the currently installed version, so a normal update should not require another Codex configuration change. Disabling the alias, removing the app, changing the package identity, or a competing registration can make the command unavailable.

The hidden `Cli` application uses the console subsystem and allows multiple instances, so each MCP client starts its own stdio process. No UI process proxies MCP traffic. Redirected stdin/stdout, concurrent MCP clients and alias behavior after an upgrade remain required executable checks.

## Identity and local data

The source identity is `MailMeUp.Desktop` with publisher `CN=Umberto Giacobbi`. It is a development default, not an asserted signing certificate or Store identity. Select the actual signing identity before the first distributed installer and keep it consistent thereafter. Increase the four-part MSIX version for each update; the default derives from `Directory.Build.props` plus a final `.0`.

The locally installed `0.1.1.3` preview uses the explicit publisher override `CN=umber`. Updates to that installation must use a higher four-part version, keep `-Publisher 'CN=umber'` and retain the same signing identity. Changing to the source default would create a different package family and is not an in-place update.

File system write virtualization is disabled using `unvirtualizedResources`, so Codex can read the prepared plugin and the UI and CLI share the real `LocalAppData\MailMeUp` folder. An explicit `MAILMEUP_DATA_DIR` remains supported. Local data and the prepared plugin therefore survive MSIX removal; removing an account in MailMeUp remains a separate action. Registry virtualization is unchanged. No Microsoft Store acceptance is claimed for these restricted capabilities.

References: [execution aliases](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap5-appexecutionalias), [package identity](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/package-identity-overview), [self-contained deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps), [AppData virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization), [MakeAppx](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool).
