# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Observable Linux setup drafts and local configuration operations.
# Created with AI: OpenAI Codex, 2026-11-10.
# Human guidance: Umberto Giacobbi; bring the Windows configuration workflow to Linux.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Keep GTK widgets out of account drafts and operation state."""

import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
from threading import Event, Thread
import tomllib

from gi.repository import GLib, GObject

from agentinbox.activity import PROBE_ENV
from agentinbox.errors import MailError
from agentinbox.mcp import TOOLS
from agentinbox.terminal_setup import diagnose, mcp_configuration

ACCOUNT_FIELDS = ("id", "label", "email", "authentication", "incoming", "smtp",
                  "read_enabled", "send_enabled", "folders")


class EndpointDraft(GObject.Object):
    """Editable endpoint fields, with a transient password."""

    host = GObject.Property(type=str, default="")
    port = GObject.Property(type=str, default="")
    tls = GObject.Property(type=str, default="tls")
    username = GObject.Property(type=str, default="")
    password = GObject.Property(type=str, default="")

    def load(self, endpoint):
        """Load server defaults or saved settings.

        Args:
            endpoint: Endpoint fields, optionally including a saved username.
        """
        self.host = endpoint.get("host", "")
        self.port = str(endpoint.get("port", ""))
        self.tls = endpoint.get("tls", "tls")
        if "username" in endpoint:
            self.username = endpoint["username"]

    def settings(self, email):
        """Convert editable port text and build an engine endpoint payload.

        Args:
            email: Mailbox address used when the sign-in username is blank.
        """
        try:
            port = int(self.port)
        except ValueError:
            port = 0  # Let the shared validator report the appropriate field error.
        return {"host": self.host, "port": port, "tls": self.tls,
                "username": self.username.strip() or email}


