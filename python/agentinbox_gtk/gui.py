# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Native GTK 4 configuration views and Linux app entry point.
# Created with AI: OpenAI Codex, 2026-11-10.
# Human guidance: Umberto Giacobbi; bring the Windows configuration workflow to Linux.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Configure AgentInbox through GTK 4 on Linux."""

import argparse
import json
from pathlib import Path
import sys
from uuid import UUID

import gi

gi.require_version("Gtk", "4.0")
from gi.repository import Gio, GObject, Gtk, Pango

from agentinbox import __version__
from agentinbox.application import MailApplication
from agentinbox.config import data_directory
from agentinbox.errors import MailError
from .model import AccountDraft, Configuration
from agentinbox.terminal_setup import PROVIDER_HELP

SYNC = GObject.BindingFlags.SYNC_CREATE
TWO_WAY = SYNC | GObject.BindingFlags.BIDIRECTIONAL
SECURITY = {"tls": "TLS", "starttls": "STARTTLS"}
REPOSITORY = "https://github.com/umbertotechnopreneur/AgentInbox"


def column(spacing=10):
    """Create a vertical group of native controls.

    Args:
        spacing: Gap between controls in logical pixels.
    """
    return Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=spacing)


def label(text="", *, selectable=False, heading=False):
    """Create a left-aligned, wrapping label.

    Args:
        text: Plain label text, including any untrusted display values.
        selectable: Whether the user can select the text.
        heading: Whether to use the theme's heading style.
    """
    widget = Gtk.Label(label=text, xalign=0, wrap=True, wrap_mode=Pango.WrapMode.WORD_CHAR,
                       selectable=selectable)
    if heading:
        widget.add_css_class("title-3")
    return widget


def describe(widget, name, tooltip=None):
    """Give a control an explicit accessible name and descriptive tooltip.

    Args:
        widget: Native GTK control receiving the metadata.
        name: Accessible label describing the control.
        tooltip: Longer explanation, or None to reuse the name.
    """
    widget.update_property([Gtk.AccessibleProperty.LABEL], [name])
    widget.set_tooltip_text(tooltip or name)
    return widget


def button(text, tooltip, action):
    """Create a button forwarding an explicit native click.

    Args:
        text: Visible and accessible action name.
        tooltip: Explanation of the action.
        action: No-argument callback.
    """
    widget = describe(Gtk.Button(label=text), text, tooltip)
    widget.connect("clicked", lambda _button: action())
    return widget


def link(text, url):
    """Create a labeled link that opens in the user's browser.

    Args:
        text: Visible and accessible link label.
        url: Official help or product URL.
    """
    widget = describe(Gtk.LinkButton(uri=url, label=text), text, f"Open {text} in your browser")
    widget.set_halign(Gtk.Align.START)
    return widget


def choice(options, selected, name, change):
    """Render a native dropdown and forward the selected key.

    Args:
        options: Ordered mapping of internal keys to display labels.
        selected: Current key, or None to leave the dropdown unselected.
        name: Accessible name and tooltip.
        change: Callback accepting a selected key.
    """
    keys = list(options)
    widget = describe(Gtk.DropDown.new_from_strings(list(options.values())), name)
    widget.set_selected(keys.index(selected) if selected in options else Gtk.INVALID_LIST_POSITION)

    def changed(dropdown, _property):
        """Forward only a valid selection.

        Args:
            dropdown: Native selector that emitted the change.
            _property: Selected-position property metadata.
        """
        index = dropdown.get_selected()
        if index < len(keys):
            change(keys[index])

    widget.connect("notify::selected", changed)
    widget.set_hexpand(True)
    return widget


def toggle(model, field, text, tooltip):
    """Bind one checkbox to a boolean draft property.

    Args:
        model: Observable editor draft.
        field: GObject property bound to the active state.
        text: Visible and accessible checkbox label.
        tooltip: Explanation of the permission or setting.
    """
    widget = describe(Gtk.CheckButton(label=text), text, tooltip)
    model.bind_property(field, widget, "active", TWO_WAY)
    return widget


