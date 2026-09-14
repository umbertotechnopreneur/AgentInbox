# Accounts and sign-in

You sign in to each Google or Microsoft account in your browser. MailMeUp doesn't need your account password. It saves sign-in tokens so it can read the accounts you've connected without asking you to sign in every time.

Setup and sign-in with several real accounts have passed Windows checks. Tests with made-up accounts also cover failed reconnections and multiple processes using the same credentials. Revoking access and reconnecting real accounts still need deliberate testing.

## What's saved on your computer

| Item | What it's for | Where it's kept |
| --- | --- | --- |
| Client ID | Tells Google or Microsoft which app is asking | Local app settings; this ID isn't secret |
| Access token | Gives the app temporary access | Memory or the sign-in library's protected cache |
| Refresh token | Renews access without another browser sign-in | Operating-system protected storage |
| Microsoft token cache | Saves sign-in information through Microsoft's MSAL library | Operating-system protected storage |
| Account names and addresses | Shows which accounts you've connected | Local SQLite database |

Keep tokens out of prompts, logs, GitHub and the account database. Microsoft desktop sign-in doesn't need a client secret. A secret bundled inside a desktop app can be extracted, so it can't be treated as private.

## Set up the app, then connect accounts

- `mailmeup setup google <client-json>` imports your Google **Desktop app** configuration file. It saves the client ID in local settings and protects the client secret with the operating system. The original downloaded file stays where it is; remove it yourself when you no longer need it.
- `mailmeup setup microsoft <client-id>` saves the public Application (client) ID. A Microsoft desktop app does not need a client secret.
- `mailmeup setup status` shows whether Google and Microsoft are set up, without revealing secrets.

Once the app is set up, connect an account:

- `mailmeup accounts connect google`
- `mailmeup accounts connect microsoft`
- add `--mail-only` or `--calendar-only` if you only want email or calendars

Run the command again to add another account. Each time, your browser opens and lets you choose an account. Google stores tokens separately for each account. Microsoft's sign-in library uses one protected cache while keeping account identities separate.

To reconnect, choose the same account again. If the new sign-in fails its checks, MailMeUp keeps the saved credentials. It also coordinates token refresh and removal when several local processes are running. See [account recovery](RECOVERY.md).

`mailmeup accounts remove <account-id>` removes the local account record and saved sign-in tokens. To revoke access too, use your Google or Microsoft account settings.

Before replacing a Google or Microsoft app registration, remove its connected accounts from MailMeUp. That lets MailMeUp clean up their old sign-in tokens first.

## Read-only access

Ask for only the permissions you need. Reading calendars needs its own permission; email access doesn't cover it. Declining calendar access must leave an existing mail connection working.

## Secure storage and sharing the app

Token storage uses Windows user protection, macOS Keychain or Linux Secret Service. If secure storage isn't available, MailMeUp must stop instead of saving tokens as plain text. The macOS and Linux paths still need testing; those platforms aren't supported in this preview.

Distributing the app publicly still requires the right Google and Microsoft registrations and any review they require. A copy of the program doesn't include the creator's account tokens.

Developer references: [Google OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [Calendar permissions](https://developers.google.com/workspace/calendar/api/auth), [MSAL cache](https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/token-cache-serialization).
