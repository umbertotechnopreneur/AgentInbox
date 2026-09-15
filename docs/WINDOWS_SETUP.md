# Windows setup preview

The Windows app walks you through connecting accounts, choosing what to share and adding MailMeUp to Codex. It's a native Windows app, packaged as an MSIX installer.

**The latest recorded local installation is `0.1.1.20`, from September 12.** It was built, signed and installed successfully. It includes the updated setup screens and a demo with sample accounts. The preceding local preview passed 291 automated tests and basic CLI/MCP checks; desktop interaction checks are still pending.

The newer read-limit code hasn't been built, tested or installed. Installation on a clean machine, sign-in through the Windows app and the guided Codex setup also need checks. See the [test record](VALIDATION.md) for earlier versions and exact results.

The September 15 source redesigns all four screens with fixed headings and a smaller sidebar. **Add account** and account sharing open centered dialogs; the Sharing list summarizes each account's saved choices. **Search and read settings** has Search, Usage and Limits tabs, and Codex keeps diagnostics behind **Connection details**. Scrollbars hide outside the active window, with keyboard, touch and high-contrast support. These source changes have not been built, tested or installed.

## Setup

Open MailMeUp and follow the four screens. The app is designed to bring the existing setup window forward if you launch it again; that behavior still needs a desktop check.

1. **Welcome:** read how MailMeUp accesses your accounts and what may reach your assistant's AI service. Sign-in tokens stay protected on your device.
2. **Connect accounts:** in the newer source, choose **Add account**, then Google or Microsoft and sign in through your browser. Repeat for more accounts. For now, you'll need [your own app registration](APP_REGISTRATION.md): import Google's Desktop client JSON file or enter Microsoft's Application (client) ID.
3. **Choose what to share:** in the newer source, select an account row to open its sharing dialog. Turn on sharing, then choose mail, calendars or both. You can share all calendars or load their names and pick individual ones. Save each account's choices. New accounts start with sharing off; reconnecting keeps your saved choices.
4. **Connect to Codex:** check the setup status and install the included local plugin. If automatic setup isn't available, the page gives you manual steps. If you've already added MailMeUp directly to Codex, choose whether to keep that connection or switch to the plugin. See [Codex setup](CODEX_SETUP.md).

On **Accounts**, use **Check access** (**Check read access** in earlier packages) to try sample searches and detail reads for mail and calendars. A **Try to reconnect** action appears when a read failure calls for it. A lack of sample messages or appointments isn't treated as a broken connection. Longer explanations go to the local [diagnostic log](LOGGING.md). The account menu also offers Reconnect and Remove from device.

In the newer source, open **Sharing → Search and read settings → Search** to choose how far back undated searches should go: 1–365 days, starting at 14. Earlier packages show the period directly on Sharing. Longer periods take more time and may reach Google's or Microsoft's request limits. Dates in your request take priority. Save or discard edits before closing settings. The setting was added in `0.1.1.19`; the new dialog and its interactions still need checking.

Closing the setup window leaves Codex's MailMeUp connection running. Sharing changes apply to later reads, including requests to open earlier results. They can't take back information already returned in a conversation. If you didn't allow mail or calendar access when signing in, reconnect with that option selected before sharing it here.

The app is in English. The newer sidebar groups information under **Help & about**. **Privacy & terms** gives you links to the MailMeUp, Google and Microsoft policies. **About & Support** shows your installed version and links to the project, its creator and GitHub issues. You can copy a short version/platform summary for a support request. Opening that dialog doesn't launch a browser or send information.

## Keep Codex connected after updates

Windows puts each package version in a different folder. MailMeUp provides a command shortcut, called an app execution alias, so Codex can keep using the same path:

```text
%LOCALAPPDATA%\Microsoft\WindowsApps\mailmeup.exe --stdio
```

The plugin fills in the full path for your user. Windows points it to the installed version, as long as updates keep the same package identity, publisher and alias. Reload Codex to start a new MailMeUp process after an update. If the command is disabled or another app uses it, select MailMeUp under Windows **App execution aliases**.

## Your data when updating or uninstalling

The setup window and command-line app share the same data folder: `MAILMEUP_DATA_DIR` if you set it, or the usual per-user `MailMeUp` folder. The package is configured so both apps and Codex can still access the local files they need. Registry handling is unchanged.

Updates keep your local data. Uninstalling the Windows package leaves the data folder and any installed Codex plugin in place. If you want to remove local sign-in tokens too, remove your accounts and the Codex plugin before uninstalling. Revoke the app's access separately in Google or Microsoft account settings.

## For developers: build the installer

The [Windows packaging script](../scripts/package-msix.ps1) builds the desktop and command-line apps with the .NET runtime included, creates installer logos and adds dependency notices. It can sign the package with an existing certificate. It doesn't run tests, install the result, change certificate trust or publish a release.

```powershell
pwsh -NoProfile -File scripts/package-msix.ps1 -Architecture x64
```

Without signing options, the file ends in `.unsigned.msix` and isn't ready for normal installation. See [packaging details](../packaging/windows/README.md) for signing and requirements. Use `MailMeUp.Windows.slnx` to build the UI; the original solution keeps the shared cross-platform code.

Before distributing a package, the installed app and command alias need checks with made-up accounts and a separate data folder, followed by clean installation and update checks. A package that builds successfully can still have problems with sign-in, sharing, plugin loading or ARM64 execution. Run these checks only when the owner asks.

The desktop startup check below creates a temporary version-2 account database with one `example.test` account. It launches the installed executable and fails if the process exits during startup:

```powershell
$desktop = Join-Path (Get-AppxPackage MailMeUp.Desktop).InstallLocation 'MailMeUp.Desktop.exe'
python scripts/smoke-test-desktop.py $desktop
```

References: [Microsoft app execution aliases](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions#start-your-application-by-using-an-alias), [OpenAI MCP configuration](https://developers.openai.com/codex/mcp/).
