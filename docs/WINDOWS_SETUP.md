# Windows configuration

The WinUI 3 app configures AgentInbox on Windows 11. Email operations run through Codex and the Python engine. See [validation](VALIDATION.md) for the source, package and provider checks actually performed.

## Connections

Choose **Add connection**, or select a saved connection by its name and email address. The list sits beside the editor on wide windows and above it on narrower windows. No account form appears until you create or select one.

When no mailbox is selected, a centered mail icon, heading and short guide explain how to select one or add another with the **+** button. An empty account list uses the same layout with a button to create your first account.

**Connection name** stays above the native **Account**, **Read mail** and **Send mail** tabs. Edit it and press **Save changes** to rename the connection. Tab icons are colored; the selected label is bold. Save and Cancel stay below the form with the current save status. The window uses Acrylic and adapts to display scaling and available space.

In **Account**, choose your **Email provider** and enter your email address. Instructions and an official link appear directly below it:

- **Gmail:** create an app password with 2-Step Verification enabled; your normal password does not work here. Account or administrator restrictions may prevent app passwords.
- **iCloud:** create an app-specific password with two-factor authentication enabled. Incoming mail uses IMAP; POP3 is unavailable.
- **Outlook.com:** **Sign in with Microsoft** and **Personal** are selected. Outlook's settings may also need to allow mail apps; use the help link.
- **Microsoft 365:** **Sign in with Microsoft** and **Work or school** are selected. Your IT team may need to allow mail apps or sending.

Your provider choice is saved with the account. Earlier saves did not record it: select the provider once and save changes. AgentInbox does not guess that choice from your email address or rewrite old settings when reading them.

For Microsoft, press **Sign in with Microsoft**, then **Open Microsoft**. The button stays available before saving and saves your complete account settings first. Enter the displayed code on Microsoft's page. **Sign-in details** keeps a local trace of the steps and errors, with private sign-in values hidden. You do not need to supply an app ID. Reading and sending use the same sign-in, and their password fields are hidden. When sending was not configured, reopening the account fills its provider's outgoing defaults without enabling sending. See [Microsoft setup](AUTHENTICATION.md) for technical requirements and validation limits.

When you save or check an account, missing or malformed fields get a red border and a short explanation underneath. The app selects the tab and focuses the first field to fix. These errors update as you edit. Controls keep their native WinUI templates and use the theme's critical-color brush; no validation library or custom control style is added. WinUI 3 has no complete built-in form-validation framework; this follows [Microsoft's form guidance](https://learn.microsoft.com/en-us/windows/apps/get-started/line-of-business/build-validated-form).

In **Read mail**, choose IMAP or POP3 and enter the server settings from your provider. IMAP shares your selected folders; POP3 has only INBOX. Enable reading only for accounts you want to share.

In **Send mail**, turn on **Set up sending** and separately choose whether Codex may send from this account. Sending starts disabled. Enabling it permits immediate sends requested through Codex; this configuration window does not compose or send messages.

## Connection checks

**Check connection and get folders** uses the current form, including unsaved passwords and settings. It opens an owned Acrylic window showing settings, secure connection, sign-in and folder discovery, plus SMTP when configured. Cancel stops the check. Nothing is saved or sent by checking.

Choose between one and twenty exact server folder names. Current support requires ASCII names. Existing choices are kept when available; new folders are not silently selected. **Use selected folders** updates the form. Creating a password-based account runs this same check before saving the explicit folder selection.

**Also test reading one message** performs one bounded read from the first chosen folder, without marking IMAP mail read or issuing POP3 delete commands. An empty folder is reported separately from a successful read. A saved account also offers a read test when there are no unsaved changes.

**Check sending connection** uses the same progress and result window, with settings, secure connection and sign-in stages. It needs neither a reading password nor enabled sending and never sends email. Errors show the actual server reply when available, with sign-in details and addresses hidden. Microsoft sign-in errors also include its bounded reply locally. Text is selectable and scrollable. A failure does not change your chosen sign-in method.

## Connect Codex

Open **Connect Codex**, headed **Connect Your Codex Assistant**, and choose **Connect to Codex**. Existing matching settings show **Settings saved in Codex**. If a different entry exists, **Update connection** asks before replacing it. The page explains the direct local MCP connection and shows the Codex settings file.

Press **Test connection** to check the saved configuration, start AgentInbox and request its mail-tool list. This uses the local connection without calling a mail tool or opening a mailbox. A successful result is separate from saved settings and from access inside a Codex chat. Start a new chat and try the provided account-list request afterward.

**Connection details** shows the app command, startup options, account-settings folder and a full multiline JSON preview. The JSON is for manual setup in another MCP app; Codex saves its own TOML entry in the file shown above. See [Codex setup](CODEX_SETUP.md).

The footer links to umbertogiacobbi.biz and the AgentInbox Terms and Privacy pages. On the right, the approved rendered VibeWare logo links to the manifesto. Its tooltip explains AI assistance under Umberto's direction and responsibility. See the [brand guide](BRAND.md#vibeware-participation).

## Local data and installation

The app and engine share the same profile. Passwords and Microsoft refresh tokens use Windows Credential Manager; temporary sign-in state uses current-user DPAPI. Account settings use schema 3; send metadata uses schema 4. Unsupported older formats fail without migration or deletion.

A save or sign-in can succeed while old credential cleanup remains pending. The separate warning offers **Retry cleanup**, which retries deletion without repeating the completed action. Removing an account does not remove server messages. See [privacy](PRIVACY.md) and [recovery](RECOVERY.md).

Authorized local installs use the source-built Debug MSIX workflow. Installation may force-close the running AgentInbox app without another prompt and preserves saved data. Store builds are separate, explicitly requested work. See [packaging](../packaging/windows/README.md).
