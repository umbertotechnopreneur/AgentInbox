# AgentInbox on Linux

I wanted to connect my existing mailboxes without a Windows configuration window. The Python CLI guides you through setup, keeps passwords in your Linux keyring and prints one MCP connection for the accounts and folders you choose.

You can connect Gmail, Microsoft, iCloud, GoDaddy and other compatible IMAP/POP3 mailboxes. Select up to twenty IMAP folders per account to work across dozens of folders across accounts. POP3 has one INBOX. Linux uses source only; there is no Linux graphical app or bundled runtime.

## Check your session

Use Python 3.11 or newer and a complete AgentInbox source checkout. The engine needs no pip, npm or .NET packages. Protected storage needs your distribution's libsecret runtime and an unlocked Secret Service keyring, such as GNOME Keyring, in the user session running both the CLI and MCP. See the [libsecret documentation](https://gnome.pages.gitlab.gnome.org/libsecret/).

Run these commands from the checkout:

```sh
python3 -B python/run.py doctor
python3 -B python/run.py doctor --check-storage
```

The first checks settings, TLS initialization, native libraries and the session-bus environment without accessing credentials. The second writes, reads and removes one random disposable protected value. Your keyring may ask you to unlock it. Neither command reads saved account credentials or contacts providers. Failed checks exit with status 1. With `--json`, `ok: true` and `result.ready: false` mean diagnosis completed and found a problem.

If protected-data cleanup remains pending, restore keyring access and run `python3 -B python/run.py profile cleanup`. Retry cleanup rather than repeating a completed save or sign-in. No plaintext credential fallback is available.

The diagnostic removes only its own disposable value. If its removal cannot be recorded, preserve the reported reference for local keyring cleanup; `profile cleanup` can retry only recorded entries.

## Add a mailbox

```sh
python3 -B python/run.py accounts add
```

Choose a provider, enter your existing email address and name the connection. Select IMAP or POP3 where supported. IMAP asks for exact folder names separated by commas; the default is INBOX. Custom settings also ask for the server, mandatory TLS mode, port and username. Use your provider's settings for GoDaddy or another custom service.

Sending starts disabled. Answer `yes` to the sending question only if you want your AI agent to send when you request it. Review the summary and confirm saving. Password-based accounts then request the incoming password with hidden input; configured SMTP requests its password separately. No password is accepted as a command-line option. Cancelling during the questions or password entry, ending input or declining the summary saves no draft account. If you interrupt after saving begins, inspect `accounts list` and `accounts show ACCOUNT_ID` before repeating the operation; the account may already have been saved.

Gmail requires an eligible [Google app password](https://support.google.com/accounts/answer/185833). iCloud uses an [Apple app-specific password](https://support.apple.com/en-us/102654). Guided setup prints each preset's sign-in guidance and official help link. Provider compatibility does not mean your provider account has been validated.

For Outlook.com or Microsoft 365, supply a public-client registration GUID:

```sh
python3 -B python/run.py accounts add --client-id PUBLIC_CLIENT_GUID
python3 -B python/run.py accounts signin ACCOUNT_ID
```

Replace the placeholders with your registration and saved ID. Microsoft presets always select Microsoft sign-in. The public application ID is not a password or client secret. Use a registration supplied by the maintainer or configured by your organization; see [public-client registration](CLI_SETUP_GUIDE.md#supply-a-public-client-registration). The terminal displays Microsoft's URL and device code; enter your password only on Microsoft's page. Real consent and provider access need their own validation.

The settings-file workflow remains available for repeatable setup and updates:

```sh
python3 -B python/run.py accounts add --file /absolute/path/accounts.setup.json --entry gmail
python3 -B python/run.py accounts show ACCOUNT_ID
python3 -B python/run.py accounts update ACCOUNT_ID --revision CURRENT_REVISION --file /absolute/path/accounts.setup.json --entry gmail
```

Supply `--file` and `--entry` together. Setup JSON contains settings only. See the [two-mailbox guide](CLI_SETUP_GUIDE.md) and [command reference](CLI_REFERENCE.md) for password replacement and SMTP settings.

## Check the saved account

```sh
python3 -B python/run.py accounts list
python3 -B python/run.py accounts check ACCOUNT_ID
python3 -B python/run.py accounts folders ACCOUNT_ID
```

Replace `ACCOUNT_ID` with the ID printed when saving. Saving confirms local configuration, not provider access. A connection check contacts your provider but sends no mail. Add `--read-message` only when you want a bounded sample-read check.

## Connect your MCP client

```sh
python3 -B python/run.py mcp-config
```

This prints a JSON `mcpServers` entry with the current absolute Python interpreter, separate launcher arguments and selected profile. It preserves a virtual environment's interpreter path. Use the entry in a client supporting local stdio MCP. It does not edit client configuration. Keep the checkout and interpreter at those locations and run the client in the same user session as the keyring.

One MCP process exposes all enabled accounts and selected folders. Starting it does not verify client integration or access to providers. Open a new client session after registering the entry and request the account list first. See the [connection guide](CODEX_SETUP.md).

## Keep the same data directory

The default is `$XDG_DATA_HOME/agentinbox`, or `~/.local/share/agentinbox` if `XDG_DATA_HOME` is unset or empty. Settings contain credential references; passwords and refresh tokens stay in Secret Service. To choose another profile, set an absolute path before setup and configuration generation:

```sh
export AGENTINBOX_DATA_DIR="$HOME/.local/share/agentinbox-work"
python3 -B python/run.py profile show
python3 -B python/run.py mcp-config
```

A relative path or an existing file is rejected. Do not point a test at your normal profile. On a live USB with persistence, verify that your profile and keyring survive a reboot. An SSH or headless session needs its own working session bus and unlocked Secret Service; Python alone is insufficient.

I'm Umberto Giacobbi. I built and maintain AgentInbox, with help from contributors.