class SetupWindow(Gtk.ApplicationWindow):
    """Bind GTK pages to one configuration model for the window lifetime."""

    def __init__(self, app, model):
        """Build the pages once and start loading saved connections.

        Args:
            app: Owning GTK application instance.
            model: Observable configuration state for the selected profile.
        """
        super().__init__(application=app, title="AgentInbox", default_width=850, default_height=750)
        self.model = model
        self.fields = {}
        root = column()
        for side in ("top", "bottom", "start", "end"):
            root.set_property(f"margin-{side}", 18)
        self.set_child(root)
        root.append(label("AgentInbox", heading=True))
        root.append(label("Connect your mailboxes and selected folders to your AI assistant through one MCP connection."))

        self.pages = Gtk.Stack(vexpand=True)
        nav = Gtk.StackSwitcher(stack=self.pages, halign=Gtk.Align.START)
        root.append(nav)
        root.append(self.pages)
        notice = label(selectable=True)
        model.bind_property("notice", notice, "label", SYNC)
        root.append(notice)
        cleanup = button("Retry protected-data cleanup", "Retry removing retired credentials from your keyring.",
                         model.retry_cleanup)
        model.bind_property("cleanup-pending", cleanup, "visible", SYNC)
        root.append(cleanup)
        stop = button("Stop after this account", "Finish the current connection check, then stop checking saved accounts.",
                      model.stop.set)
        model.bind_property("can-stop", stop, "visible", SYNC)
        root.append(stop)

        self.accounts_page()
        self.assistant_page()
        self.troubleshooting_page()
        self.about_page()
        tab = nav.get_first_child()
        for tab_name in ("Accounts", "Connect Codex", "Troubleshooting", "About"):
            describe(tab, tab_name, f"Open the {tab_name} page")
            tab = tab.get_next_sibling()

        for field in ("busy", "flow"):
            model.connect(f"notify::{field}", self.update_activity, cleanup)
        model.connect("notify::accounts", self.render_accounts)
        model.connect("notify::checked", self.render_accounts)
        model.connect("notify::draft", self.render_editor)
        model.connect("notify::flow", self.render_signin)
        model.connect("notify::error-field", self.focus_error)
        self.connect("close-request", self.close_requested)
        self.render_accounts()
        self.render_editor()
        model.reload()

    def add_page(self, content, name, title):
        """Add one scrollable page with its content anchored to the top.

        Args:
            content: Page's native vertical layout.
            name: Stable navigation ID.
            title: Visible navigation tab label.
        """
        scroller = Gtk.ScrolledWindow(hscrollbar_policy=Gtk.PolicyType.NEVER, child=content)
        content.set_valign(Gtk.Align.START)
        content.set_margin_top(10)
        content.set_margin_end(10)
        self.pages.add_titled(scroller, name, title)

    def accounts_page(self):
        """Build saved-account controls and slots for the draft and device flow."""
        page = column()
        self.account_actions = column()
        self.account_list = column(5)
        self.editor = column()
        self.signin = column()
        self.signin_actions = None
        self.account_actions.append(self.account_list)
        self.account_actions.append(self.editor)
        page.append(self.signin)
        page.append(self.account_actions)
        self.add_page(page, "accounts", "Accounts")

    def render_accounts(self, *_change):
        """Render current saved snapshots and their session-only check marks.

        Args:
            *_change: Optional model notification arguments.
        """
        self.clear(self.account_list)
        accounts = self.model.accounts
        if accounts:
            options = {}
            for account in accounts:
                checked = " ✓" if account["id"] in self.model.checked else ""
                options[account["id"]] = f"{account['label']} · {account['email']}{checked}"
            draft = self.model.draft
            selection = draft.identity if draft else None
            self.account_list.append(label("Saved connections", heading=True))
            picker = choice(options, selection, "Choose a saved account to edit", self.select_account)
            picker.set_enable_search(True)
            self.account_list.append(picker)
        else:
            self.account_list.append(label("Add your first mailbox from Gmail, Microsoft, iCloud, GoDaddy or another compatible provider."))

        actions = Gtk.Box(spacing=8)
        actions.append(button("Add account", "Open a new connection form.", self.new_account))
        actions.append(button("Refresh", "Reload saved accounts and close the current editor.", self.model.reload))
        if accounts:
            actions.append(button("Test all", "Check saved connections and sign-in in order. No mail is read or sent.",
                                  self.model.check_all))
        self.account_list.append(actions)

    def select_account(self, identity):
        """Start an editor from an explicitly selected public snapshot.

        Args:
            identity: Saved account ID chosen in the selector.
        """
        account = next(item for item in self.model.accounts if item["id"] == identity)
        self.model.draft = AccountDraft(account)

    def new_account(self):
        """Open an empty editor only after the user asks to add an account."""
        self.model.draft = AccountDraft()

    def render_editor(self, *_change):
        """Bind the active draft to the three native editor tabs.

        Args:
            *_change: Optional model notification arguments.
        """
        draft = self.model.draft
        previous_tab = getattr(self, "tabs", None)
        tab_index = previous_tab.get_current_page() if previous_tab and self.bound_draft is draft else 0
        if draft is not getattr(self, "bound_draft", None):
            self.render_accounts()
        self.bound_draft = draft
        self.clear(self.editor)
        self.fields.clear()
        if not draft:
            self.tabs = None
            return
        self.editor.append(label("Edit connection" if draft.identity else "New connection", heading=True))
        self.tabs = Gtk.Notebook()
        for title, content in (
            ("Account", self.identity_fields(draft)),
            ("Read mail", self.incoming_fields(draft)),
            ("Send mail", self.sending_fields(draft)),
        ):
            content.set_margin_top(12)
            content.set_margin_bottom(12)
            content.set_margin_start(12)
            content.set_margin_end(12)
            tab = label(title)
            describe(tab, title, f"Open the {title} settings")
            self.tabs.append_page(content, tab)
        self.editor.append(self.tabs)
        self.tabs.set_current_page(tab_index)

        actions = Gtk.Box(spacing=8)
        save = button("Save changes", "Save the current account and its sharing permissions.", self.model.save)
        save.add_css_class("suggested-action")
        actions.append(save)
        actions.append(button("Close editor", "Discard unsaved changes and close the account form.",
                              lambda: self.model.set_property("draft", None)))
        if draft.identity:
            actions.append(button("Remove account", "Remove local access and the saved sign-in for this account.",
                                  lambda: self.confirm("Remove this account?",
                                                       "This removes local access to the mailbox. Your email stays with your provider.",
                                                       "Remove account", self.model.remove)))
        self.editor.append(actions)

    def identity_fields(self, draft):
        """Build provider, mailbox identity and sharing controls.

        Args:
            draft: Current observable account editor.
        """
        content = column()
        providers = {key: preset["label"] for key, preset in self.model.presets.items()}

        def provider_changed(identity):
            """Apply an explicitly chosen preset and refresh its visible fields.

            Args:
                identity: Provider ID from the shared engine catalog.
            """
            draft.select_provider(identity, self.model.presets, self.model.client_id)
            self.render_editor()

        provider = choice(providers, draft.preset, "Email provider", provider_changed)
        self.fields["preset"] = (0, provider)
        content.append(provider)
        preset = self.model.presets[draft.preset]
        if draft.microsoft and not preset.get("microsoft_tenant"):
            content.append(label("Sign in on Microsoft's website to authorize this mailbox."))
        elif preset.get("notice") and (draft.microsoft or not preset.get("microsoft_tenant")):
            content.append(label(preset["notice"]))
        else:
            content.append(label("Enter the mail settings from your provider. Use an app password if your provider requires one."))
        if draft.preset in PROVIDER_HELP:
            content.append(link(f"{preset['label']} sign-in help", PROVIDER_HELP[draft.preset]))

        grid = Gtk.Grid(column_spacing=12, row_spacing=8)
        self.entry(grid, 0, "Email address", draft, "email", "email", 0, 254)
        self.entry(grid, 1, "Connection name", draft, "label", "label", 0, 100,
                   placeholder="Defaults to your email address")
        content.append(grid)
        content.append(toggle(draft, "read-enabled", "Allow reading",
                              "Let your assistant search and read in the folders you choose."))
        if draft.microsoft:
            content.append(label("Microsoft sign-in is saved." if draft.microsoft_connected
                                 else "Sign in to authorize reading and any enabled sending."))
            content.append(button("Sign in with Microsoft", "Save these settings and request a code for Microsoft's website.",
                                  lambda: self.model.save(sign_in=True)))
        return content

    def incoming_fields(self, draft):
        """Build incoming settings, folder selection and reading checks.

        Args:
            draft: Current observable account editor.
        """
        content = column()
        protocols = {protocol: protocol.upper() for protocol in draft.protocols(self.model.presets)}

        def protocol_changed(protocol):
            """Switch protocol and show its fresh endpoint defaults.

            Args:
                protocol: Incoming IMAP or POP3 protocol.
            """
            draft.select_protocol(protocol, self.model.presets)
            self.render_editor()

        protocol = choice(protocols, draft.protocol, "Incoming mail protocol", protocol_changed)
        self.fields["incoming.protocol"] = (1, protocol)
        content.append(protocol)
        content.append(self.endpoint_fields(draft.incoming, "incoming", 1, draft.microsoft))
        content.append(button("Check connection and get folders", "Check this draft and discover folders without saving or sending.",
                              self.model.check_draft))
        content.append(self.folder_controls(draft))
        if draft.identity:
            content.append(button("Check reading", "Use saved settings to check a bounded sample read.",
                                  self.model.check_read))
        return content

    def folder_controls(self, draft):
        """Display POP3 INBOX or selectable IMAP folders.

        Args:
            draft: Current account draft and its engine-supplied folder choices.
        """
        content = column(5)
        if draft.protocol == "pop3":
            content.append(label("POP3 shares INBOX. The server does not expose other folders."))
            return content
        maximum = self.model.max_folders
        count = label(f"{len(draft.folders)} of {maximum} folders selected")
        content.append(count)
        if draft.available_folders is None:
            content.append(label("Check the connection to list the folders available on the server."))
        choices = column(4)

        def selected(widget, name):
            """Forward one selection and show the shared limit.

            Args:
                widget: Native checkbox emitting the selection.
                name: Exact engine folder name.
            """
            if not draft.choose_folder(name, widget.get_active(), maximum):
                widget.set_active(False)
                self.model.notice = f"Choose up to {maximum} folders for this account."
            count.set_label(f"{len(draft.folders)} of {maximum} folders selected")

        for name in draft.folder_choices():
            missing = draft.available_folders is not None and name not in draft.available_folders
            caption = f"{name} (unavailable)" if missing else name
            check = describe(Gtk.CheckButton(active=name in draft.folders), caption,
                             f"Share the {caption} folder")
            check.set_child(label(caption))
            check.connect("toggled", selected, name)
            choices.append(check)
        viewport = Gtk.ScrolledWindow(hscrollbar_policy=Gtk.PolicyType.NEVER, max_content_height=185,
                                      propagate_natural_height=True, child=choices)
        self.fields["folders"] = (1, viewport)
        content.append(viewport)
        return content

    def sending_fields(self, draft):
        """Build optional SMTP configuration separately from permission to send.

        Args:
            draft: Current observable account editor.
        """
        content = column()
        configure = toggle(draft, "configure-smtp", "Configure a sending server",
                           "Add SMTP settings so you can check the sending connection.")
        self.fields["smtp.configured"] = (2, configure)
        content.append(configure)
        settings = column()
        settings.append(self.endpoint_fields(draft.smtp, "smtp", 2, draft.microsoft))
        settings.append(toggle(draft, "send-enabled", "Allow sending",
                               "Let your assistant send through MCP when you explicitly request it."))
        settings.append(button("Check sending connection", "Check SMTP sign-in without submitting email or saving changes.",
                               lambda: self.model.check_draft(smtp_only=True)))
        draft.bind_property("configure-smtp", settings, "visible", SYNC)
        content.append(settings)
        return content

    def endpoint_fields(self, endpoint, prefix, tab, microsoft):
        """Bind native controls to one incoming or outgoing endpoint.

        Args:
            endpoint: Observable endpoint draft.
            prefix: Engine field prefix, either incoming or smtp.
            tab: Zero-based editor tab index for field error navigation.
            microsoft: Whether the provider uses OAuth instead of a password.
        """
        grid = Gtk.Grid(column_spacing=12, row_spacing=8)
        self.entry(grid, 0, "Server", endpoint, "host", f"{prefix}.host", tab, 255)
        self.entry(grid, 1, "Port", endpoint, "port", f"{prefix}.port", tab, 5)
        security = choice(SECURITY, endpoint.tls, "Connection security",
                          lambda selected: endpoint.set_property("tls", selected))
        self.fields[f"{prefix}.tls"] = (tab, security)
        grid.attach(label("Connection security"), 0, 2, 1, 1)
        grid.attach(security, 1, 2, 1, 1)
        self.entry(grid, 3, "Sign-in username", endpoint, "username", f"{prefix}.username", tab, 255,
                   placeholder="Defaults to your email address")
        if not microsoft:
            self.entry(grid, 4, "Password or app password", endpoint, "password", f"passwords.{prefix}", tab,
                       4096, password=True)
        return grid

    def entry(self, grid, row, text, model, field, engine_field, tab, maximum, *, placeholder="", password=False):
        """Attach a labeled, bounded entry bound to an observable field.

        Args:
            grid: Parent native form grid.
            row: Zero-based row in the grid.
            text: Label and accessible entry name.
            model: Observable account or endpoint draft.
            field: Model property bound to the entry text.
            engine_field: Validation field path reported by the engine.
            tab: Zero-based editor tab for the field.
            maximum: Maximum number of editable characters.
            placeholder: Optional native hint for a blank entry.
            password: Use the native masked entry when True.
        """
        if password:
            widget = Gtk.PasswordEntry(show_peek_icon=True, hexpand=True)
            widget.get_delegate().set_max_length(maximum)
        else:
            widget = Gtk.Entry(max_length=maximum, hexpand=True, placeholder_text=placeholder)
        describe(widget, text)
        model.bind_property(field, widget, "text", TWO_WAY)
        self.fields[engine_field] = (tab, widget)
        grid.attach(label(text), 0, row, 1, 1)
        grid.attach(widget, 1, row, 1, 1)

    def focus_error(self, _model, _property):
        """Navigate to the field associated with the current safe engine error.

        Args:
            _model: Model that emitted the error field change.
            _property: Error field property metadata.
        """
        if target := self.fields.get(self.model.error_field):
            tab, widget = target
            self.pages.set_visible_child_name("accounts")
            self.tabs.set_current_page(tab)
            widget.grab_focus()

    def render_signin(self, _model, _property):
        """Show the current Microsoft device code and native actions.

        Args:
            _model: Model emitting the active-flow change.
            _property: Active-flow property metadata.
        """
        self.clear(self.signin)
        self.signin_actions = None
        if flow := self.model.flow:
            self.signin.append(label("Sign in with Microsoft", heading=True))
            self.signin.append(label(f"Enter this code on Microsoft's page: {flow['user_code']}", selectable=True))
            actions = Gtk.Box(spacing=8)
            actions.append(link("Open Microsoft sign-in", flow["verification_uri"]))
            actions.append(button("Copy code", "Copy the current Microsoft device code.",
                                  lambda: self.copy(flow["user_code"])))
            actions.append(button("Cancel sign-in", "Stop checking and remove temporary sign-in data.",
                                  self.model.cancel_signin))
            self.signin_actions = actions
            self.signin.append(actions)

    def update_activity(self, _model, _property, cleanup):
        """Disable conflicting controls while an operation or sign-in is active.

        Args:
            _model: Configuration model emitting a state change.
            _property: Busy or active-flow property metadata.
            cleanup: Retry button that must wait for active work.
        """
        idle = not self.model.busy and self.model.flow is None
        for group in (self.account_actions, self.assistant_actions, self.diagnostic_actions, cleanup):
            group.set_sensitive(idle)
        if self.signin_actions is not None:
            self.signin_actions.set_sensitive(not self.model.busy)

    def close_requested(self, _window):
        """Wait for active work or cancel an open sign-in before closing setup.

        Args:
            _window: Window requesting closure.
        """
        if self.model.busy:
            self.model.notice = "Wait for the current operation to finish before closing AgentInbox."
            return True
        if self.model.flow:
            self.model.cancel_signin(done=self.close)
            return True
        return False

    def assistant_page(self):
        """Show the registration and separate Codex and MCP check results."""
        page = column()
        page.append(label("Connect your assistant", heading=True))
        page.append(label("Register AgentInbox once. Your assistant can then use all enabled accounts and selected folders in this profile."))
        self.assistant_actions = column()
        self.assistant_actions.append(button("Check Codex settings", "Inspect the user-level AgentInbox MCP entry.",
                                             self.model.check_codex))
        self.assistant_actions.append(button("Connect or update Codex", "Use the local Codex CLI to register this profile.",
                                             lambda: self.confirm("Register AgentInbox in Codex?",
                                                                  "This adds or replaces the AgentInbox entry in your user-level Codex settings.",
                                                                  "Register AgentInbox", self.model.connect_codex)))
        self.assistant_actions.append(button("Test local MCP startup", "Start the generated command and request tool definitions.",
                                             self.model.test_startup))
        page.append(self.assistant_actions)

        for field in ("codex-status", "startup-status"):
            status = label(selectable=True)
            self.model.bind_property(field, status, "label", SYNC)
            page.append(status)
        page.append(label("Local startup checks the AgentInbox launcher and its tool definitions. Open a new Codex chat to verify the connection there."))
        page.append(link("Codex MCP setup guide", "https://learn.chatgpt.com/docs/extend/mcp?surface=cli"))
        page.append(self.copy_block("Codex command", self.model.codex_command()))
        page.append(self.copy_block("MCP settings", json.dumps(self.model.connection, indent=2)))
        self.add_page(page, "assistant", "Connect Codex")

    def troubleshooting_page(self):
        """Offer safe Codex prompts and the existing local profile diagnostics."""
        page = column()
        page.append(label("Troubleshooting", heading=True))
        page.append(self.copy_block("Account access prompt",
                                    "@AgentInbox Which email accounts and selected folders can you access through AgentInbox?"))
        page.append(self.copy_block("Troubleshooting prompt",
                                    "@AgentInbox Check whether the local AgentInbox MCP connection is loaded and its tools are available. "
                                    "If available, list the shared accounts and selected folders. If it fails, report the exact safe error and the next check. "
                                    "Do not read or send email, or request any passwords or sign-in codes."))
        self.diagnostic_actions = column()
        self.diagnostic_actions.append(button("Check local setup", "Check local profile settings, TLS and keyring prerequisites.",
                                              self.model.check_profile))
        self.diagnostic_actions.append(button("Check keyring access", "Store, read and remove one disposable protected value.",
                                              lambda: self.model.check_profile(check_storage=True)))
        page.append(self.diagnostic_actions)
        result = label(selectable=True)
        self.model.bind_property("diagnosis", result, "label", SYNC)
        page.append(result)
        self.add_page(page, "troubleshooting", "Troubleshooting")

    def about_page(self):
        """Display product attribution and maintained documentation links."""
        page = column()
        page.append(label(f"AgentInbox {__version__}", heading=True))
        page.append(label("I’m Umberto Giacobbi. I built and maintain AgentInbox, with help from contributors."))
        page.append(link("AgentInbox source", REPOSITORY))
        page.append(link("Linux setup guide", f"{REPOSITORY}/blob/main/docs/LINUX_SETUP.md"))
        page.append(link("Privacy", f"{REPOSITORY}/blob/main/docs/PRIVACY.md"))
        page.append(link("Terms", f"{REPOSITORY}/blob/main/docs/TERMS.md"))
        self.add_page(page, "about", "About")

    def copy_block(self, title, text):
        """Show selectable plain text and an explicit clipboard action.

        Args:
            title: Name of the command, settings, or prompt.
            text: Exact text shown and copied on request.
        """
        block = column(5)
        block.append(label(title, heading=True))
        block.append(label(text, selectable=True))
        action = f"Copy {title.lower()}"
        block.append(button(action, f"Copy the {title.lower()} to the clipboard.", lambda: self.copy(text)))
        return block

    def copy(self, text):
        """Copy only text explicitly chosen by the user.

        Args:
            text: Configuration, prompt, or Microsoft code associated with the clicked action.
        """
        self.get_clipboard().set(text)
        self.model.notice = "Copied to clipboard."

    def confirm(self, title, details, action, accepted):
        """Ask before removing local access or editing Codex settings.

        Args:
            title: Short confirmation question.
            details: Exact scope of the change.
            action: Label for the button that commits the change.
            accepted: No-argument action called after confirmation.
        """
        dialog = Gtk.MessageDialog(transient_for=self, modal=True, text=title, secondary_text=details)
        describe(dialog.add_button("Cancel", Gtk.ResponseType.CANCEL), "Cancel", "Close this confirmation.")
        describe(dialog.add_button(action, Gtk.ResponseType.ACCEPT), action, details)
        dialog.set_default_response(Gtk.ResponseType.CANCEL)

        def respond(window, response):
            """Forward only the explicit affirmative response.

            Args:
                window: Owned native confirmation dialog.
                response: Native response ID.
            """
            window.destroy()
            if response == Gtk.ResponseType.ACCEPT:
                accepted()

        dialog.connect("response", respond)
        dialog.present()

    @staticmethod
    def clear(container):
        """Remove children before rebinding a GTK box.

        Args:
            container: Native box whose current children are replaced.
        """
        while child := container.get_first_child():
            container.remove(child)


