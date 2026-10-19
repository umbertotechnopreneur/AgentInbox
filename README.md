# AgentInbox — Read and send email with your AI assistant.

I kept switching between mailboxes to find the message I needed. I built AgentInbox to bring those inboxes into one conversation, using the same IMAP and SMTP connections as a regular email client.

Connect a compatible mailbox, choose what your assistant can read, and ask for the messages you need. Sending is optional: you enable SMTP for an account, then approve each complete message locally before it leaves your computer.

**[Get started](docs/GETTING_STARTED.md)** · [Account settings](docs/AUTHENTICATION.md) · [Privacy](docs/PRIVACY.md) · [Development](docs/DEVELOPMENT.md)

[![MIT](https://img.shields.io/badge/license-MIT-71DEB7)](LICENSE)
![Project status: development](https://img.shields.io/badge/status-in_development-F6C453)

> **This implementation is in development and unvalidated.** No build, runtime check or package installation has validated the Python engine and Windows setup changes. See [validation](docs/VALIDATION.md).

> **Your assistant may send requested mail to its AI service.** AgentInbox runs locally; the whole conversation may not. Read the [privacy policy](docs/PRIVACY.md) before connecting accounts.

## Connect your mailbox

The setup asks for your incoming IMAP server, port, encrypted connection type, username and password or app password. A provider preset can fill in server details; you still need an account that permits password-based access.

| Mail service | Connection requirement |
| --- | --- |
| Gmail | An eligible account with an app password and IMAP access allowed |
| iCloud Mail | An app-specific password |
| Other providers | IMAP with TLS and a password or app password |
| Outlook.com / Microsoft 365 | Unsupported by this password-based implementation; setup rejects these presets |

Your provider or organization can disable access even when the server settings are correct. AgentInbox reports the problem without trying a different sign-in method.

The Windows setup app uses WinUI 3. It keeps incoming mail and optional outgoing mail settings together, with separate connection checks. See [Windows setup](docs/WINDOWS_SETUP.md).

## Keep control of sending

SMTP starts disabled. Enabling it lets your assistant prepare a message for review; it does not authorize delivery.

Before every send, AgentInbox shows the sending account, all recipients, subject, body and attachments. You approve that exact message locally. Changing its content requires a new approval. The assistant cannot approve a message through MCP.

A test email follows the same confirmation step. A successful SMTP response means the server accepted the message, not that the recipient received it. An uncertain send is not retried automatically.

## Use it with your assistant

The Python engine runs as a local MCP process. It searches mail through IMAP and prepares optional SMTP sends. It does not need a .NET backend.

Source entry point:

```text
python -B python/run.py mcp
```

Use an absolute interpreter path and an absolute path to `python/run.py` in your MCP client. See [connection setup](docs/CODEX_SETUP.md) and the [tool contract](docs/MCP_CONTRACT.md).

Codex, Claude and other local MCP clients are intended connection options. Client integration has not been validated for this implementation.

## Where it runs

- **Windows 11 x64 and ARM64:** native WinUI 3 setup; commercial distribution is MSIX only.
- **Linux and macOS:** Python source only, for users who can configure and adapt it. No compiled distributions or commercial support are provided. These platforms have not been validated.

The engine requires Python 3.11 or newer and uses the standard library. Credentials require operating-system protected storage. There is no plain-text fallback. See [dependencies](docs/DEPENDENCIES.md).

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

## VibeWare

I coined [VibeWare](https://umbertogiacobbi.biz/vibeware/manifesto) for software built with AI and human responsibility. I guide the work, review the result and state what has actually been tested. You are welcome to explore and reuse the open-source materials under their licenses.

## External binary security policy

Before importing, integrating, copying or running an external library or prebuilt binary in this repository or a deliverable, run an explicit targeted Microsoft Windows Defender scan on the downloaded package or archive and the extracted files to be used, including required third-party dependencies. This includes components supplied without source code. Keep the files staged until scanning finishes; real-time protection alone is not sufficient.

Proceed only after the scan completes without detections. Record the official source, version or SHA-256, scan command and result. An unavailable, disabled, failed or unfinished scan, or any detection, blocks the import. Do not bypass detections with exclusions, allowlisting, restored quarantine items or disabled protection. Any exception requires separate explicit owner approval. This requirement applies to future imports; it does not certify existing dependencies.
