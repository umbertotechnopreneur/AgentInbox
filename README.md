# Connect unlimited mailboxes (Gmail, iCloud, Microsoft, or custom) to your AI assistant via MCP.

[![CI status](https://github.com/umbertotechnopreneur/AgentInbox/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/umbertotechnopreneur/AgentInbox/actions/workflows/ci.yml)
![Platform: Windows 11](https://img.shields.io/badge/platform-Windows_11-0078D4)
![Status: in development](https://img.shields.io/badge/status-in_development-F6C453)
[![License: MIT](https://img.shields.io/badge/license-MIT-2EA44F)](LICENSE)

I could connect only one mailbox at a time to my AI assistant. I built AgentInbox so I could use all my mailboxes in the same conversation.

Add your personal, work and custom-domain accounts to one local MCP connection. There is no fixed mailbox-count limit. Choose which accounts your assistant can read and which it can send from, then ask for what you need.

**[Get started](docs/GETTING_STARTED.md)** · [Account settings](docs/AUTHENTICATION.md) · [Privacy](docs/PRIVACY.md) · [Development](docs/DEVELOPMENT.md)

> **Your assistant may send requested mail to its AI service.** AgentInbox runs locally; the whole conversation may not. Read the [privacy policy](docs/PRIVACY.md) before connecting accounts.

## Bring your mailboxes into one conversation

1. Open **Accounts** in Windows setup and choose **Add** for each mailbox you want to connect.
2. Choose Gmail, iCloud, Microsoft or a custom provider, then follow its sign-in instructions.
3. Enable reading and optional sending separately for each account. Use **Test All** to check your saved connections.
4. Open **Connect Codex** for guided setup, or configure another local MCP client manually. One connection exposes the accounts you enable.

Start by asking your assistant:

> Which email accounts can you access through AgentInbox?

Then name the accounts you want it to use:

> Find messages about invoices in my work and personal mailboxes from the past week.

Your mailboxes stay with their providers. AgentInbox gives your assistant tools to search and read the accounts you choose, and to send messages when you request them.

## Connect Gmail, iCloud, Microsoft or a custom provider

Choose your **Email provider** to see its instructions and fill in the settings. Gmail and iCloud need a separate password for this app; the setup page links to their instructions. Outlook.com and Microsoft 365 select **Sign in with Microsoft**: press that button, then enter the code on Microsoft's page. Windows setup saves the account settings first. You do not need to enter an app ID or a second password for sending.

| Mail service | Connection requirement |
| --- | --- |
| Gmail | A Google app password; your usual Gmail password will not work here |
| iCloud Mail | An app-specific password |
| Custom providers | IMAP or POP3 over TLS with a password or app password |
| Outlook.com / Microsoft 365 | Sign in with Microsoft. Outlook settings or your IT team may need to allow mail apps and sending |

Your provider or organization can block access even when the settings are correct. Choosing a Microsoft provider selects **Sign in with Microsoft**. A rejected connection does not change that choice. This app's Microsoft setup still needs a real sign-in check; see [signing in](docs/AUTHENTICATION.md).

IMAP exposes selected server folders and read status. POP3 exposes one mailbox as `INBOX`, has no unread flags or server folders, and searches message headers; its dates come from the email's `Date` header. AgentInbox sends no delete commands; provider retention settings can still affect your mailbox. Connections require TLS or required STARTTLS with certificate and hostname checks; there is no plaintext mode or custom-CA setting.

The Windows setup app uses WinUI 3. It keeps incoming mail and optional outgoing mail settings together, with separate connection checks. See [Windows setup](docs/WINDOWS_SETUP.md).

## Send from the account you choose

Sending starts disabled for every account. Enable SMTP only for the accounts you want your assistant to send from. When you request a message, specify the sending account, recipients and content. The `send_email` tool submits immediately, without another confirmation in AgentInbox.

The Windows app only configures connections and permissions. Its SMTP test checks connection and sign-in without sending. A successful send result means the server accepted the message, not that the recipient received it. Unresolved sends are never retried automatically.

## One local MCP connection for your enabled accounts

Your assistant lists your enabled accounts and calls the mail tools for each account you want it to use. It can work with several mailboxes in the same conversation. The Python engine runs locally, using IMAP or POP3 for reading and optional SMTP for sending. It does not need a .NET backend.

Source entry point:

```text
python -B python/run.py mcp
```

Use an absolute interpreter path and an absolute path to `python/run.py` in your MCP client. See [connection setup](docs/CODEX_SETUP.md) and the [tool contract](docs/MCP_CONTRACT.md).

The Windows app guides you through connecting Codex. For another assistant, configure its local MCP connection manually using the same account profile.

## Where it runs

- **Windows 11 x64 and ARM64:** native WinUI 3 setup; commercial distribution is MSIX only.
- **Linux and macOS:** Python source only, for users who can configure and adapt it. No compiled distributions or commercial support are provided. Contributor pull requests with fixes are welcome.

The engine requires Python 3.11 or newer and uses the standard library. On Windows, passwords and Microsoft refresh tokens use Windows Credential Manager; temporary sign-in blobs use current-user DPAPI. There is no plain-text credential fallback. See [dependencies](docs/DEPENDENCIES.md).

## Explore the project

- [Getting started](docs/GETTING_STARTED.md)
- [Accounts and passwords](docs/AUTHENTICATION.md)
- [Connection troubleshooting](docs/RECOVERY.md)
- [MCP tools](docs/MCP_CONTRACT.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Build and contribute](docs/DEVELOPMENT.md)
- [Privacy](docs/PRIVACY.md) and [terms](docs/TERMS.md)

[MIT license](LICENSE). [Contributions](CONTRIBUTING.md) and [security reports](SECURITY.md) are welcome. Independent project; no provider or assistant vendor endorsement is implied.

## More from MeUp

- [**AgentInbox**](https://github.com/umbertotechnopreneur/AgentInbox) · Connect your mailboxes to your AI assistant via MCP.
- [**PromptMeUp**](https://github.com/umbertotechnopreneur/PromptMeUp) · Can't remember that command? Git, Bash, or PowerShell: describe what you want to do. hm is here to help.
- [**WorkTrail**](https://github.com/umbertotechnopreneur/WorkTrail) · Track your time. Find what you worked on.

I’m Umberto Giacobbi. I built and maintain AgentInbox, with help from contributors. Find me on [LinkedIn](https://www.linkedin.com/in/umbertogiacobbi/) or at [umbertogiacobbi.biz](https://umbertogiacobbi.biz/).

Artwork and writing style: [brand guide](docs/BRAND.md).

<a id="external-binary-security-policy"></a>

## A note on security

I care about what goes into this project. Before I add a third-party library or prebuilt binary, including files supplied without source code, I scan the downloaded package, its extracted files and any dependencies with Microsoft Windows Defender. I keep new imports aside until the scan finishes without detections.

I keep a record of where each new import came from and the scan result. If a scan cannot finish or flags something, I leave it out.

---

## VibeWare

AgentInbox participates in [VibeWare](https://umbertogiacobbi.biz/vibeware/manifesto), my open initiative for software built with AI under human direction. I define what the app should do, guide the work and take responsibility for checking the result. The mark describes this approach; the [validation record](docs/VALIDATION.md) shows what has actually been checked. You can explore and reuse the open-source materials under their licenses.

[Terms](https://umbertogiacobbi.biz/agentinbox/terms/) · [Privacy](https://umbertogiacobbi.biz/agentinbox/privacy/)

<p align="right">
  <a href="https://umbertogiacobbi.biz/vibeware/manifesto">
    <img src="resources/vibeware/vibeware-logo.png" alt="AgentInbox participates in VibeWare. Read the manifesto." width="200">
  </a><br>
  <em>Human intent, AI execution, and plenty of tokens</em>
</p>
