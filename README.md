# AgentInbox — Read and send email with your AI assistant.

[![CI status](https://github.com/umbertotechnopreneur/AgentInbox/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/umbertotechnopreneur/AgentInbox/actions/workflows/ci.yml)
![Platform: Windows 11](https://img.shields.io/badge/platform-Windows_11-0078D4)
![Status: in development](https://img.shields.io/badge/status-in_development-F6C453)
[![License: MIT](https://img.shields.io/badge/license-MIT-2EA44F)](LICENSE)

I kept switching between mailboxes to find the message I needed. I built AgentInbox to bring those inboxes into one conversation, using IMAP or POP3 to read mail and optional SMTP to send it, like a regular email client.

Connect a compatible mailbox, choose what your assistant can read, and ask for the messages you need. Sending is optional: enable SMTP for an account in Windows setup, then ask Codex to send a message.

**[Get started](docs/GETTING_STARTED.md)** · [Account settings](docs/AUTHENTICATION.md) · [Privacy](docs/PRIVACY.md) · [Development](docs/DEVELOPMENT.md)

> **Your assistant may send requested mail to its AI service.** AgentInbox runs locally; the whole conversation may not. Read the [privacy policy](docs/PRIVACY.md) before connecting accounts.

## Connect your mailbox

Choose your **Email provider** to see its instructions and fill in the settings. Gmail and iCloud need a separate password for this app; the setup page links to their instructions. Outlook.com and Microsoft 365 select **Sign in with Microsoft**: save changes, press that button, then enter the code on Microsoft's page. You do not need to enter an app ID or a second password for sending.

| Mail service | Connection requirement |
| --- | --- |
| Gmail | A Google app password; your usual Gmail password will not work here |
| iCloud Mail | An app-specific password |
| Other providers | IMAP or POP3 over TLS with a password or app password |
| Outlook.com / Microsoft 365 | Sign in with Microsoft. Outlook settings or your IT team may need to allow mail apps and sending |

Your provider or organization can block access even when the settings are correct. Choosing a Microsoft provider selects **Sign in with Microsoft**. A rejected connection does not change that choice. This app's Microsoft setup still needs a real sign-in check; see [signing in](docs/AUTHENTICATION.md).

IMAP exposes selected server folders and read status. POP3 exposes one mailbox as `INBOX`, has no unread flags or server folders, and searches message headers; its dates come from the email's `Date` header. AgentInbox sends no delete commands; provider retention settings can still affect your mailbox. Connections require TLS or required STARTTLS with certificate and hostname checks; there is no plaintext mode or custom-CA setting.

The Windows setup app uses WinUI 3. It keeps incoming mail and optional outgoing mail settings together, with separate connection checks. See [Windows setup](docs/WINDOWS_SETUP.md).

## Sending through Codex

SMTP starts disabled. When you enable it, Codex can send from that account through `send_email`. Give Codex the intended recipients and content; the tool submits immediately, without another confirmation in AgentInbox.

The Windows app only configures connections and permissions. Its SMTP test checks connection and sign-in without sending. A successful send result means the server accepted the message, not that the recipient received it. Unresolved sends are never retried automatically.

## Use it with your assistant

The Python engine runs as a local MCP process. It reads mail through IMAP or POP3 and submits optional SMTP sends. It does not need a .NET backend.

Source entry point:

```text
python -B python/run.py mcp
```

Use an absolute interpreter path and an absolute path to `python/run.py` in your MCP client. See [connection setup](docs/CODEX_SETUP.md) and the [tool contract](docs/MCP_CONTRACT.md).

Use it with Codex, Claude or another local MCP client.

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

- [**AgentInbox**](https://github.com/umbertotechnopreneur/AgentInbox) · Read and send email with your AI assistant.
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
