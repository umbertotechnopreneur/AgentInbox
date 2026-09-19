<p align="center">
  <img src="docs/assets/branding/mailmeup-hero.png" alt="MailMeUp: several inboxes, one conversation" width="100%" />
</p>

# MailMeUp

**All your inboxes. One conversation.**

Work email here, personal email there, client messages somewhere else. MailMeUp lets you search your Google and Microsoft accounts and check your calendars from one conversation with your AI assistant.

It runs on your computer. You choose which accounts to share, and it only reads: it can't send emails, change messages or edit your calendar. Setup currently focuses on Codex.

[![CI](https://github.com/umbertotechnopreneur/MailMeUp/actions/workflows/ci.yml/badge.svg)](https://github.com/umbertotechnopreneur/MailMeUp/actions/workflows/ci.yml)
[![MIT](https://img.shields.io/badge/license-MIT-71DEB7)](LICENSE)
[![Project status: pre-alpha](https://img.shields.io/badge/status-pre--alpha-F6C453)](docs/ROADMAP.md)

> [!WARNING]
> **MailMeUp is still an early preview.** Try it while keeping an eye on the results. Commands, setup steps and local data formats may change before a stable release.

> **Your assistant may send the information you request to its AI service.** MailMeUp runs locally, but that doesn't make the whole conversation offline. See [privacy](docs/PRIVACY.md).

## A look at setup

![MailMeUp setup flow: Welcome and connect accounts](docs/assets/branding/mailmeup-setup-flow-welcome-accounts.png)

![MailMeUp setup flow: choose what to share and connect to Codex](docs/assets/branding/mailmeup-setup-flow-sharing-codex.png)

> [!NOTE]
> These illustrations show the four setup steps. They're based on the Windows app, but aren't screenshots of the current version.

## Why MailMeUp?

MailMeUp starts with a practical problem: making several independent mailboxes available to an AI assistant in one conversation. The goal is to make a setup such as **ten Microsoft accounts, ten Google accounts, or a mix of both** easy to connect and search, with each account signed in separately and the user choosing what to share. Ten accounts is an example of the workflow we want to simplify, not a tested capacity guarantee.

Work, personal and client accounts should be available together without repeatedly disconnecting one account to connect another. MailMeUp gives your assistant one place to search the accounts you've chosen. Ask for the latest message about a project, unread mail across your accounts, or next week's meetings. Start with a short list, then open the details you need.

The preview already supports multiple accounts. Easier onboarding remains work in progress, and setup currently focuses on Codex; see [before you start](#before-you-start) and [AI assistant support](#ai-assistant-support).

<details>
<summary>How this compares with built-in connectors (research: September 19, 2026)</summary>

The official documentation reviewed did not establish a built-in setup for connecting ten independent Google/Microsoft accounts in Codex or Claude. That is a documentation finding, not proof that every plugin is limited to one mailbox:

- Claude's [Google Workspace guide](https://support.claude.com/en/articles/10166901-use-google-workspace-connectors) describes access to the connected Google account, without documenting several simultaneous Gmail account connections.
- Claude's [Microsoft 365 guide](https://support.claude.com/en/articles/12542951-set-up-the-microsoft-365-connector) explicitly supports delegated access to shared mailboxes. Those mailboxes are accessed through an authorized work account; this is distinct from signing in to several independent accounts across providers.
- OpenAI documents [plugins](https://learn.chatgpt.com/docs/plugins) and [custom MCP connections](https://learn.chatgpt.com/docs/extend/mcp?surface=cli) for Codex. Those extension points allow other integrations; these guides do not establish a universal one-mailbox limit for Gmail or Outlook plugins.

MailMeUp's focus is a simple, local, read-only connection for the independent accounts you choose to share with your assistant.

</details>

## Small and native

The MailMeUp command-line processes in the Task Manager snapshot below use **7.4–9.6 MB of RAM each** while waiting for requests. That's one snapshot, so memory use will vary with the work you're doing.

We like small, native apps. The Windows setup window doesn't need an embedded browser to draw its screens. The shared .NET code is portable; the setup app is built for Windows. Other platforms still need testing.

![Windows Task Manager showing the highlighted MailMeUp CLI process group using roughly 7 MB RAM per process; unrelated processes are blurred for privacy](docs/assets/branding/mailmeup-task-manager-lightweight.png)

*MailMeUp is highlighted. Other processes are blurred for privacy.*

## What you can try

- Search across Gmail, Google Workspace, Outlook.com and Microsoft 365 inboxes.
- List unread messages or messages in a received-time range, with optional sender, recipient and attachment filters.
- Show appointments from Google Calendar and Microsoft calendars.
- Choose which accounts and calendars to include.
- Return short results first, then open the details you need.

Each person connects their own accounts. You don't need a hosted MailMeUp service or a public marketplace.

## Before you start

**Windows x64 is the current test platform.** The preview includes browser sign-in, multiple accounts, mail search and a combined calendar agenda. Searches skip Spam/Junk and Trash/Deleted Items by default.

> [!IMPORTANT]
> **There's still some setup to do.** For now, you need to register your own app with Google or Microsoft before connecting an account. Google gives you a configuration file; Microsoft gives you an Application (client) ID. The [registration guide](docs/APP_REGISTRATION.md) walks you through it. Making this easier is part of the work ahead.

> [!NOTE]
> **MSIX installers are temporarily unavailable.** We're working on the code-signing certificate needed to distribute them. Until then, use the Windows portable ZIP.

The Windows app helps you connect accounts, choose what to share and set up Codex. Its **Check read access** button tries sample searches and reads for mail and calendars separately. It helps spot connection problems; it doesn't guarantee every future search will work.

Prefer no installer? The [complete Windows portable ZIP](docs/WINDOWS_PORTABLE.md) includes the setup app and CLI/MCP tools. Extract the whole ZIP and open `MailMeUp.Desktop.exe`. Keep that folder in place after connecting Codex.

Your sign-in tokens stay on your computer, protected by the operating system. Earlier builds have been tried with real Google and Microsoft accounts. Newer changes still need checks; the [validation record](docs/VALIDATION.md) lists what was tested in each version.

![MailMeUp cross-account mail search in an AI conversation, with example addresses redacted](docs/assets/branding/mailmeup-chat-search.png)

*An example search, with email addresses hidden.*

## AI assistant support

Codex is the main focus today. You can use the [Windows setup app](docs/WINDOWS_SETUP.md) to prepare its local plugin, or follow the [manual setup guide](docs/CODEX_SETUP.md).

MailMeUp uses MCP, a standard way for an assistant to use tools, through a local process. Other clients, including Claude, may work if they support this kind of connection, but haven't been tested. ChatGPT Desktop isn't supported as a direct local connection yet.

If you'd like to help try another client and document the setup, see [how to contribute](CONTRIBUTING.md).

## Where it runs

- **Windows x64:** the preview has been built, signed and installed locally. Some command and UI checks passed on earlier versions; a clean-machine installation and the remaining setup and update checks are still needed.
- **Windows ARM64:** the package builds, but hasn't been run on ARM64 hardware.
- **macOS and Linux:** not tested or supported in this preview, including sign-in and secure token storage.

## How it fits together

![Planned email workflow: accounts connect to MailMeUp on your device, then to Codex](docs/assets/branding/mailmeup-concept.png)

*Concept artwork showing how your accounts connect to your assistant through MailMeUp. Calendars work through the same connection. Requested information may reach your assistant's AI service.*

## Explore the project

- [Short product overview](docs/PRODUCT.md)
- [Roadmap](docs/ROADMAP.md)
- [Plan for the first usable version](docs/MVP_PLAN.md)
- [Getting started](docs/GETTING_STARTED.md)
- [Calendars and appointments](docs/CALENDARS.md)
- [Accounts and credentials](docs/AUTHENTICATION.md)
- [Register the app with Google and Microsoft](docs/APP_REGISTRATION.md)
- [Privacy](docs/PRIVACY.md)
- [Connect to Codex](docs/CODEX_SETUP.md)
- [Build and contribute](docs/DEVELOPMENT.md)

For developers: [how the code fits together](docs/ARCHITECTURE.md), [MCP tools](docs/MCP_CONTRACT.md), [release process](docs/RELEASING.md) and [test results](docs/VALIDATION.md).

Created by [Umberto Giacobbi](https://github.com/umbertotechnopreneur). [MIT license](LICENSE). [Contributions](CONTRIBUTING.md) and [security reports](SECURITY.md) are welcome. Independent project; no endorsement by OpenAI, Google or Microsoft.

Artwork, source prompts and writing style: [brand guide](docs/BRAND.md).
