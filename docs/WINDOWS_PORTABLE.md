# AgentInbox for Windows — portable preview

The ZIP includes the Windows setup app, the command-line app, MCP tools and their runtimes. You do not need to install the MSIX or the .NET SDK.

1. Extract the **entire ZIP** into a folder you want to keep, such as `C:\Tools\AgentInbox`. Do not run the app from inside the ZIP.
2. Open **AgentInbox.Desktop.exe** to connect accounts, choose what to share and configure Codex.
3. Keep the `cli`, `Assets`, `CodexPlugin` and runtime folders beside the app. The command-line executable is `cli\agentinbox.exe`.

For manual Codex setup, replace this example with the full path on your PC:

```powershell
codex mcp add agentinbox -- 'C:\Tools\AgentInbox\cli\agentinbox.exe' --stdio
```

Choose one Codex connection method: the local plugin prepared by the app, or a direct MCP entry. The portable app uses the executable in its own folder. If you move that folder later, update your Codex connection before using it again.

**Portable means no application installation.** By default, account settings and protected sign-in data still belong to your Windows user profile; copying the ZIP to another computer does not copy your connected accounts. `AGENTINBOX_DATA_DIR` can select a different local data folder. It is not included in this download.

Each person registers their own Google or Microsoft app before connecting accounts. Follow the [registration guide](https://github.com/umbertotechnopreneur/AgentInbox/blob/main/docs/APP_REGISTRATION.md).

AgentInbox only reads mail and calendars. Information you request may reach your AI assistant's service. It cannot send mail, change messages or create appointments.

This is an early, unsigned preview for Windows. The ZIP does not require importing the test certificate used for the separate MSIX installer. Windows x64 is the current test platform; ARM64 packages are build-only until tested on ARM64 hardware. `BUILD_INFO.txt` identifies the exact source commit and the checks performed by packaging. Clean-machine installation, provider sign-in and full desktop interaction are separate checks.

[Documentation and source](https://github.com/umbertotechnopreneur/AgentInbox). MIT license; dependency notices are included.
