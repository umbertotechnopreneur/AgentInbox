# Contributing

Help improve AgentInbox with a bug fix, clearer instructions, or a report of what worked or failed. For a new email service or a change to account security, open an issue first so I can review the approach with you.

1. Fork the repository and create a branch for your change.
2. Use .NET 10 from `global.json`, Python 3.10+ and PowerShell 7 for the validation scripts.
3. Write code, comments and docs in English. Use made-up accounts and mail, never real mailbox data.
4. Run `pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command install-hooks` once per clone. The pre-commit hook formats staged C# files automatically.
5. Run `pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command validate`; local validation also applies style fixes before building.
6. Open a pull request explaining what changed, how you checked it and anything that still needs work.

Keep credentials, messages, saved sign-in tokens, other users' local paths and private app settings out of issues and pull requests. If you change sign-in code, add tests for separate accounts, simultaneous token refreshes, revoked access and unavailable secure storage.

Contributions use the repository's MIT license. If you add generated artwork, include the prompt and how it was made. Keep it clear that the project isn't endorsed by the companies whose services it connects to.
