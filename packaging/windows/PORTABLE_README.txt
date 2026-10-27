AgentInbox for Windows 11 - portable Release

Connect your compatible mailboxes to your AI assistant through one local MCP connection.

1. Extract the whole ZIP into a folder you can keep.
2. Open AgentInbox.Desktop.exe to configure your accounts and permissions.
3. Use Connect Codex, or copy the connection settings for another local MCP client.

Keep the runtime, Python, Assets and other folders beside the executable. The ZIP
includes the .NET, Windows App SDK and Python runtimes; no system Python is needed.

For terminal setup, open a terminal in this folder and run:
  AgentInbox.cmd accounts add --help
For an MCP client that accepts a direct executable, use runtime/python.exe with:
  -B -m agentinbox mcp

Settings stay in your Windows user profile (LocalAppData/AgentInbox). Passwords and
Microsoft refresh credentials stay in Windows Credential Manager. An explicit
absolute AGENTINBOX_DATA_DIR can select a different settings profile; credentials
remain protected by Windows. The ZIP never contains your accounts or credentials.
Portable means no installer. It does not copy your profile between computers.
If you move this folder, update your assistant's launcher path or reconnect it.

Reading and sending permissions are separate. SMTP starts disabled. An assistant
send request submits immediately; server acceptance does not prove delivery.
Provider compatibility does not guarantee that your provider permits access.

This GitHub ZIP is a Release build, separate from local Debug MSIX installations
and Microsoft Store MSIX distribution. No MSI installer is included.

Project and setup guides:
  https://github.com/umbertotechnopreneur/AgentInbox
Privacy:
  https://github.com/umbertotechnopreneur/AgentInbox/blob/main/docs/PRIVACY.md

License: MIT. See LICENSE, THIRD_PARTY_NOTICES.md and licenses/ for bundled notices.
