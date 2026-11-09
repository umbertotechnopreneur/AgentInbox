# AgentInbox on Linux

I wanted to set up my existing mailboxes from Linux. I added a GTK setup app so you can connect your accounts, choose folders and share them through one MCP connection. The CLI can do the same setup on a machine without a desktop.

You can connect Gmail, Microsoft, iCloud, GoDaddy and other compatible IMAP/POP3 mailboxes. Select up to twenty IMAP folders per account to work across dozens of folders across accounts. POP3 has one INBOX.

## Install the setup app

On a Debian-based desktop with Python 3.11 or newer and GTK 4.8 or newer, install both matching local packages together. From their directory, run:

```sh
sudo apt install ./agentinbox-engine_*_all.deb ./agentinbox_*_all.deb
```

Open **AgentInbox** from the application menu or run `agentinbox-setup` as your desktop user. Choose **Add account**, pick a provider and enter your email address. In **Read mail**, enter your sign-in details and check the connection to discover folders. Use **Sign in with Microsoft** for Outlook.com or Microsoft 365. New accounts open **Send mail** with SMTP configuration enabled. Fill in the sending server details or clear **Configure a sending server** to skip SMTP. Turn on **Allow sending** only if you want your assistant to send when you ask. Use **Connect Codex** to register the local MCP entry or copy the connection settings.

To build the packages from a checkout, install `fakeroot` and `desktop-file-utils`. From the repository root, run:

```sh
fakeroot python3 -B scripts/build-linux-deb.py
```

The build writes both packages and their SHA-256 checksums to `artifacts/linux-deb/<version>/`. They use the system Python. To run GTK directly from source, install `python3-gi` and `gir1.2-gtk-4.0`, then run `PYTHONPATH=python /usr/bin/python3 -B -m agentinbox_gtk.gui` from the checkout.

## Use the tray

If your desktop supports StatusNotifierItem or AppIndicator icons, open AgentInbox once to add it to the tray. The icon opens setup and its menu links to **Accounts**, **Connect Codex**, **Troubleshooting**, **About** and **Exit AgentInbox**. Closing setup leaves the icon available while the tray host is active.

Notifications show when the first live MCP session connects, when the last one disconnects, or when the profile has no configured mailboxes. Use **Exit AgentInbox** to close the setup app. See the [command reference](CLI_REFERENCE.md#back-up-and-restore-accounts) for encrypted backups.

## Run only the engine

On the Debian-based computer that will run the MCP process, install the engine package by itself:

```sh
sudo apt install ./agentinbox-engine_*_all.deb
agentinbox doctor
agentinbox mcp-config
```

Run `agentinbox` as the same user and in the same keyring session as the MCP client. The generated registration starts the installed engine with the selected profile. For a Dot workflow, [connect a computer to your dot](https://learn.chatgpt.com/docs/dots/computers-and-apps) if you want it to start a local Codex task. Check package installation, local stdio support and keyring access on the target computer separately.

## Check your session

Use Python 3.11 or newer. The examples below run from a complete source checkout. With the engine package installed, replace `python3 -B python/run.py` with `agentinbox`. The engine needs no pip, npm or .NET packages. Protected storage needs your distribution's libsecret runtime and an unlocked Secret Service keyring, such as GNOME Keyring, in the user session running both the CLI and MCP. See the [libsecret documentation](https://gnome.pages.gitlab.gnome.org/libsecret/).

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
