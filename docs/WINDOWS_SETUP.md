# Windows setup preview

The Windows app walks you through connecting accounts, choosing what to share and adding AgentInbox to Codex. It's a native Windows app, packaged as an MSIX installer.

**The latest recorded local installation is AgentInbox Debug x64 `0.0.1.1`, from September 22.** It was built, signed with the approved Store publisher, and installed successfully after a fresh `0.0.1.0` package. The current source passed 495 automated tests, published and installed CLI/MCP smoke, and published and installed desktop startup checks with synthetic isolated data.

Native layout and interaction, installation on a clean machine, sign-in through the Windows app and the guided Codex setup still need checks. See the [test record](VALIDATION.md) for exact results and limits.

The current app redesigns all four screens with fixed headings and a smaller sidebar. **Add account** and account sharing open centered dialogs; the Sharing list summarizes each account's saved choices. **Search and read settings** has Search, Usage and Limits tabs, and Codex keeps diagnostics behind **Connection details**. Scrollbars hide outside the active window, with keyboard, touch and high-contrast support. The source is built and installed; these native interactions still need visual checks.

The final Codex step shows plugin/direct connection choices, instructions for the selected method and **Check again** after you make changes in Codex. Unresolved setup offers **Finish later**; confirmed local setup offers a copyable first-task prompt and **Finish setup**. Plugin maintenance, detailed checks and manual setup sit in an expandable section. Synthetic state tests passed; live Codex setup and native interaction remain untested.

## Setup

Open AgentInbox and follow the four screens. The app is designed to bring the existing setup window forward if you launch it again; that behavior still needs a desktop check.

1. **Welcome:** read how AgentInbox accesses your accounts and what may reach your assistant's AI service. Sign-in tokens stay protected on your device.
2. **Connect accounts:** choose **Add account**, then Google or Microsoft and sign in through your browser. Repeat for more accounts. For now, you'll need [your own app registration](APP_REGISTRATION.md): import Google's Desktop client JSON file or enter Microsoft's Application (client) ID.
3. **Choose what to share:** select an account row to open its sharing dialog. Turn on sharing, then choose mail, calendars or both. You can share all calendars or load their names and pick individual ones. Save each account's choices. New accounts start with sharing off; reconnecting keeps your saved choices.
4. **Connect to Codex:** choose the recommended local plugin or a direct connection and follow the inline instructions. Make any changes to existing connections in Codex, then select **Check again**. When local setup is ready, copy the first-task prompt to try in a new Codex task. **Finish later** leaves unresolved setup for another time. See [Codex setup](CODEX_SETUP.md).

On **Accounts**, use **Check access** (**Check read access** in earlier packages) to try sample searches and detail reads for mail and calendars. A **Try to reconnect** action appears when a read failure calls for it. A lack of sample messages or appointments isn't treated as a broken connection. Longer explanations go to the local [diagnostic log](LOGGING.md). The account menu also offers Reconnect and Remove from device.

Open **Sharing → Search and read settings → Search** to choose how far back undated searches should go: 1–365 days, starting at 14. Earlier packages show the period directly on Sharing. Longer periods take more time and may reach Google's or Microsoft's request limits. Dates in your request take priority. Save or discard edits before closing settings. The current dialog and its interactions still need visual checking.

Closing the setup window leaves Codex's AgentInbox connection running. Sharing changes apply to later reads, including requests to open earlier results. They can't take back information already returned in a conversation. If you didn't allow mail or calendar access when signing in, reconnect with that option selected before sharing it here.

The app is in English. The sidebar groups information under **Help & about**. **Privacy & terms** gives you links to the AgentInbox, Google and Microsoft policies. **About & Support** shows your installed version and links to the project, its creator and GitHub issues. You can copy a short version/platform summary for a support request. Opening that dialog doesn't launch a browser or send information.

## Keep Codex connected after updates

Windows puts each package version in a different folder. AgentInbox provides a command shortcut, called an app execution alias, so Codex can keep using the same path:

```text
%LOCALAPPDATA%\Microsoft\WindowsApps\agentinbox.exe --stdio
```

The plugin fills in the full path for your user. Windows points it to the installed version, as long as updates keep the same package identity, publisher and alias. Reload Codex to start a new AgentInbox process after an update. If the command is disabled or another app uses it, select AgentInbox under Windows **App execution aliases**.

## Your data when updating or uninstalling

The setup window and command-line app share the same data folder: `AGENTINBOX_DATA_DIR` if you set it, or the usual per-user `AgentInbox` folder. The package is configured so both apps and Codex can still access the local files they need. Registry handling is unchanged.

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
$desktop = Join-Path (Get-AppxPackage UmbertoGiacobbiDotBiz.AgentInbox).InstallLocation 'AgentInbox.Desktop.exe'
python scripts/smoke-test-desktop.py $desktop
```

References: [Microsoft app execution aliases](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions#start-your-application-by-using-an-alias), [OpenAI MCP configuration](https://developers.openai.com/codex/mcp/).