def main(argv=None):
    """Start the Linux configuration application using the selected local profile.

    Args:
        argv: Optional explicit CLI arguments; None reads the current command line.
    """
    parser = argparse.ArgumentParser(description="Configure AgentInbox on Linux.")
    parser.add_argument("--version", action="version", version=f"AgentInbox {__version__}")
    parser.add_argument("--settings", type=Path, default=Path(__file__).resolve().parents[2]
                        / "src/MailMeUp.Desktop/appsettings.json", help="Shipped application settings file.")
    options = parser.parse_args(argv)
    if sys.platform != "linux":
        parser.error("The GTK configuration app requires Linux.")
    try:
        settings = json.loads(options.settings.read_text(encoding="utf-8"))
        client_id = str(UUID(settings["MicrosoftClientId"]))
    except (OSError, KeyError, TypeError, ValueError, AttributeError):
        parser.error("The shipped application settings are missing or have an invalid Microsoft public client ID.")
    try:
        model = Configuration(MailApplication(data_directory()), client_id)
    except MailError as error:
        parser.error(str(error))
    app = Gtk.Application(application_id="io.github.umbertotechnopreneur.AgentInbox",
                          flags=Gio.ApplicationFlags.NON_UNIQUE)

    def activate(application):
        """Present the configuration window for this application instance.

        Args:
            application: GTK application emitting activation.
        """
        window = application.get_active_window() or SetupWindow(application, model)
        window.present()

    app.connect("activate", activate)
    return app.run([])


if __name__ == "__main__":
    raise SystemExit(main())