class AccountDraft(GObject.Object):
    """Own one unsaved editor, including its discovered and selected folders."""

    label = GObject.Property(type=str, default="")
    email = GObject.Property(type=str, default="")
    read_enabled = GObject.Property(type=bool, default=True)
    send_enabled = GObject.Property(type=bool, default=False)
    configure_smtp = GObject.Property(type=bool, default=True)

    def __init__(self, account=None):
        """Start a new draft or edit a public saved snapshot.

        Args:
            account: Public saved account, or None for a new custom connection.
        """
        super().__init__()
        is_new = account is None
        account = account or {}
        self.identity = account.get("id", "")
        self.revision = account.get("revision")
        self.preset = account.get("preset", "custom")
        self.include_preset = not self.identity or "preset" in account
        self.authentication = dict(account.get("authentication", {"method": "password"}))
        self.microsoft_connected = account.get("microsoft_connected", False)
        self.label = account.get("label", "")
        self.email = account.get("email", "")
        self.read_enabled = account.get("read_enabled", True)
        self.send_enabled = account.get("send_enabled", False)
        self.configure_smtp = is_new or account.get("smtp") is not None
        self.incoming = EndpointDraft()
        incoming = account.get("incoming", {"port": 993})
        self.incoming.load(incoming)
        self.protocol = incoming.get("protocol", "imap")
        self.smtp = EndpointDraft()
        self.smtp.load(account.get("smtp") or {"port": 465})
        self.folders = list(account.get("folders", ["INBOX"]))
        self.available_folders = None
        self.connect("notify::configure-smtp", self._smtp_changed)

    @property
    def microsoft(self):
        """Report whether the draft uses Microsoft sign-in."""
        return self.authentication["method"] == "microsoft_oauth"

    def _smtp_changed(self, _draft, _property):
        """Clear sending permission when its server is removed from the draft.

        Args:
            _draft: Draft emitting the property change.
            _property: Property metadata for the SMTP configuration toggle.
        """
        if not self.configure_smtp:
            self.send_enabled = False

    def select_provider(self, identity, presets, client_id):
        """Apply a chosen provider's endpoint defaults and sign-in context.

        Args:
            identity: Provider ID from the engine's preset catalog.
            presets: Shared preset catalog keyed by provider ID.
            client_id: Shipped public Microsoft registration GUID.
        """
        self.preset = identity
        self.include_preset = True
        preset = presets[identity]
        self.authentication = {"method": "password"}
        if tenant := preset.get("microsoft_tenant"):
            self.authentication = {"method": "microsoft_oauth", "tenant": tenant, "client_id": client_id}
        self.microsoft_connected = False
        self.incoming.username = self.smtp.username = ""
        self.incoming.password = self.smtp.password = ""
        offered = preset["incoming"]
        self.select_protocol(self.protocol if not offered or self.protocol in offered
                             else next(iter(offered)), presets)
        self.smtp.load(preset["smtp"] or {"port": 465})

    def protocols(self, presets):
        """List available protocols while keeping the saved protocol editable.

        Args:
            presets: Shared preset catalog keyed by provider ID.
        """
        offered = presets[self.preset]["incoming"] or {"imap": {}, "pop3": {}}
        return tuple(dict.fromkeys((*offered, self.protocol))) if self.identity else tuple(offered)

    def select_protocol(self, protocol, presets):
        """Update the incoming endpoint and reset its folder selection.

        Args:
            protocol: Incoming IMAP or POP3 protocol.
            presets: Shared preset catalog keyed by provider ID.
        """
        self.protocol = protocol
        default_port = 993 if protocol == "imap" else 995
        self.incoming.load(presets[self.preset]["incoming"].get(protocol, {"port": default_port}))
        self.folders = ["INBOX"]
        self.available_folders = None

    def folder_choices(self):
        """Include saved selections so missing folders can be deselected."""
        return tuple(dict.fromkeys([*(self.available_folders or ()), *self.folders]))

    def choose_folder(self, name, selected, maximum):
        """Apply a native folder selection if it fits within the shared limit.

        Args:
            name: Folder label from the server or saved settings.
            selected: Whether this folder should be shared.
            maximum: Maximum selected folders from the engine catalog.
        """
        if selected and name not in self.folders:
            if len(self.folders) >= maximum:
                return False
            self.folders.append(name)
        if not selected and name in self.folders:
            self.folders.remove(name)
        return True

    def arguments(self, *, smtp_only=False):
        """Snapshot the draft before passing it to a background operation.

        Args:
            smtp_only: Omit unrelated incoming fields and credentials for password SMTP checks.
        """
        email = self.email.strip()
        smtp = self.smtp.settings(email) if self.configure_smtp else None
        account = {"id": self.identity, "authentication": dict(self.authentication), "smtp": smtp}
        smtp_password = self.smtp.password if self.configure_smtp and not self.microsoft else ""
        passwords = {"smtp": smtp_password}
        if not smtp_only or self.microsoft:
            incoming = {"protocol": self.protocol, **self.incoming.settings(email)}
            account.update(email=email, incoming=incoming, send_enabled=self.send_enabled and self.configure_smtp)
        if not smtp_only:
            account.update(label=self.label.strip() or email, read_enabled=self.read_enabled, folders=list(self.folders))
            if self.include_preset:
                account["preset"] = self.preset
            passwords["incoming"] = "" if self.microsoft else self.incoming.password
        return {"account": account, "passwords": passwords, "expected_revision": self.revision}


