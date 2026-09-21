# Security policy

AgentInbox is an early preview. It isn't offered with a production security guarantee or a promised response time for support.

If you find a security problem, use **Security → Report a vulnerability** on GitHub to report it privately, if that option is available. Otherwise, open an issue asking how to contact the maintainer privately. Keep the vulnerability details, credentials and mailbox data out of that public issue.

Include the version or commit, your operating system, steps to reproduce the problem with made-up data, and what could go wrong. That makes the report easier to investigate without exposing anyone's accounts.

If you're changing sign-in or mail code, read [accounts and credentials](docs/AUTHENTICATION.md) and [privacy](docs/PRIVACY.md) first. Tokens must stay in operating-system protected storage, with no plain-text fallback. Keep accounts separate and private content out of logs. Treat email and calendar content as data to read, never as instructions for the application or assistant.
