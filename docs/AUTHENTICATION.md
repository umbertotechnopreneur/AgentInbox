# Accounts and passwords

An account has an incoming IMAP connection and optional outgoing SMTP connection. Authentication uses the password or app password accepted by that server.

## Connection settings

Enter a hostname, port, encrypted connection mode and username for each connection. Supply passwords through local setup or management JSON on stdin. Keep them out of command-line arguments, issue reports and logs.

Use TLS from the start or require STARTTLS before authentication. Certificate and hostname validation must succeed. There is no unencrypted fallback.

Gmail needs an eligible account with an app password. iCloud Mail needs an app-specific password. Custom providers must allow password-based IMAP and optional SMTP. Outlook.com and Microsoft 365 presets are rejected because this implementation cannot satisfy their required sign-in method.

## Protected storage

Configuration holds non-secret connection settings and permissions. Passwords use system protection:

| Platform | Required protection |
| --- | --- |
| Windows | Current-user DPAPI |
| macOS | Keychain through native APIs |
| Linux | An unlocked Secret Service through the native `libsecret` library |

An unavailable or locked store is an error. There is no plain-text fallback or previous-profile import.

This protection does not encrypt every metadata file or protect a compromised signed-in operating-system account. See [privacy](PRIVACY.md).

## Account permissions

Enable assistant reads separately from saving a connection. SMTP remains disabled until you enable it, and every message still needs local approval.

Removing an account removes local access and its saved credentials, not the mailbox. Revoke a dedicated app password in your provider settings when no longer needed.

Each platform's credential path remains unvalidated.
