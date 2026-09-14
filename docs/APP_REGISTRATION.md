# Set up Google or Microsoft

> [!IMPORTANT]
> **This is the fiddly part for now.** Before connecting an account, you need to register your own desktop app with Google or Microsoft. A simpler setup is still on the to-do list. You only need to follow the section for the service you use.

> [!WARNING]
> **Use Windows x64 for this preview.** That's where Google and Microsoft sign-in has been tried. Sign-in and secure token storage on macOS and Linux still need testing.

## Why this is needed

Google and Microsoft need your permission before MailMeUp can read an account. You'll sign in on their website, see which permissions MailMeUp asks for, and decide whether to allow them. This sign-in process is called OAuth.

A **Client ID** tells Google or Microsoft which app is asking. It isn't your account password. Google gives you a downloadable JSON configuration file; keep it private. Microsoft gives you an **Application (client) ID** and doesn't need a client secret for this desktop setup.

Set up Google, Microsoft or both. You'll sign in to each account separately. MailMeUp saves the resulting sign-in tokens using your operating system's protected storage.

## Before you begin

Open PowerShell and point it to your copy of MailMeUp. Replace this example path with your own:

```powershell
$MailMeUp = 'C:\Tools\MailMeUp\mailmeup.exe'
& $MailMeUp --help
```

The commands below use `$MailMeUp`, so you can run them from any folder. Keep JSON contents, tokens and client secrets out of chats, GitHub and terminal output you share.

## Google: register the app and connect an account

1. Open [Google Cloud Console](https://console.cloud.google.com/) and select or create a project for MailMeUp.
2. In **APIs & Services > Library**, enable only **Gmail API** and **Google Calendar API**.
3. Open **Google Auth Platform**:
   - set the app name and contact email;
   - choose **External** if personal or other Google accounts will sign in;
   - keep the app in **Testing** while trying MailMeUp;
   - add the exact Google accounts that may test the app.
4. Under **Data Access**, add these permissions (Google calls them scopes):

   ```text
   openid
   email
   profile
   https://www.googleapis.com/auth/gmail.readonly
   https://www.googleapis.com/auth/calendar.calendarlist.readonly
   https://www.googleapis.com/auth/calendar.events.readonly
   ```

5. Under **Clients**, create a **Desktop app** client named **MailMeUp Desktop**. Download its JSON file and keep it in a private local folder.
6. Import the file into MailMeUp, then start sign-in:

   ```powershell
   & $MailMeUp setup google 'C:\Private\client_secret.json'
   & $MailMeUp setup status
   & $MailMeUp accounts connect google
   ```

7. Sign in with one of the configured Google test users and approve the read-only permissions. Run the last command again for every additional Google account.

Keep the Google app in Testing for this preview. You don't need to publish it, create API keys or use service accounts. Leave out permissions to send email or change mail and calendars.

## Microsoft: register the app and connect an account

1. Open [Microsoft Entra](https://entra.microsoft.com/) with a directory where you can register applications. Go to **Entra ID > App registrations > New registration**.
2. Name the app **MailMeUp Desktop**.
3. To use both Outlook.com and Microsoft 365, choose **Accounts in any organizational directory and personal Microsoft accounts**. Choose a more limited option if you only want certain accounts to sign in.
4. In **Authentication**, add the **Mobile and desktop applications** platform with this redirect URI:

   ```text
   http://localhost
   ```

5. In **API permissions > Microsoft Graph > Delegated permissions**, retain `User.Read` and add only:

   ```text
   Mail.Read
   Calendars.Read
   ```

6. Do not create a certificate or client secret. Do not add application permissions, `Mail.ReadWrite`, `Mail.Send`, or `Calendars.ReadWrite`. Do not grant tenant-wide admin consent unless your organization explicitly requires and approves it.
7. On **Overview**, copy the **Application (client) ID**. Configure MailMeUp and start browser sign-in:

   ```powershell
   & $MailMeUp setup microsoft '<application-client-id>'
   & $MailMeUp setup status
   & $MailMeUp accounts connect microsoft
   ```

8. Choose your Microsoft account in the browser and approve the read-only permissions. Run the last command again for each additional Microsoft account.

Your workplace or school may limit which apps you can connect. If sign-in is blocked for that reason, ask your administrator for help.

## Check connected accounts

```powershell
& $MailMeUp accounts list
```

Add `--mail-only` or `--calendar-only` to `accounts connect` if you only want to share email or calendars. Use `accounts remove <account-id>` to remove an account and its saved sign-in tokens from this device. To revoke the app's access too, use your Google or Microsoft account settings.

MailMeUp only reads. It can't send email, change or delete messages, create or edit appointments, or send invitations.
