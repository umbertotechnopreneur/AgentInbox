# Windows desktop MSIX

Source for the WinUI 3 setup app and the existing CLI in one MSIX. On 2026-09-06 Windows x64 preview `0.1.1.3` was built, signed with the owner's earlier local certificate (`CN=umber`) and installed. CLI/MCP smoke checks passed through both the published executable and installed alias; the About UI and banner were inspected. That earlier package family cannot update the current Store identity. See [validation](../../docs/VALIDATION.md) for the remaining limits. The main `MailMeUp.slnx` remains independent of Windows UI tooling; `MailMeUp.Windows.slnx` includes the desktop app.

After the owner authorizes a build, use Windows with the .NET 10 SDK and Windows SDK tools:

```powershell
pwsh -NoProfile -File scripts/package-msix.ps1
```

The script defaults to the local `Debug` channel and Windows x64. It publishes self-contained .NET and Windows App SDK payloads, creates package logos from the existing artwork, copies dependency notices and calls MakeAppx. It does not run tests or launch AgentInbox. Restore creates or updates dedicated graphs under `eng/locks/msix/win-<architecture>/`, preserving the normal cross-platform and portable package lock files. Review the MSIX graph changes before committing. Pass `-Architecture arm64` for an ARM64 package. ARM64 runtime support remains untested. Use `-Channel Store` only for an explicitly authorized Store package; it uses the `Release` configuration but does not upload or publish anything.

Output goes into `artifacts/msix/<debug|store>/<version>/<architecture>/`. Before building, the script clears the entire repository `artifacts/` directory, including previous packages and logs. Copy anything you want to retain elsewhere first, and do not run builds concurrently in the same checkout. Signed packaging requires an explicit matching certificate and timestamp server; use `-Unsigned` only when an unsigned staging package is intentional. The script does not create certificates, change trust stores, install packages, contact accounts, register plugins, or publish releases.

To sign during an explicitly authorized package build, supply an existing code-signing certificate in `CurrentUser\My` and an RFC 3161 timestamp service. The script requires its exact subject to match the source manifest publisher:

```powershell
pwsh -NoProfile -File scripts/package-msix.ps1 -Channel Store -Version 0.1.2.0 -CertificateThumbprint '<40 hexadecimal characters>' -TimestampServer 'https://your-timestamp-service.example'
```

Replace all example values. Successful signing also exports the public certificate to a `.cer` file beside the MSIX; it contains no private key and does not install trust. A signed package still needs a chain trusted by the target Windows device. Keep using the manifest signing identity for subsequent updates. Signing, installation, clean-device launch, provider login and upgrades require separate validation. `-MakeAppxPath` and `-SignToolPath` accept explicit Windows SDK tool paths when they cannot be discovered.

## Stable Codex command

Windows manages the versioned installation directory. The manifest registers the console execution alias `agentinbox.exe`; Codex must use that alias, never a path inside `C:\Program Files\WindowsApps`.

The setup app uses the absolute alias path under the current user's local application directory, normally `%LOCALAPPDATA%\Microsoft\WindowsApps\agentinbox.exe`, with `--stdio` as its argument. The `.exe` name, package identity and application IDs remain stable across upgrades. Windows resolves the alias to the currently installed version, so a normal update should not require another Codex configuration change. Disabling the alias, removing the app, changing the package identity, or a competing registration can make the command unavailable.

The hidden `Cli` application uses the console subsystem and allows multiple instances, so each MCP client starts its own stdio process. No UI process proxies MCP traffic. Redirected stdin/stdout, concurrent MCP clients and alias behavior after an upgrade remain required executable checks.

## Identity and local data

The source identity is `UmbertoGiacobbiDotBiz.AgentInbox` with publisher `CN=82BCDD1C-1D59-48A0-9BDF-6352BE319510`, display name `UmbertoGiacobbiDotBiz` and Store ID `9N392ZMBJ4D1`. The package script preserves that identity and only varies the four-part version and target architecture. The default version derives from `Directory.Build.props` plus a final `.0`; increase it for every update. Store links become available only after the product is live.

File system write virtualization is disabled using `unvirtualizedResources`, so Codex can read the prepared plugin and the UI and CLI share the real `LocalAppData\AgentInbox` folder. An explicit `AGENTINBOX_DATA_DIR` remains supported. Local data and the prepared plugin therefore survive MSIX removal; removing an account in AgentInbox remains a separate action. Registry virtualization is unchanged. No Microsoft Store acceptance is claimed for these restricted capabilities.

References: [execution aliases](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap5-appexecutionalias), [package identity](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/package-identity-overview), [self-contained deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps), [AppData virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization), [MakeAppx](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool).
