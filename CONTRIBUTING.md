# Contributing

Want to help? Small fixes, clearer docs and reports of what worked or went wrong are useful. Start with the [roadmap](docs/ROADMAP.md). If you're thinking about adding another email service or changing how account security works, open an issue first so we can talk it through.

1. Fork the repository and create a branch for your change.
2. Use .NET 10 from `global.json`, Python 3.10+ and PowerShell 7 for the validation scripts.
3. Write code, comments and docs in English. Use made-up accounts and mail, never real mailbox data.
4. Run `pwsh -NoProfile -File scripts/install-git-hooks.ps1` once per clone. The pre-commit hook formats staged C# files automatically.
5. Run `pwsh -NoProfile -File scripts/validate.ps1`; local validation also applies style fixes before building.
6. Open a pull request explaining what changed, how you checked it and anything that still needs work.

Keep credentials, messages, saved sign-in tokens, other users' local paths and private app settings out of issues and pull requests. If you change sign-in code, add tests for separate accounts, simultaneous token refreshes, revoked access and unavailable secure storage.

Contributions use the repository's MIT license. If you add generated artwork, include the prompt and how it was made. Keep it clear that the project isn't endorsed by the companies whose services it connects to.