class Configuration(GObject.Object):
    """Own the active draft, public account snapshots and serialized local work."""

    page = GObject.Property(type=str, default="accounts")
    editor_section = GObject.Property(type=str, default="account")
    folder_summary = GObject.Property(type=str, default="")
    activity = GObject.Property(type=object)
    accounts = GObject.Property(type=object)
    draft = GObject.Property(type=object)
    flow = GObject.Property(type=object)
    busy = GObject.Property(type=bool, default=False)
    can_stop = GObject.Property(type=bool, default=False)
    notice = GObject.Property(type=str, default="")
    cleanup_pending = GObject.Property(type=bool, default=False)
    error_field = GObject.Property(type=str, default="")
    codex_status = GObject.Property(type=str, default="Codex settings have not been checked.")
    diagnosis = GObject.Property(type=str, default="")
    startup_status = GObject.Property(type=str, default="Local MCP startup has not been tested.")
    checked = GObject.Property(type=object)

    def __init__(self, application, client_id):
        """Bind one shared engine and load its editor catalog.

        Args:
            application: MailApplication for the same profile used by CLI and MCP.
            client_id: Shipped public Microsoft registration GUID.
        """
        super().__init__()
        self.application = application
        self.client_id = client_id
        catalog = self.manage("account_editor")
        self.presets = catalog["presets"]
        self.max_folders = catalog["max_folders"]
        self.connection = mcp_configuration(application.store.root)
        self.accounts = ()
        self.checked = frozenset()
        self.stop = Event()
        self._poll_source = 0
        self.connect("notify::draft", self._update_folder_summary)

    def new_account(self):
        """Start a fresh account draft on its identity section."""
        self.editor_section = "account"
        self.draft = AccountDraft()

    def select_account(self, identity):
        """Start editing a saved public snapshot.

        Args:
            identity: Saved connection ID, or an empty string to clear the selection.
        """
        if not identity:
            self.close_editor()
            return
        account = next(item for item in self.accounts if item["id"] == identity)
        self.editor_section = "account"
        self.draft = AccountDraft(account)

    def close_editor(self):
        """Discard the active draft after an explicit close action."""
        self.draft = None

    def select_provider(self, identity):
        """Apply the requested provider and publish the resulting draft.

        Args:
            identity: Provider ID from the shared preset catalog.
        """
        self.draft.select_provider(identity, self.presets, self.client_id)
        self.notify("draft")

    def select_protocol(self, protocol):
        """Apply a protocol and publish the resulting draft.

        Args:
            protocol: Incoming IMAP or POP3 protocol chosen by the user.
        """
        self.draft.select_protocol(protocol, self.presets)
        self.notify("draft")

    def choose_folder(self, name, selected):
        """Apply a folder selection and publish its current value.

        Args:
            name: Exact folder name reported by the engine.
            selected: Requested sharing state from the folder control.
        """
        if not self.draft.choose_folder(name, selected, self.max_folders):
            self.notice = f"Choose up to {self.max_folders} folders for this account."
        self._update_folder_summary()
        return name in self.draft.folders

    def _update_folder_summary(self, *_change):
        """Publish the selected count as a projection of the current draft.

        Args:
            *_change: Optional notification arguments after draft replacement.
        """
        self.folder_summary = (f"{len(self.draft.folders)} of {self.max_folders} folders selected"
                               if self.draft else "")

    def refresh_activity(self):
        """Read status without taking over the editor's operation state."""
        try:
            result = self.manage("activity_status")
            status = (result["mcp_connected"], result["has_mailboxes"])
        except (MailError, OSError):
            status = None
        if status != self.activity:
            self.activity = status
        return GLib.SOURCE_CONTINUE

    def manage(self, operation, arguments=None, *, progress=None):
        """Forward one explicit local configuration operation to the shared engine.

        Args:
            operation: Name of an allowlisted local management operation.
            arguments: Engine payload, or None for an empty request.
            progress: Optional background callback for safe stage and state strings.
        """
        return self.application.manage(operation, arguments or {}, progress=progress)

    def run(self, message, action, done, *, failed=None):
        """Run a single engine or client operation off the GTK main loop.

        Args:
            message: Status shown while work starts.
            action: Background callable returning an operation result.
            done: GTK-thread callback receiving a successful result.
            failed: Optional GTK-thread callback receiving the safe failure text.
        """
        if self.busy:
            return
        self.error_field = ""
        self.busy = True
        self.notice = message

        def finish(result, error):
            """Publish the result and release the operation before a follow-up.

            Args:
                result: Background result, or None on failure.
                error: Safe MailError, or None on success.
            """
            self.busy = False
            if error is None:
                done(result)
            else:
                self.notice = str(error)
                self.error_field = error.field or ""
                if failed:
                    failed(str(error))

        def worker():
            """Pass only a result or safe diagnostic back to the GTK thread."""
            try:
                result = action()
            except MailError as error:
                details = str(error)
                if error.server_response:
                    details += "\n" + error.server_response
                safe_error = MailError(error.code, details, field=error.field)
                GLib.idle_add(finish, None, safe_error)
            except Exception:
                GLib.idle_add(finish, None, MailError("operation_failed", "The operation could not finish. Check local setup."))
            else:
                GLib.idle_add(finish, result, None)

        Thread(target=worker, daemon=True).start()

    def progress(self, stage, state):
        """Publish only bounded check stages on the main loop.

        Args:
            stage: Engine stage name, such as incoming_tls or folders.
            state: Engine state, such as started or passed.
        """
        GLib.idle_add(self.set_property, "notice", f"{stage.replace('_', ' ').capitalize()} · {state}")

    def reload(self):
        """Refresh the account list and discard the draft only after a successful read."""
        def load():
            """Read public accounts and separate protected-data cleanup state."""
            return self.manage("list_accounts"), self.manage("profile_info")

        def loaded(result):
            """Publish refreshed accounts and clear prior selections.

            Args:
                result: Current public saved snapshots and local profile information.
            """
            accounts, profile = result
            self.draft = None
            self.checked = frozenset()
            self.accounts = tuple(accounts)
            self.apply_cleanup(profile)
            self.notice = ""

        self.run("Loading accounts…", load, loaded)

    def apply_cleanup(self, result):
        """Show cleanup separately from a committed operation's outcome.

        Args:
            result: Engine response that may report pending protected-data cleanup.
        """
        if "cleanup_pending" in result:
            self.cleanup_pending = result["cleanup_pending"]

    def _accept_account(self, account, message):
        """Update the list and draft directly from the committed response.

        Args:
            account: Public saved snapshot returned after save or sign-in.
            message: Text acknowledging the committed operation.
        """
        identity = account["id"]
        snapshots = {entry["id"]: entry for entry in self.accounts}
        snapshots[identity] = account
        self.checked = self.checked - {identity}
        self.accounts = tuple(snapshots.values())
        self.draft = AccountDraft(account)
        self.apply_cleanup(account)
        self.notice = message

    def save(self, *, sign_in=False):
        """Save the active draft and optionally start Microsoft sign-in afterward.

        Args:
            sign_in: Whether the user explicitly requested a Microsoft device code.
        """
        arguments = self.draft.arguments()

        def saved(account):
            """Keep the committed result even if getting a device code fails.

            Args:
                account: Public snapshot with the new revision and cleanup outcome.
            """
            self._accept_account(account, "Account saved.")
            if sign_in:
                self.run("Account saved. Requesting a Microsoft code…",
                         lambda: self.manage("start_microsoft_signin", {"account_id": account["id"]}),
                         self._show_flow,
                         failed=lambda detail: self.set_property("notice", "Account saved. " + detail))

        self.run("Saving account…", lambda: self.manage("save_account", arguments), saved)

    def _show_flow(self, flow):
        """Expose the device code and schedule a poll using Microsoft's interval.

        Args:
            flow: Engine-issued device code, approved URL and polling interval in seconds.
        """
        self.flow = flow
        self.notice = "Enter the code on Microsoft's sign-in page."
        self._schedule_poll(flow["interval"])

    def _schedule_poll(self, interval):
        """Schedule one device-flow poll.

        Args:
            interval: Engine-provided wait in seconds.
        """
        self._poll_source = GLib.timeout_add_seconds(interval, self._poll_signin)

    def _poll_signin(self):
        """Poll once, preserving a committed sign-in and disposing of failed flows."""
        self._poll_source = 0
        flow_id = self.flow["flow_id"]

        def polled(result):
            """Continue polling or publish the connected account.

            Args:
                result: Pending interval or committed Microsoft account response.
            """
            if result["status"] == "pending":
                self.notice = "Waiting for Microsoft sign-in…"
                self._schedule_poll(result["interval"])
                return
            self.flow = None
            self._accept_account(result["account"], "Microsoft sign-in completed.")

        self.run("Checking Microsoft sign-in…", lambda: self.manage("poll_microsoft_signin", {"flow_id": flow_id}),
                 polled, failed=lambda detail: self.cancel_signin(detail))

    def cancel_signin(self, notice="Microsoft sign-in cancelled.", *, done=None):
        """Stop polling and remove the temporary protected flow.

        Args:
            notice: Final notice, preserving a preceding sign-in failure when present.
            done: Optional no-argument callback after cancellation, such as closing setup.
        """
        if self._poll_source:
            GLib.source_remove(self._poll_source)
            self._poll_source = 0
        flow_id = self.flow["flow_id"]

        def cancelled(result):
            """Clear the displayed code after the engine releases the flow.

            Args:
                result: Engine cancellation response and separate cleanup status.
            """
            self.flow = None
            self.apply_cleanup(result)
            self.notice = notice
            if done:
                done()

        self.run("Closing Microsoft sign-in…", lambda: self.manage("cancel_microsoft_signin", {"flow_id": flow_id}),
                 cancelled, failed=lambda detail: self.set_property("notice", notice + "\n" + detail))

    def remove(self):
        """Revoke the selected saved account after the view confirms the action."""
        identity = self.draft.identity

        def removed(result):
            """Remove the committed account from the local list.

            Args:
                result: Engine removal response and separate cleanup status.
            """
            self.draft = None
            self.accounts = tuple(entry for entry in self.accounts if entry["id"] != identity)
            self.checked = self.checked - {identity}
            self.apply_cleanup(result)
            self.notice = "Account removed from AgentInbox."

        self.run("Removing local account…", lambda: self.manage("remove_account", {"account_id": identity}), removed)

    def check_draft(self, *, smtp_only=False):
        """Check the current draft and update discovered folders for a full check.

        Args:
            smtp_only: Whether to check just the configured sending connection.
        """
        draft = self.draft
        operation = "check_smtp" if smtp_only else "check_account"
        arguments = draft.arguments(smtp_only=smtp_only)

        def checked(result):
            """Display authentication success and update folder choices.

            Args:
                result: Engine check result, including folders for an incoming check.
            """
            if smtp_only:
                self.notice = "Sending connection and sign-in passed."
            else:
                draft.available_folders = tuple(result["folders"])
                self.notify("draft")
                self.notice = ("POP3 connection passed. INBOX is selected." if draft.protocol == "pop3"
                               else "Connection passed. Choose the folders to share and save the account.")

        self.run("Checking the connection…", lambda: self.manage(operation, arguments, progress=self.progress), checked)

    def check_read(self):
        """Request a bounded sample from the selected saved account."""
        identity = self.draft.identity
        self.run("Checking a sample read…", lambda: self.manage("check_read", {"account_id": identity}),
                 lambda result: self.set_property("notice", result["message"]))

    def check_all(self):
        """Check saved snapshots in order, stopping after the first failure or a stop request."""
        accounts = self.accounts
        self.stop.clear()
        self.checked = frozenset()
        self.can_stop = True

        def publish(identity):
            """Mark one successful check on the GTK thread.

            Args:
                identity: Account ID whose saved connection passed.
            """
            self.checked = self.checked | {identity}

        def check():
            """Check each saved connection without reading or sending mail."""
            count = 0
            for account in accounts:
                if self.stop.is_set():
                    break
                payload = {key: account[key] for key in ACCOUNT_FIELDS}
                if "preset" in account:
                    payload["preset"] = account["preset"]
                arguments = {"account": payload, "passwords": {"incoming": "", "smtp": ""},
                             "expected_revision": account["revision"]}
                GLib.idle_add(self.set_property, "notice", f"Checking saved account {count + 1} of {len(accounts)}…")
                try:
                    self.manage("check_account", arguments, progress=self.progress)
                except MailError as error:
                    raise MailError(error.code, f"Saved account {count + 1} of {len(accounts)}: {error}",
                                    server_response=error.server_response) from None
                count += 1
                GLib.idle_add(publish, account["id"])
            return count

        def complete(count):
            """Report how many saved accounts passed.

            Args:
                count: Number of accounts checked successfully before completion or stopping.
            """
            self.can_stop = False
            self.notice = f"{count} of {len(accounts)} saved connections passed."

        self.run("Checking saved accounts…", check, complete,
                 failed=lambda _detail: self.set_property("can-stop", False))

    def check_profile(self, *, check_storage=False):
        """Run local diagnosis and optionally use a disposable keyring value.

        Args:
            check_storage: Whether to round-trip a disposable protected value.
        """
        def checked(result):
            """Display only the engine's local prerequisite results.

            Args:
                result: Local profile checks and cleanup status.
            """
            self.diagnosis = "\n".join(
                f"{check['name'].replace('_', ' ').capitalize()} · {check['status']} · {check['message']}"
                for check in result["checks"]
            )
            self.apply_cleanup(result)
            self.notice = "Local diagnosis completed."

        self.run("Checking local setup…", lambda: diagnose(self.application, check_storage), checked)

    def retry_cleanup(self):
        """Retry retired protected values separately from saves and sign-in."""
        def cleaned(result):
            """Publish the current state of the cleanup queue.

            Args:
                result: Cleanup result and any remaining queue status.
            """
            self.apply_cleanup(result)
            self.notice = "Protected-data cleanup is still pending." if self.cleanup_pending else "Local cleanup completed."

        self.run("Retrying local cleanup…", lambda: self.manage("retry_cleanup"), cleaned)

    def _launch(self):
        """Return the generated stdio launch arguments and explicit profile."""
        entry = self.connection["mcpServers"]["agentinbox"]
        return [entry["command"], *entry["args"]], entry["env"]

    def _codex_arguments(self):
        """Build the Codex CLI command to register this specific profile."""
        command, environment = self._launch()
        return ["codex", "mcp", "add", "agentinbox", "--env",
                f"AGENTINBOX_DATA_DIR={environment['AGENTINBOX_DATA_DIR']}", "--", *command]

    def codex_command(self):
        """Return the safely quoted registration command for manual setup."""
        return shlex.join(self._codex_arguments())

    def _check_codex(self):
        """Read user-level Codex settings and report only the entry's status."""
        home = Path(os.environ.get("CODEX_HOME") or (Path.home() / ".codex"))
        if not home.is_absolute():
            return "CODEX_HOME needs an absolute directory."
        try:
            with (home / "config.toml").open("rb") as stream:
                data = stream.read(1_048_577)
            if len(data) > 1_048_576:
                return "Codex settings exceed the local inspection limit."
            settings = tomllib.loads(data.decode("utf-8"))
        except FileNotFoundError:
            return "The user-level Codex settings file was not found."
        except (OSError, UnicodeError, ValueError):
            return "The user-level Codex settings could not be read."
        servers = settings.get("mcp_servers", {})
        entry = servers.get("agentinbox") if isinstance(servers, dict) else None
        if entry is None:
            return "AgentInbox is absent from user-level Codex settings."
        command, environment = self._launch()
        if (isinstance(entry, dict) and entry.get("enabled", True) is True and "url" not in entry
                and entry.get("command") == command[0] and entry.get("args") == command[1:]
                and isinstance(entry.get("env"), dict)
                and entry["env"].get("AGENTINBOX_DATA_DIR") == environment["AGENTINBOX_DATA_DIR"]):
            return "AgentInbox is enabled in user-level Codex settings for this profile."
        return "The AgentInbox entry in user-level Codex settings differs from this profile or is disabled."

    def check_codex(self):
        """Check Codex settings without launching any configured command."""
        self.run("Checking Codex settings…", self._check_codex, self._show_codex_status)

    def _show_codex_status(self, status):
        """Publish user-level registration status independently of MCP startup.

        Args:
            status: Safe description of the generated entry's registration state.
        """
        self.codex_status = status
        self.notice = "Codex settings check completed."

    def connect_codex(self):
        """Use the local Codex CLI after explicit confirmation in the view."""
        def register():
            """Register only the AgentInbox MCP entry and verify its settings."""
            arguments = self._codex_arguments()
            executable = shutil.which(arguments[0])
            if not executable:
                raise MailError("codex_unavailable", "The Codex CLI was not found. Use the connection settings in Codex.")
            result = subprocess.run([executable, *arguments[1:]], stdin=subprocess.DEVNULL,
                                    stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
            if result.returncode:
                raise MailError("codex_registration_failed", "Codex could not register AgentInbox. Check the entry in Codex settings.")
            return self._check_codex()

        self.run("Registering AgentInbox in Codex…", register, self._show_codex_status)

    def test_startup(self):
        """Verify the generated stdio command through initialization and tools/list."""
        def probe():
            """Start the known launcher with no mailbox tool calls."""
            command, environment = self._launch()
            requests = [
                {"id": 1, "method": "initialize", "params": {
                    "protocolVersion": "2025-06-18", "capabilities": {},
                    "clientInfo": {"name": "AgentInbox setup", "version": "1"},
                }},
                {"method": "notifications/initialized"},
                {"id": 2, "method": "tools/list"},
            ]
            lines = "".join(json.dumps({"jsonrpc": "2.0", **request}) + "\n" for request in requests)
            try:
                result = subprocess.run(command, input=lines, encoding="utf-8",
                                        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                        env={**os.environ, **environment, PROBE_ENV: "1"},
                                        timeout=30, check=True)
                initialized, listed = [json.loads(line) for line in result.stdout.splitlines()]
                names = {tool["name"] for tool in listed["result"]["tools"]}
                if (initialized["id"] != 1 or initialized["result"]["serverInfo"]["name"] != "agentinbox"
                        or listed["id"] != 2 or names != {tool["name"] for tool in TOOLS}):
                    raise ValueError
            except (OSError, subprocess.SubprocessError, ValueError, KeyError, TypeError):
                raise MailError("mcp_startup_failed", "Local MCP startup failed. Check the Python launcher and profile.") from None
            return f"Local MCP startup passed. AgentInbox returned {len(names)} mail tools."

        self.run("Testing local MCP startup…", probe, self._show_startup_status, failed=self._show_startup_status)

    def _show_startup_status(self, status):
        """Keep the last local startup result available for troubleshooting.

        Args:
            status: Safe result of the local stdio handshake.
        """
        self.startup_status = status
        self.notice = status
