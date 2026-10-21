# Accounts and signing in

Choose your email provider in Windows setup. Outlook.com and Microsoft 365 select **Sign in with Microsoft** and the matching personal or work/school account type. Gmail and iCloud select **Use a password** and explain how to create a separate password for this app. Reading uses IMAP or POP3; optional sending uses SMTP.

Earlier synthetic sign-in tests passed with mocked transports and protected stores. The latest provider-persistence and Microsoft-diagnostic cases have not run. Live sign-in and native credential storage remain unvalidated. See [validation](VALIDATION.md).

## Passwords and app passwords

Enter the host, port, TLS mode and username for each connection. Use TLS from the start or require STARTTLS before authentication. Certificate validation must succeed; there is no unencrypted fallback.

Supply passwords through local setup, never in an assistant conversation, command-line argument, issue report or log. Gmail requires an eligible app password for AgentInbox, with 2-Step Verification enabled. iCloud Mail requires an app-specific password and two-factor authentication. Use each preset's official link to create one; a normal account password will not work in these modes. Provider and organization policies still apply. [Google app passwords](https://support.google.com/accounts/answer/185833), [Apple app-specific passwords](https://support.apple.com/en-us/102654).

AgentInbox uses an app password for Google accounts, rather than a browser sign-in button. A rejected password does not change the chosen sign-in method.

For Outlook.com and Microsoft 365, choose **Sign in with Microsoft**. Your usual email password cannot be used for reading here; an app password does not bypass Microsoft's restrictions. [Outlook.com sign-in requirements](https://support.microsoft.com/en-us/support/known-issues/modern-authentication-methods-now-needed-to-continue-syncing-outlook-email-in-non-microsoft-email-ap), [Exchange Online password sign-in removal](https://learn.microsoft.com/en-us/exchange/clients-and-mobile-in-exchange-online/deprecation-of-basic-authentication-exchange-online).

## Sign in with Microsoft

The Outlook.com and Microsoft 365 choices select **Sign in with Microsoft**. AgentInbox shows a short code; enter it on Microsoft's page and choose whether to allow mail access. Reading and sending use the same sign-in, with no separate password fields. The underlying OAuth connection uses standard IMAP, POP or SMTP, not Microsoft Graph. [Microsoft protocol documentation](https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth/).

Windows setup ships the owner's existing public Microsoft client ID in `src/MailMeUp.Desktop/appsettings.json`, copied beside the application when built and published. It is not a password or client secret and is not shown in the account form. Missing or invalid application settings fail at startup. Saved OAuth accounts retain the client ID associated with their tokens; selecting a Microsoft preset uses the shipped registration.

The recovered registration has not been verified against Microsoft's service for the current device-code flow or mail protocol scopes. Recovery of an ID does not prove it supports these requirements. For maintainers configuring this registration:

1. Choose supported account types that include the account you intend to connect.
2. Enable **Allow public client flows** for device-code authentication.
3. Put the Application (client) ID in `MicrosoftClientId` in the desktop application settings. Account configuration still supports `common`, `organizations`, `consumers` or an explicit tenant GUID; source-only local management callers supply the ID in their authentication settings.
4. Use the delegated mail permissions for the selected incoming protocol, plus SMTP only when sending is enabled. No application client secret is used.

Registration creates the application identity; bundling its ID does not create or validate the registration. [Register an application](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app), [public-client configuration](https://learn.microsoft.com/en-us/entra/identity-platform/msal-client-applications).

If Microsoft says `AADSTS70002` and specifically says the client must be marked as `mobile`, update the existing registration under **Authentication → Advanced settings → Allow public client flows → Yes**, then save. A new client ID is not required to fix that setting. Microsoft uses this classification for desktop apps too. Other `AADSTS70002` failures can have different causes; keep the accompanying explanation. [Microsoft desktop setup](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-desktop-app-configuration#enable-public-client-flow).

Press **Sign in with Microsoft**, then **Open Microsoft**. The sign-in button stays available before saving. It saves complete account settings first; missing or malformed fields get a red border and a nearby explanation. Enter the displayed code on Microsoft's page and review what the app asks to access. AgentInbox waits for your decision and stops if you cancel, the code expires or Microsoft refuses. [How the Microsoft sign-in code works](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code).

**Sign-in details** shows elapsed time, the public app ID, account type, requested permissions, request endpoint, HTTP status, waiting and cleanup steps. The same trace appears in the sign-in window and remains on the account page after an error; **Copy sign-in details** copies it. The trace is bounded and stays in memory. It excludes passwords, sign-in codes, tokens, email addresses and response bodies. The browser uses the HTTPS address returned by Microsoft's verified endpoint, without a hard-coded domain or path list. Only its origin appears in the trace; paths, queries and fragments are hidden because they may contain codes. A page-refresh problem after successful sign-in does not undo the saved sign-in.

If Microsoft refuses, the Windows app shows a simple explanation followed by **Server reply** when available. That reply is bounded and hides known sign-in secrets and email addresses, including secrets echoed from a request. It stays out of ordinary MCP errors. The previous generic registration message did not establish the actual cause; the recovered registration still needs a real sign-in check when authorized.

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

Temporary device-code state is protected and bound to the account revision and expiry. On Windows, temporary sign-in state uses DPAPI-protected files separately from the credential entries. An expired device flow is rejected and its state removed when that flow is accessed; cancellation removes its local state. There is no background expiry timer.

Unavailable secure storage is an error, with no plain-text fallback. Protection does not encrypt every metadata file or protect a compromised signed-in operating-system account. See [privacy](PRIVACY.md).

## Account permissions and removal

Assistant read access is separate from saving or authenticating a connection. SMTP starts disabled. Microsoft consent to an SMTP scope does not enable sending by itself. Enable SMTP separately in Windows configuration; Codex can then submit the emails you request without another AgentInbox confirmation.

Removing an account removes local access and stored account credentials, not the mailbox. Revoke a dedicated app password or Microsoft application consent with the provider when no longer needed. Previous profiles are not imported or converted.
