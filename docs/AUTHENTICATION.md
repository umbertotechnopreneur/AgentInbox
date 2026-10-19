# Accounts and authentication

Choose IMAP or POP3 for incoming mail and optional SMTP for outgoing mail. Authentication is a separate choice: password or app password by default, or explicitly selected Microsoft sign-in.

Synthetic authentication tests pass with mocked transports and protected stores. Live sign-in and native credential storage remain unvalidated. Changing an authentication setting does not prove the account can connect.

## Passwords and app passwords

Enter the host, port, TLS mode and username for each connection. Use TLS from the start or require STARTTLS before authentication. Certificate validation must succeed; there is no unencrypted fallback.

Supply passwords through local setup, never in an assistant conversation, command-line argument, issue report or log. Gmail can require an eligible app password; iCloud Mail requires an app-specific password. Provider and organization policies still apply.

Google browser sign-in is not implemented. AgentInbox does not try a different authentication method after a rejected password.

Outlook.com requires modern authentication. Exchange Online has removed Basic authentication for IMAP and POP. Selecting password mode or supplying an app password cannot bypass those restrictions. Use the explicit Microsoft sign-in option for these services. [Outlook.com authentication requirements](https://support.microsoft.com/en-us/support/known-issues/modern-authentication-methods-now-needed-to-continue-syncing-outlook-email-in-non-microsoft-email-ap), [Exchange Online Basic authentication removal](https://learn.microsoft.com/en-us/exchange/clients-and-mobile-in-exchange-online/deprecation-of-basic-authentication-exchange-online).

## Optional Microsoft sign-in

For Outlook.com or Microsoft 365, choose **Microsoft sign-in** explicitly. Microsoft documents delegated authorization for IMAP, POP and SMTP for both account types. This authenticates the standard mail connection; AgentInbox does not use Microsoft Graph. [Microsoft protocol documentation](https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth/).

The connection requires a Microsoft Entra app registration configured as a public client. In that registration:

1. Choose supported account types that include the account you intend to connect.
2. Enable **Allow public client flows** for device-code authentication.
3. Record the Application (client) ID. Enter it in AgentInbox with the tenant: `common`, `organizations`, `consumers` or an explicit tenant GUID.
4. Use the delegated mail permissions for the selected incoming protocol, plus SMTP only when sending is enabled. No application client secret is used.

Registration creates the application identity; AgentInbox does not create or verify it for you. [Register an application](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app), [public-client configuration](https://learn.microsoft.com/en-us/entra/identity-platform/msal-client-applications).

After saving the account, start sign-in locally. AgentInbox displays a short code and a Microsoft verification address. You choose to open that address, sign in and review the requested permissions. The engine polls at Microsoft's required interval and stops on cancellation, expiry or rejection. It does not open a browser automatically. [Device authorization flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code).

The requested scopes are:

| Account choice | Delegated scope |
| --- | --- |
| IMAP | `https://outlook.office.com/IMAP.AccessAsUser.All` |
| POP3 | `https://outlook.office.com/POP.AccessAsUser.All` |
| SMTP enabled | `https://outlook.office.com/SMTP.Send` |
| Refresh after an access token expires | `offline_access` |

Only the selected incoming protocol is requested. Changing the connection, identity or authentication settings, or enabling SMTP, can require signing in again. Sign-in completion is separate from a successful mail connection. SMTP AUTH can still be disabled by tenant or mailbox policy. [Microsoft SMTP AUTH settings](https://learn.microsoft.com/en-us/exchange/clients-and-mobile-in-exchange-online/authenticated-client-smtp-submission).

### Registration and publisher verification are different

Publisher verification identifies the organization behind a Microsoft application. It is separate from app registration, delegated consent and mailbox access. A verified-publisher badge does not certify app quality or prove AgentInbox works.

Organization policies can block consent to an unverified multitenant application or require administrator consent. A client ID alone does not bypass those rules. AgentInbox makes no claim that a supplied registration or this project is publisher-verified. [Microsoft publisher verification](https://learn.microsoft.com/en-us/entra/identity-platform/publisher-verification-overview).

## Protected local storage

Account JSON stores non-secret settings and permission choices, not password or token values.

| Platform | Password and refresh-token storage |
| --- | --- |
| Windows | Windows Credential Manager, through native WinCred APIs |
| macOS | Keychain through native APIs |
| Linux | Native `libsecret` APIs with an unlocked Secret Service keyring |

The macOS and Linux implementations are source-only and untested. I welcome contributor pull requests that improve them and document the platform, credential path and checks actually performed.

Windows credentials are limited to 2,560 UTF-8 bytes; oversized values fail instead of switching to a file. Microsoft access tokens stay only in process memory. Refresh tokens remain in protected storage and are replaced after successful refresh without changing account settings.

Temporary device-code state is protected and bound to the account revision and expiry. On Windows, larger temporary sign-in state and pending message content use DPAPI-protected files separately from the credential entries. An expired device flow is rejected and its state removed when that flow is accessed; cancellation removes its local state. There is no background expiry timer.

Unavailable secure storage is an error, with no plain-text fallback. Protection does not encrypt every metadata file or protect a compromised signed-in operating-system account. See [privacy](PRIVACY.md).

## Account permissions and removal

Assistant read access is separate from saving or authenticating a connection. SMTP starts disabled. Microsoft consent to an SMTP scope does not authorize sending: every message still requires direct local approval of its exact content.

Removing an account removes local access and stored account credentials, not the mailbox. Revoke a dedicated app password or Microsoft application consent with the provider when no longer needed. Previous profiles are not imported or converted.
