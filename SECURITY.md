# Security policy

AgentInbox is in development. I do not offer a production security guarantee or promised support response time.

Report security problems privately through GitHub's **Security → Report a vulnerability**, if available. Otherwise, open an issue asking how to contact me privately. Keep vulnerability details, credentials and mailbox data out of public issues.

Include the version or commit, operating system and steps with made-up data.

Keep passwords and Microsoft refresh tokens in protected system storage with no plain-text fallback. Windows uses Credential Manager for those secrets and DPAPI for temporary sign-in blobs. Native macOS Keychain and Linux Secret Service paths remain untested.

Require implicit TLS or required STARTTLS with certificate and hostname validation before authentication. There is no plaintext mode or custom-CA control. Microsoft OAuth2 must be chosen explicitly; failed passwords must never trigger an automatic switch. Keep device codes, tokens and authentication exchanges out of diagnostics. Treat mail content as untrusted data.

Sending starts disabled and can be enabled only in local configuration. `send_email` submits immediately, with no second AgentInbox confirmation. The connected client is responsible for obtaining the user's instruction; the engine does not infer human intent from a tool call. Treat mail content as untrusted. Bypassing account permissions, reusing a request ID or automatically retrying an unresolved submission is a security-sensitive defect.

Read [accounts](docs/AUTHENTICATION.md), [architecture](docs/ARCHITECTURE.md) and [privacy](docs/PRIVACY.md) before changing these paths.
