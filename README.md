# AgentInbox — Read and send email with your AI assistant.

I kept switching between mailboxes to find the message I needed. I built AgentInbox to bring those inboxes into one conversation, using IMAP or POP3 to read mail and optional SMTP to send it, like a regular email client.

Connect a compatible mailbox, choose what your assistant can read, and ask for the messages you need. Sending is optional: you enable SMTP for an account, then approve each complete message locally before it leaves your computer.

**[Get started](docs/GETTING_STARTED.md)** · [Account settings](docs/AUTHENTICATION.md) · [Privacy](docs/PRIVACY.md) · [Development](docs/DEVELOPMENT.md)

[![MIT](https://img.shields.io/badge/license-MIT-71DEB7)](LICENSE)
![Project status: development](https://img.shields.io/badge/status-in_development-F6C453)

> **In development.** All 77 synthetic Python tests and nine isolated Codex setup checks passed locally on Windows. Debug MSIX 1.0.9.0 is installed on Windows x64; account-form navigation, resizing and error recovery were exercised with an isolated profile. Real-provider access and native credential storage remain unvalidated. See [validation](docs/VALIDATION.md).

> **Your assistant may send requested mail to its AI service.** AgentInbox runs locally; the whole conversation may not. Read the [privacy policy](docs/PRIVACY.md) before connecting accounts.

## Connect your mailbox

Choose IMAP or POP3, then enter the server, port, encrypted connection type and username. Password or app password is the default. For Outlook.com or Microsoft 365, you can explicitly choose **Web authorization**: Microsoft gives you a short code to sign in. This choice requires your own Microsoft app registration.

| Mail service | Connection requirement |
| --- | --- |
| Gmail | An eligible account with an app password and the chosen mail protocol allowed; Web authorization is not available for Google accounts |
| iCloud Mail | An app-specific password |
| Other providers | IMAP or POP3 over TLS with a password or app password |
| Outlook.com / Microsoft 365 | Outlook.com requires Web authorization with Microsoft; Exchange Online no longer accepts ordinary passwords for IMAP and POP3. This choice requires your own app registration; organization rules can still restrict access |

Your provider or organization can disable access even when the server settings are correct. AgentInbox reports the problem without automatically switching sign-in methods. Presets fill settings; no provider connection has been validated for this implementation.

IMAP exposes selected server folders and read status. POP3 exposes one mailbox as `INBOX`, has no unread flags or server folders, and searches message headers; its dates come from the email's `Date` header. AgentInbox sends no delete commands; provider retention settings can still affect your mailbox. Connections require TLS or required STARTTLS with certificate and hostname checks; there is no plaintext mode or custom-CA setting.

The Windows setup app uses WinUI 3. It keeps incoming mail and optional outgoing mail settings together, with separate connection checks. See [Windows setup](docs/WINDOWS_SETUP.md).

## Keep control of sending

SMTP starts disabled. Enabling it lets your assistant prepare a message for review; it does not authorize delivery.

Before every send, AgentInbox shows the sending account, all recipients, subject, body and attachments. You approve that exact message locally. Changing its content requires a new approval. The assistant cannot approve a message through MCP.

A test email follows the same confirmation step. A successful SMTP response means the server accepted the message, not that the recipient received it. An uncertain send is not retried automatically.

## Use it with your assistant

The Python engine runs as a local MCP process. It reads mail through IMAP or POP3 and prepares optional SMTP sends. It does not need a .NET backend.

Source entry point:

```text
python -B python/run.py mcp
```

Use an absolute interpreter path and an absolute path to `python/run.py` in your MCP client. See [connection setup](docs/CODEX_SETUP.md) and the [tool contract](docs/MCP_CONTRACT.md).

Codex, Claude and other local MCP clients are intended connection options. Client integration has not been validated for this implementation.

## Where it runs

- **Windows 11 x64 and ARM64:** native WinUI 3 setup; commercial distribution is MSIX only.
- **Linux and macOS:** Python source only, for users who can configure and adapt it. Native Keychain and Secret Service paths remain untested. No compiled distributions or commercial support are provided. Contributor pull requests with fixes and clear validation evidence are welcome.

The engine requires Python 3.11 or newer and uses the standard library. On Windows, passwords and Microsoft refresh tokens use Windows Credential Manager; temporary outgoing-message and sign-in blobs use current-user DPAPI. There is no plain-text credential fallback. See [dependencies](docs/DEPENDENCIES.md).

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

When first introducing an external library or prebuilt binary, scan its archive and extracted files/dependencies once with targeted Microsoft Windows Defender. Keep the import staged until the scan finishes. A new version or changed third-party content is a new import; reuse the evidence for unchanged files. Builds, tests, packaging and installation do not trigger scans, and installed SDK/system trees are outside this import check.

Proceed only after the scan completes without detections. Record the official source, version or SHA-256, scan command and result. A missing, failed, unfinished or unavailable scan blocks the new import. Never bypass detections with exclusions, allowlisting, restored quarantine items or disabled protection. This first-import requirement does not certify existing dependencies.
