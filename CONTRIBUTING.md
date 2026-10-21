# Contributing

Help improve AgentInbox with a fix, clearer instructions or a report of what worked or failed. For changes to account security or sending, open an issue so I can review the approach with you.

Use Python 3.11+ for the engine. Windows setup also needs the SDK in `global.json` and WinUI 3. See [development](docs/DEVELOPMENT.md).

Write code, comments and docs in English. Use synthetic `example.test` accounts. Keep credentials, private mail and machine-specific settings out of issues and pull requests.

Keep mail behavior in Python. Reject unsupported settings, use protected credential storage and require enabled SMTP, retain send outcomes and prevent automatic retries. Do not add compatibility shims or previous-data import.

macOS Keychain and Linux Secret Service paths are implemented through native interfaces but have not been tested. Pull requests that improve these paths or document reproducible platform validation are welcome. Keep IMAP/POP3/SMTP behavior, password defaults and explicit Microsoft sign-in boundaries consistent across platforms.

Follow the [Defender scan requirement](README.md#external-binary-security-policy) before adding dependencies. Follow authorization and cleanup rules in [AGENTS.md](AGENTS.md) for checks.

Describe changes, evidence and untested paths in your pull request. Contributions use MIT. Include generated-artwork provenance and preserve third-party attribution.
