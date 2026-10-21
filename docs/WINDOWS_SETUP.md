# Windows configuration

The WinUI 3 app configures AgentInbox on Windows 11. Email operations run through Codex and the Python engine. See [validation](VALIDATION.md) for the source, package and provider checks actually performed.

## Connections

Choose **Add connection**, or select a saved connection by its name and email address. The list sits beside the editor on wide windows and above it on narrower windows. No account form appears until you create or select one.

When no mailbox is selected, a centered mail icon, heading and short guide explain how to select one or add another with the **+** button. An empty account list uses the same layout with a button to create your first account.

**Connection name** stays above the native **Account**, **Incoming** and **Outgoing** tabs. Edit it and press **Save changes** to rename the connection. Tab icons are colored; the selected label is bold. Save and Cancel stay below the form with the current save status. The window uses Acrylic and adapts to display scaling and available space.

In **Account**, choose a preset or custom servers and enter your email address. Instructions and an official link appear directly below the preset:

- **Gmail:** create an app password with 2-Step Verification enabled; your normal password does not work here. Account or administrator restrictions may prevent app passwords.
- **iCloud:** create an app-specific password with two-factor authentication enabled. Incoming mail uses IMAP; POP3 is unavailable.
- **Outlook.com:** OAuth and personal accounts are selected. Enable IMAP or POP access in Outlook.com settings.
- **Microsoft 365:** OAuth and work/school accounts are selected. Your organization may require administrator consent and must permit mail protocol access; sending also requires SMTP AUTH.

Windows setup loads the Microsoft app ID from its application settings; there is no app ID field to fill in. Save the account before starting web authorization. The browser opens only when you press its button. See [authentication](AUTHENTICATION.md) for registration requirements and validation limits.

In **Incoming**, choose IMAP or POP3 and enter the server, port, TLS mode and credentials. IMAP shares your selected folders; POP3 has only INBOX. Enable reading only for accounts you want to share.

In **Outgoing**, configure SMTP and separately choose whether Codex may send from this account. Sending starts disabled. Enabling it permits immediate sends requested through Codex; this configuration window does not compose or send messages.

## Connection checks

**Check connection and get folders** uses the current form, including unsaved passwords and settings. It opens an owned Acrylic window showing settings, secure connection, sign-in and folder discovery, plus SMTP when configured. Cancel stops the check. Nothing is saved or sent by checking.

Choose between one and twenty exact server folder names. Current support requires ASCII names. Existing choices are kept when available; new folders are not silently selected. **Use selected folders** updates the form. Creating a password-based account runs this same check before saving the explicit folder selection.

**Also test reading one message** performs one bounded read from the first chosen folder, without marking IMAP mail read or issuing POP3 delete commands. An empty folder is reported separately from a successful read. A saved account also offers a read test when there are no unsaved changes.

**Test SMTP connection** uses the same progress and result window, with settings, secure connection and sign-in stages. It needs neither an incoming password nor enabled sending and never sends email. Errors show the actual server or connection detail when available, with credentials and addresses redacted. Text is selectable and scrollable. No authentication method changes automatically after an error.

## Connect Codex

Open **Your assistant** and choose **Connect to Codex**. Existing matching settings show **Added to Codex**. If a different entry exists, **Update connection** asks before replacing it. Start a new Codex chat and use the provided account-list prompt to check access.

**Manual setup and troubleshooting** contains the configuration path and copyable MCP settings. Saving client settings does not prove a mailbox connection. See [Codex setup](CODEX_SETUP.md).

The footer links to umbertogiacobbi.biz and the AgentInbox Terms and Privacy pages, with VibeWare branding on the right.

## Local data and installation

The app and engine share the same profile. Passwords and Microsoft refresh tokens use Windows Credential Manager; temporary sign-in state uses current-user DPAPI. Account settings use schema 3; send metadata uses schema 4. Unsupported older formats fail without migration or deletion.

A save or sign-in can succeed while old credential cleanup remains pending. The separate warning offers **Retry cleanup**, which retries deletion without repeating the completed action. Removing an account does not remove server messages. See [privacy](PRIVACY.md) and [recovery](RECOVERY.md).

Authorized local installs use the source-built Debug MSIX workflow. Installation may force-close the running AgentInbox app without another prompt and preserves saved data. Store builds are separate, explicitly requested work. See [packaging](../packaging/windows/README.md).
