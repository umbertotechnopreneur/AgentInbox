# Windows configuration

The WinUI 3 app configures AgentInbox on Windows 11. Email operations run through Codex and the Python engine. See [validation](VALIDATION.md) for the source, package and provider checks actually performed.

## Connections

Choose **Add connection**, or select a saved connection by its name and email address. The list sits beside the editor on wide windows and above it on narrower windows. No account form appears until you create or select one.

**Connection name** stays above the native **Account**, **Incoming** and **Outgoing** tabs. Edit it and press **Save changes** to rename the connection. Tab icons are colored; the selected label is bold. Save and Cancel stay below the form with the current save status. The window uses Acrylic and adapts to display scaling and available space.

In **Account**, choose a preset or custom servers, email address and authentication method. Password or app password is the default. Supported Microsoft servers also offer explicit **Web authorization with Microsoft**, using your own public-client registration. Save before starting that sign-in flow. The browser opens only when you press its button. See [authentication](AUTHENTICATION.md).

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
