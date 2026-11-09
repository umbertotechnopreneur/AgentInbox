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
import os
from pathlib import Path
import sys
from uuid import UUID

import gi

gi.require_version("Gtk", "4.0")
from gi.repository import Gdk, Gio, GLib, GObject, Gtk, Pango

from agentinbox import __version__
from agentinbox.application import MailApplication
from agentinbox.config import data_directory
from agentinbox.errors import MailError
from .model import Configuration
from .tray import APP_ID, Tray
from agentinbox.terminal_setup import PROVIDER_HELP

SIGNIN_STYLE = b"""
.agentinbox-signin-background { filter: blur(6px); }
.agentinbox-signin-shade { background-color: rgba(0, 0, 0, 0.68); }
.agentinbox-signin-card {
    background-color: @theme_bg_color;
    color: @theme_fg_color;
    border: 1px solid alpha(@theme_fg_color, 0.20);
    border-radius: 16px;
    padding: 24px;
    box-shadow: 0 12px 36px rgba(0, 0, 0, 0.38);
}
.agentinbox-signin-code {
    background-color: alpha(@theme_fg_color, 0.08);
    border-radius: 8px;
    padding: 10px 20px;
    font-family: monospace;
    font-size: 36px;
    font-weight: 700;
    letter-spacing: 2px;
}
"""

SYNC = GObject.BindingFlags.SYNC_CREATE
TWO_WAY = SYNC | GObject.BindingFlags.BIDIRECTIONAL
SECURITY = {"tls": "TLS", "starttls": "STARTTLS"}
REPOSITORY = "https://github.com/umbertotechnopreneur/AgentInbox"
PROVIDER_LOGOS = {"custom": "custom", "gmail": "google", "icloud": "icloud",
                  "outlook": "outlook", "microsoft365": "microsoft"}
PROVIDER_ASSETS = Path(__file__).resolve().parents[1] / "providers"
if not PROVIDER_ASSETS.is_dir():
    PROVIDER_ASSETS = Path(__file__).resolve().parents[2] / "resources/providers"
ACCOUNT_PROMPT = "@AgentInbox Which email accounts and selected folders can you access through AgentInbox?"
TROUBLESHOOTING_PROMPT = (
    "@AgentInbox Check whether the local AgentInbox MCP connection is loaded and its tools are available. "
    "If available, list the shared accounts and selected folders. If it fails, report the exact safe error and the next check. "
    "Do not read or send email, or request any passwords or sign-in codes."
)


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
    widget = describe(Gtk.Button(label=text, halign=Gtk.Align.START), text, tooltip)
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


def provider_image_path(preset):
    """Resolve a local image for the model's provider ID.

    Args:
        preset: Stable provider ID from the shared catalog or saved account.
    """
    return str(PROVIDER_ASSETS / f"{PROVIDER_LOGOS.get(preset, 'custom')}.svg")


def choice(options, selected, name, change, *, compact=False, providers=None):
    """Render a native dropdown and forward the selected key.

    Args:
        options: Ordered mapping of internal keys to display labels.
        selected: Current key, or None to leave the dropdown unselected.
        name: Accessible name and tooltip.
        change: Callback accepting a selected key.
        compact: Ellipsize long names while retaining the full search text and tooltip.
        providers: Mapping from choice keys to provider IDs for the leading images.
    """
    keys = list(options)
    widget = describe(Gtk.DropDown.new_from_strings(list(options.values())), name)
    widget.set_selected(keys.index(selected) if selected in options else Gtk.INVALID_LIST_POSITION)
    if providers is not None:
        items = widget.get_model()
        images = {items.get_item(index): provider_image_path(providers[key])
                  for index, key in enumerate(keys) if key in providers}
        factory = Gtk.SignalListItemFactory()

        def setup(_factory, item):
            """Create a native row for a provider or account choice.

            Args:
                _factory: Native list-item factory emitting setup.
                item: Reusable dropdown item to equip with a row.
            """
            row = Gtk.Box(spacing=8)
            row.append(Gtk.Image(pixel_size=20))
            caption = Gtk.Label(xalign=0)
            if compact:
                caption.set_ellipsize(Pango.EllipsizeMode.END)
                caption.set_max_width_chars(36)
            row.append(caption)
            item.set_child(row)

        def bind(_factory, item):
            """Display the caption and image of the bound model item.

            Args:
                _factory: Native list-item factory emitting bind.
                item: Dropdown item associated with a provider or account.
            """
            entry = item.get_item()
            row = item.get_child()
            image = row.get_first_child()
            path = images.get(entry)
            image.set_from_file(path)
            image.set_visible(path is not None)
            caption = row.get_last_child()
            caption.set_label(entry.get_string())
            caption.set_tooltip_text(entry.get_string())

        factory.connect("setup", setup)
        factory.connect("bind", bind)
        widget.set_factory(factory)
        widget.set_list_factory(factory)

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
        super().__init__(application=app, title="AgentInbox", default_width=880, default_height=590)
        self.model = model
        self.tray = None
        self.about_window = None
        self.quitting = False
        self.fields = {}
        self.section_binding = None
        root = column(14)
        for side in ("top", "bottom", "start", "end"):
            root.set_property(f"margin-{side}", 18)
        self.settings_view = root
        style = Gtk.CssProvider()
        style.load_from_data(SIGNIN_STYLE)
        Gtk.StyleContext.add_provider_for_display(self.get_display(), style, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        overlay = Gtk.Overlay(child=root)
        self.signin = Gtk.Box(visible=False)
        self.signin.add_css_class("agentinbox-signin-shade")
        overlay.add_overlay(self.signin)
        self.set_child(overlay)
        keys = Gtk.EventControllerKey()
        keys.connect("key-pressed", self.signin_key)
        self.add_controller(keys)
        self.pages = Gtk.Stack(vexpand=True, vhomogeneous=False)
        nav = Gtk.StackSwitcher(stack=self.pages, halign=Gtk.Align.START, hexpand=True)
        navigation = Gtk.Box(spacing=8)
        navigation.append(nav)
        navigation.append(button("About", "Open information about AgentInbox in a separate window.",
                                 self.show_about))
        root.append(navigation)
        root.append(Gtk.Separator())
        root.append(self.pages)
        status = Gtk.Box(spacing=8)
        notice = label()
        notice.set_lines(2)
        notice.set_ellipsize(Pango.EllipsizeMode.END)
        notice.set_hexpand(True)
        model.bind_property("notice", notice, "label", SYNC)
        status.append(notice)
        details = button("Details", "Read the complete status message.",
                         lambda: self.show_text("Operation details", self.model.notice))
        status.append(details)
        root.append(status)
        cleanup = button("Retry protected-data cleanup", "Retry removing retired credentials from your keyring.",
                         model.retry_cleanup)
        model.bind_property("cleanup-pending", cleanup, "visible", SYNC)
        status.append(cleanup)
        stop = button("Stop after this account", "Finish the current connection check, then stop checking saved accounts.",
                      model.stop.set)
        model.bind_property("can-stop", stop, "visible", SYNC)
        status.append(stop)

        def update_notice(_model, _property):
            """Show the status row only while the model has something to display.

            Args:
                _model: Configuration model emitting the status change.
                _property: Metadata for the changed status property.
            """
            details.set_visible(bool(model.notice))
            status.set_visible(bool(model.notice or model.cleanup_pending or model.can_stop))

        for field in ("notice", "cleanup-pending", "can-stop"):
            model.connect(f"notify::{field}", update_notice)
        update_notice(model, None)

        self.accounts_page()
        self.assistant_page()
        self.troubleshooting_page()
        model.bind_property("page", self.pages, "visible-child-name", TWO_WAY)
        tab = nav.get_first_child()
        for tab_name in ("Accounts", "Connect Codex", "Troubleshooting"):
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
        """Add one settings page directly to the navigation stack.

        Args:
            content: Page's native vertical layout.
            name: Stable navigation ID.
            title: Visible navigation tab label.
        """
        self.pages.add_titled(content, name, title)

    def accounts_page(self):
        """Build saved-account controls and the draft editor slot."""
        page = column(12)
        self.account_actions = column(14)
        self.account_actions.set_vexpand(True)
        self.account_list = column(8)
        self.editor = column(12)
        self.editor.set_vexpand(True)
        self.account_actions.append(self.account_list)
        self.account_actions.append(self.editor)
        page.append(self.account_actions)
        self.add_page(page, "accounts", "Accounts")

    def render_accounts(self, *_change):
        """Render current saved snapshots and their session-only check marks.

        Args:
            *_change: Optional model notification arguments.
        """
        self.clear(self.account_list)
        accounts = self.model.accounts
        actions = Gtk.Box(spacing=8)
        if accounts:
            options = {"": "Select a saved account"}
            providers = {}
            for account in accounts:
                checked = " ✓" if account["id"] in self.model.checked else ""
                options[account["id"]] = f"{account['label']} · {account['email']}{checked}"
                providers[account["id"]] = account.get("preset", "custom")
            draft = self.model.draft
            selection = draft.identity if draft else ""
            picker = choice(options, selection, "Choose a saved account to edit",
                            self.model.select_account, compact=True, providers=providers)
            picker.set_enable_search(True)
            actions.append(picker)
        add = button("Add account", "Open a new connection form.", self.model.new_account)
        if not self.model.draft:
            add.add_css_class("suggested-action")
        actions.append(add)
        actions.append(button("Refresh", "Reload saved accounts and close the current editor.", self.model.reload))
        if accounts:
            actions.append(button("Test all", "Check saved connections and sign-in in order. No mail is read or sent.",
                                  self.model.check_all))
        self.account_list.append(actions)

        if not accounts and not self.model.draft:
            self.account_list.append(label("Connect your first mailbox", heading=True))
            self.account_list.append(label("Add Gmail, Microsoft, iCloud, GoDaddy or another compatible mailbox. "
                                           "Choose which folders to share through one MCP connection."))

    def render_editor(self, *_change):
        """Bind the active draft to the three native editor tabs.

        Args:
            *_change: Optional model notification arguments.
        """
        draft = self.model.draft
        self.render_accounts()
        if self.section_binding:
            self.section_binding.unbind()
            self.section_binding = None
        self.clear(self.editor)
        self.fields.clear()
        if not draft:
            return
        sections = Gtk.Stack(vexpand=True, vhomogeneous=False)
        tabs = Gtk.StackSwitcher(stack=sections)
        header = Gtk.Box(spacing=12)
        provider_name = self.model.presets[draft.preset]["label"]
        logo = Gtk.Image.new_from_file(provider_image_path(draft.preset))
        logo.set_pixel_size(28)
        header.append(describe(logo, provider_name))
        heading = label("Edit connection" if draft.identity else "New connection", heading=True)
        heading.set_hexpand(True)
        header.append(heading)
        header.append(tabs)
        self.editor.append(header)

        for name, title, content in (
            ("account", "Account", self.identity_fields(draft)),
            ("incoming", "Read mail", self.incoming_fields(draft)),
            ("smtp", "Send mail", self.sending_fields(draft)),
        ):
            content.set_valign(Gtk.Align.START)
            sections.add_titled(content, name, title)
        tab = tabs.get_first_child()
        for title in ("Account", "Read mail", "Send mail"):
            describe(tab, title, f"Open the {title} settings")
            tab = tab.get_next_sibling()
        self.section_binding = self.model.bind_property("editor-section", sections, "visible-child-name", TWO_WAY)
        self.editor.append(sections)
        self.editor.append(Gtk.Separator())

        actions = Gtk.Box(spacing=8)
        save = button("Save changes", "Save the current account and its sharing permissions.", self.model.save)
        save.add_css_class("suggested-action")
        actions.append(save)
        actions.append(button("Close editor", "Discard unsaved changes and close the account form.",
                              self.model.close_editor))
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

        provider = choice(providers, draft.preset, "Email provider", self.model.select_provider,
                          providers={key: key for key in providers})
        self.fields["preset"] = ("account", provider)
        grid = Gtk.Grid(column_spacing=12, row_spacing=8)
        grid.attach(Gtk.Label(label="Provider", xalign=0), 0, 0, 1, 1)
        grid.attach(provider, 1, 0, 1, 1)
        self.entry(grid, 1, "Email address", draft, "email", "email", "account", 254)
        self.entry(grid, 2, "Connection name", draft, "label", "label", "account", 100,
                   placeholder="Defaults to your email address")
        content.append(grid)

        preset = self.model.presets[draft.preset]
        if draft.microsoft and not preset.get("microsoft_tenant"):
            content.append(label("Sign in on Microsoft's website to authorize this mailbox."))
        elif preset.get("notice") and (draft.microsoft or not preset.get("microsoft_tenant")):
            content.append(label(preset["notice"]))
        else:
            content.append(label("Enter the mail settings from your provider. Use an app password if your provider requires one."))
        if draft.preset in PROVIDER_HELP:
            content.append(link(f"{preset['label']} sign-in help", PROVIDER_HELP[draft.preset]))

        actions = Gtk.Box(spacing=8)
        actions.append(toggle(draft, "read-enabled", "Allow reading",
                              "Let your assistant search and read in the folders you choose."))
        if draft.microsoft:
            actions.append(button("Sign in with Microsoft", "Save these settings and request a code for Microsoft's website.",
                                  lambda: self.model.save(sign_in=True)))
            actions.append(label("Sign-in saved" if draft.microsoft_connected else "Sign-in required"))
        content.append(actions)
        return content

    def incoming_fields(self, draft):
        """Build incoming settings, folder selection and reading checks.

        Args:
            draft: Current observable account editor.
        """
        content = column()
        content.append(self.endpoint_fields(draft.incoming, "incoming", draft))
        actions = Gtk.Box(spacing=8)
        actions.append(button("Check connection", "Check this draft and discover folders without saving or sending.",
                              self.model.check_draft))
        if draft.identity:
            actions.append(button("Check reading", "Use saved settings to check a bounded sample read.",
                                  self.model.check_read))
        if draft.protocol == "imap":
            folders = button("Choose folders", "Select the IMAP folders to share.", self.show_folders)
            self.fields["folders"] = ("incoming", folders)
            actions.append(folders)
        content.append(actions)
        if draft.protocol == "pop3":
            content.append(label("POP3 shares INBOX."))
        else:
            summary = label()
            self.model.bind_property("folder-summary", summary, "label", SYNC)
            content.append(summary)
            if draft.available_folders is None:
                content.append(label("Check the connection to discover folders on the server."))
        return content

    def show_folders(self):
        """Render folder choices in a dedicated dialog for the active draft."""
        draft = self.model.draft
        dialog, body = self.detail_window("Choose folders", 540)
        summary = label()
        self.model.bind_property("folder-summary", summary, "label", SYNC)
        body.append(summary)
        choices = column(4)

        def selected(widget, name):
            """Forward a checkbox gesture and reflect the model's accepted value.

            Args:
                widget: Native checkbox emitting the gesture.
                name: Exact engine folder name.
            """
            active = widget.get_active()
            accepted = self.model.choose_folder(name, active)
            if active != accepted:
                widget.set_active(accepted)

        for name in draft.folder_choices():
            missing = draft.available_folders is not None and name not in draft.available_folders
            caption = f"{name} (unavailable)" if missing else name
            check = describe(Gtk.CheckButton(active=name in draft.folders), caption,
                             f"Share the {caption} folder")
            check.set_child(label(caption))
            check.connect("toggled", selected, name)
            choices.append(check)
        body.append(Gtk.ScrolledWindow(hscrollbar_policy=Gtk.PolicyType.NEVER, max_content_height=300,
                                       propagate_natural_height=True, child=choices))
        body.append(button("Done", "Return to the account editor with the selected folders.", dialog.close))
        dialog.present()

    def sending_fields(self, draft):
        """Build optional SMTP configuration separately from permission to send.

        Args:
            draft: Current observable account editor.
        """
        content = column()
        configure = toggle(draft, "configure-smtp", "Configure a sending server",
                           "Add SMTP settings so you can check the sending connection.")
        self.fields["smtp.configured"] = ("smtp", configure)
        content.append(configure)
        settings = column()
        settings.append(self.endpoint_fields(draft.smtp, "smtp", draft))
        actions = Gtk.Box(spacing=8)
        actions.append(toggle(draft, "send-enabled", "Allow sending",
                              "Let your assistant send through MCP when you explicitly request it."))
        actions.append(button("Check sending connection", "Check SMTP sign-in without submitting email or saving changes.",
                              lambda: self.model.check_draft(smtp_only=True)))
        settings.append(actions)
        draft.bind_property("configure-smtp", settings, "visible", SYNC)
        content.append(settings)
        return content

    def endpoint_fields(self, endpoint, prefix, draft):
        """Bind a compact desktop grid to an incoming or outgoing endpoint.

        Args:
            endpoint: Observable endpoint draft.
            prefix: Engine field prefix and editor section, either incoming or smtp.
            draft: Active account draft supplying protocol and authentication.
        """
        grid = Gtk.Grid(column_spacing=12, row_spacing=8)
        self.entry(grid, 0, "Server", endpoint, "host", f"{prefix}.host", prefix, 255)
        self.entry(grid, 0, "Port", endpoint, "port", f"{prefix}.port", prefix, 5, column_index=2)
        security = choice(SECURITY, endpoint.tls, "Connection security",
                          lambda selected: endpoint.set_property("tls", selected))
        security.set_halign(Gtk.Align.START)
        self.fields[f"{prefix}.tls"] = (prefix, security)
        grid.attach(Gtk.Label(label="Connection security", xalign=0), 0, 1, 1, 1)
        grid.attach(security, 1, 1, 1, 1)
        if prefix == "incoming":
            protocols = {protocol: protocol.upper() for protocol in draft.protocols(self.model.presets)}
            protocol = choice(protocols, draft.protocol, "Incoming mail protocol", self.model.select_protocol)
            protocol.set_hexpand(False)
            self.fields["incoming.protocol"] = (prefix, protocol)
            grid.attach(Gtk.Label(label="Protocol", xalign=0), 2, 1, 1, 1)
            grid.attach(protocol, 3, 1, 1, 1)
        self.entry(grid, 2, "Sign-in username", endpoint, "username", f"{prefix}.username", prefix, 255,
                   placeholder="Defaults to your email address", span=3)
        if not draft.microsoft:
            self.entry(grid, 3, "Password or app password", endpoint, "password", f"passwords.{prefix}", prefix,
                       4096, password=True, span=3)
        return grid

    def entry(self, grid, row, text, model, field, engine_field, tab, maximum, *, placeholder="", password=False, column_index=0, span=1):
        """Attach a labeled, bounded entry bound to an observable field.

        Args:
            grid: Parent native form grid.
            row: Zero-based row in the grid.
            text: Label and accessible entry name.
            model: Observable account or endpoint draft.
            field: Model property bound to the entry text.
            engine_field: Validation field path reported by the engine.
            tab: Stable editor section name for the field.
            maximum: Maximum number of editable characters.
            placeholder: Optional native hint for a blank entry.
            password: Use the native masked entry when True.
            column_index: Zero-based grid column for the field label.
            span: Number of grid columns occupied by the entry.
        """
        if password:
            widget = Gtk.PasswordEntry(show_peek_icon=True, hexpand=True)
            widget.get_delegate().set_max_length(maximum)
        else:
            widget = Gtk.Entry(max_length=maximum, hexpand=True, placeholder_text=placeholder)
        describe(widget, text)
        model.bind_property(field, widget, "text", TWO_WAY)
        self.fields[engine_field] = (tab, widget)
        if column_index:
            widget.set_width_chars(maximum)
            widget.set_max_width_chars(maximum)
            widget.set_hexpand(False)
        grid.attach(Gtk.Label(label=text, xalign=0), column_index, row, 1, 1)
        grid.attach(widget, column_index + 1, row, span, 1)

    def focus_error(self, _model, _property):
        """Navigate to the field associated with the current safe engine error.

        Args:
            _model: Model that emitted the error field change.
            _property: Error field property metadata.
        """
        if target := self.fields.get(self.model.error_field):
            tab, widget = target
            self.model.page = "accounts"
            self.model.editor_section = tab
            widget.grab_focus()

    def render_signin(self, _model, _property):
        """Present the model's active device flow in a centered overlay.

        Args:
            _model: Model emitting the active-flow change.
            _property: Active-flow property metadata.
        """
        self.clear(self.signin)
        flow = self.model.flow
        self.signin.set_visible(flow is not None)
        self.settings_view.set_sensitive(flow is None)
        if flow is None:
            self.settings_view.remove_css_class("agentinbox-signin-background")
            return
        self.settings_view.add_css_class("agentinbox-signin-background")

        card = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14,
                       halign=Gtk.Align.CENTER, valign=Gtk.Align.CENTER, hexpand=True,
                       accessible_role=Gtk.AccessibleRole.DIALOG)
        card.add_css_class("agentinbox-signin-card")
        card.update_property([Gtk.AccessibleProperty.LABEL, Gtk.AccessibleProperty.MODAL],
                             ["Sign in with Microsoft", True])
        heading = Gtk.Label(label="Sign in with Microsoft")
        heading.add_css_class("title-1")
        card.append(heading)
        card.append(Gtk.Label(label="Copy the code, then open Microsoft to sign in."))
        code = Gtk.Label(label=flow["user_code"], selectable=True, halign=Gtk.Align.CENTER)
        code.add_css_class("agentinbox-signin-code")
        card.append(code)
        card.append(Gtk.Label(label="After signing in, return here. AgentInbox will finish automatically."))

        actions = Gtk.Box(spacing=8, halign=Gtk.Align.CENTER)
        actions.append(button("Copy code", "Copy the current Microsoft device code.",
                              lambda: self.copy(self.model.flow["user_code"])))
        open_signin = link("OK · Open Microsoft", flow["verification_uri"])
        open_signin.add_css_class("suggested-action")
        open_signin.set_tooltip_text("Open Microsoft's sign-in page in your browser.")
        actions.append(open_signin)
        actions.append(button("Cancel", "Cancel Microsoft sign-in and return to account settings.",
                              self.model.cancel_signin))
        self.model.bind_property("signin-cancelling", actions, "sensitive",
                                 SYNC | GObject.BindingFlags.INVERT_BOOLEAN)
        card.append(actions)

        status = Gtk.Label(wrap=True, justify=Gtk.Justification.CENTER, max_width_chars=58)
        status.set_lines(3)
        status.set_ellipsize(Pango.EllipsizeMode.END)
        status.add_css_class("dim-label")
        self.model.bind_property("notice", status, "label", SYNC)
        card.append(status)
        self.signin.append(card)
        open_signin.grab_focus()

    def signin_key(self, _controller, keyval, _keycode, _state):
        """Forward Escape to the active sign-in's cancel command.

        Args:
            _controller: GTK key controller for the setup window.
            keyval: Symbolic key value from the native event.
            _keycode: Hardware key code supplied by GTK.
            _state: Active keyboard modifiers.
        """
        if self.model.flow is not None and keyval == Gdk.KEY_Escape:
            self.model.cancel_signin()
            return True
        return False

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

    def close_requested(self, _window):
        """Wait for active work or cancel an open sign-in before closing setup.

        Args:
            _window: Window requesting closure.
        """
        if not self.quitting and self.tray and self.tray.available:
            self.set_visible(False)
            return True
        if self.model.busy:
            self.quitting = False
            self.present()
            self.model.notice = "Wait for the current operation to finish before closing AgentInbox."
            return True
        if self.model.flow:
            self.model.cancel_signin(done=self.close)
            return True
        return False

    def present_page(self, page):
        """Reopen the window at an explicitly requested setup page.

        Args:
            page: GTK stack page key associated with the tray item.
        """
        if self.model.flow is None:
            self.model.page = page
        self.present()

    def quit_from_tray(self):
        """Close setup explicitly, preserving an active save or sign-in cleanup."""
        self.quitting = True
        self.close()

    def assistant_page(self):
        """Show the registration and separate Codex and MCP check results."""
        page = column(12)
        page.append(label("Connect Codex", heading=True))
        page.append(label("One connection gives Codex access to the mailboxes and folders you choose."))
        self.assistant_actions = Gtk.Box(spacing=8)
        self.assistant_actions.append(button("Check Codex settings", "Inspect the user-level AgentInbox MCP entry.",
                                             self.model.check_codex))
        self.assistant_actions.append(button("Connect or update Codex", "Use the local Codex CLI to register this profile.",
                                             lambda: self.confirm("Register AgentInbox in Codex?",
                                                                  "This adds or replaces the AgentInbox entry in your user-level Codex settings.",
                                                                  "Register AgentInbox", self.model.connect_codex)))
        self.assistant_actions.append(button("Test local MCP startup", "Start the generated command and request tool definitions.",
                                             self.model.test_startup))
        page.append(self.assistant_actions)

        results = Gtk.Grid(column_spacing=12, row_spacing=6)
        for row, (caption, field) in enumerate((("Codex", "codex-status"), ("Local MCP", "startup-status"))):
            status = label(selectable=True)
            self.model.bind_property(field, status, "label", SYNC)
            results.attach(Gtk.Label(label=caption, xalign=0), 0, row, 1, 1)
            results.attach(status, 1, row, 1, 1)
        page.append(results)
        page.append(label("After connecting, open a new Codex chat to check access."))
        page.append(Gtk.Separator())
        page.append(label("Manual setup", heading=True))
        actions = Gtk.Box(spacing=8)
        actions.append(button("Copy Codex command", "Copy the command for the selected profile.",
                              lambda: self.copy(self.model.codex_command())))
        actions.append(button("Copy MCP settings", "Copy the MCP configuration for the selected profile.",
                              lambda: self.copy(json.dumps(self.model.connection, indent=2))))
        actions.append(button("View details", "Read the command and complete MCP configuration.",
                              lambda: self.show_text("Connection details", self.model.codex_command() + "\n\n"
                                                     + json.dumps(self.model.connection, indent=2), monospace=True)))
        actions.append(link("Setup guide", "https://learn.chatgpt.com/docs/extend/mcp?surface=cli"))
        page.append(actions)
        self.add_page(page, "assistant", "Connect Codex")

    def troubleshooting_page(self):
        """Offer safe Codex prompts and the existing local profile diagnostics."""
        page = column(12)
        page.append(label("Troubleshooting", heading=True))
        page.append(label("Check the local profile, TLS and access to your keyring."))
        self.diagnostic_actions = Gtk.Box(spacing=8)
        self.diagnostic_actions.append(button("Check local setup", "Check local profile settings, TLS and keyring prerequisites.",
                                              self.model.check_profile))
        self.diagnostic_actions.append(button("Check keyring access", "Store, read and remove one disposable protected value.",
                                              lambda: self.model.check_profile(check_storage=True)))
        result = button("View diagnostic details", "Read the complete local diagnostic result.",
                        lambda: self.show_text("Diagnostic details", self.model.diagnosis, monospace=True))
        self.diagnostic_actions.append(result)

        def update_result(model, _property):
            """Enable the detail action when the model has diagnostic output.

            Args:
                model: Configuration model emitting a diagnostic result.
                _property: Metadata for the changed diagnosis property.
            """
            result.set_sensitive(bool(model.diagnosis))

        self.model.connect("notify::diagnosis", update_result)
        result.set_sensitive(bool(self.model.diagnosis))
        page.append(self.diagnostic_actions)
        page.append(Gtk.Separator())
        page.append(label("Ask Codex", heading=True))
        page.append(label("Copy a prompt to check which mailboxes Codex can access or investigate a connection problem."))
        actions = Gtk.Box(spacing=8)
        actions.append(button("Copy access prompt", "Copy the prompt that lists the accessible mailboxes.",
                              lambda: self.copy(ACCOUNT_PROMPT)))
        actions.append(button("Copy troubleshooting prompt", "Copy the prompt that checks the MCP connection.",
                              lambda: self.copy(TROUBLESHOOTING_PROMPT)))
        actions.append(button("View prompts", "Read both prompts before copying.",
                              lambda: self.show_text("Codex prompts", ACCOUNT_PROMPT + "\n\n" + TROUBLESHOOTING_PROMPT)))
        page.append(actions)
        self.add_page(page, "troubleshooting", "Troubleshooting")

    def show_about(self):
        """Open or focus the dedicated About window from setup or the tray."""
        if self.model.flow is not None:
            self.present()
            return
        if self.about_window is None:
            window = Gtk.Window(title="About AgentInbox", transient_for=self,
                                destroy_with_parent=True, hide_on_close=True, resizable=False,
                                default_width=560, default_height=450)
            body = column(10)
            for side in ("top", "bottom", "start", "end"):
                body.set_property(f"margin-{side}", 24)

            package = Path(__file__).resolve().parents[1]
            resources = package.parent / "resources"
            theme = Gtk.IconTheme.get_for_display(self.get_display())
            logo = (Gtk.Image.new_from_icon_name(APP_ID) if theme.has_icon(APP_ID)
                    else Gtk.Image.new_from_file(str(resources / "agentinbox-logo-512-safe.png")))
            logo.set_pixel_size(72)
            body.append(logo)
            title = Gtk.Label(label="AgentInbox")
            title.add_css_class("title-1")
            body.append(title)
            version = Gtk.Label(label=f"Version {__version__}")
            version.add_css_class("dim-label")
            body.append(version)
            description = Gtk.Label(
                label="I built AgentInbox so you can connect your mailboxes and share selected folders "
                      "with your AI assistant through one MCP connection.",
                wrap=True, justify=Gtk.Justification.CENTER)
            body.append(description)
            body.append(Gtk.Label(label="I’m Umberto Giacobbi. I maintain AgentInbox with help from contributors."))
            body.append(Gtk.Separator())

            mark_path = package / "vibeware/vibeware-logo.png"
            if not mark_path.is_file():
                mark_path = resources / "vibeware/vibeware-logo.png"
            mark = Gtk.Image.new_from_file(str(mark_path))
            mark.set_pixel_size(68)
            initiative = link("VibeWare", "https://umbertogiacobbi.biz/vibeware/manifesto")
            initiative.set_child(mark)
            initiative.set_tooltip_text("VibeWare · Software built with AI under human direction and responsibility")
            initiative.set_halign(Gtk.Align.CENTER)
            body.append(initiative)

            links = Gtk.Box(spacing=8, halign=Gtk.Align.CENTER)
            links.append(link("Website", "https://umbertogiacobbi.biz"))
            links.append(link("Source code", REPOSITORY))
            links.append(link("Linux setup", f"{REPOSITORY}/blob/main/docs/LINUX_SETUP.md"))
            links.append(link("Terms", f"{REPOSITORY}/blob/main/docs/TERMS.md"))
            links.append(link("Privacy", f"{REPOSITORY}/blob/main/docs/PRIVACY.md"))
            body.append(links)
            window.set_child(body)
            self.about_window = window
        self.about_window.present()

    def detail_window(self, title, width):
        """Create an owned dialog for optional details.

        Args:
            title: Window title describing the detail or selection.
            width: Initial width in GTK logical pixels.
        """
        window = Gtk.Window(title=title, transient_for=self, destroy_with_parent=True,
                            modal=True, default_width=width)
        body = column(10)
        for side in ("top", "bottom", "start", "end"):
            body.set_property(f"margin-{side}", 16)
        window.set_child(body)
        return window, body

    def show_text(self, title, text, *, monospace=False):
        """Display a selectable snapshot in its own detail window.

        Args:
            title: Window title for the optional details.
            text: Exact plain text displayed to the user.
            monospace: Whether to use fixed-width text for technical output.
        """
        window, body = self.detail_window(title, 660)
        viewer = Gtk.TextView(editable=False, cursor_visible=False, monospace=monospace,
                              wrap_mode=Gtk.WrapMode.WORD_CHAR)
        viewer.get_buffer().set_text(text)
        body.append(Gtk.ScrolledWindow(hscrollbar_policy=Gtk.PolicyType.NEVER, child=viewer,
                                       min_content_height=160, max_content_height=360,
                                       propagate_natural_height=True, vexpand=True))
        body.append(button("Close", "Close the details and return to settings.", window.close))
        window.present()

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
    # A custom profile can run independently of the normal desktop instance.
    flags = Gio.ApplicationFlags.NON_UNIQUE if os.environ.get("AGENTINBOX_DATA_DIR") else Gio.ApplicationFlags(0)
    app = Gtk.Application(application_id=APP_ID, flags=flags)

    def activate(application):
        """Present the configuration window for this application instance.

        Args:
            application: GTK application emitting activation.
        """
        existing = next((window for window in application.get_windows() if isinstance(window, SetupWindow)), None)
        if existing is not None:
            existing.present()
            return
        window = SetupWindow(application, model)
        theme = Gtk.IconTheme.get_for_display(window.get_display())
        icon = APP_ID if theme.has_icon(APP_ID) else "mail-unread"
        window.tray = Tray(application, model, icon, {
            "Open AgentInbox": window.present,
            "Accounts": lambda: window.present_page("accounts"),
            "Connect Codex": lambda: window.present_page("assistant"),
            "Troubleshooting": lambda: window.present_page("troubleshooting"),
            "About": window.show_about,
            "Exit AgentInbox": window.quit_from_tray,
        })
        model.refresh_activity()
        poll = GLib.timeout_add_seconds(5, model.refresh_activity)

        def shutdown(_application):
            """Release the poll and native registration when the desktop app exits.

            Args:
                _application: GTK application finishing its shutdown.
            """
            GLib.source_remove(poll)
            window.tray.close()

        application.connect("shutdown", shutdown)
        window.present()

    show = Gio.SimpleAction.new("open", None)
    show.connect("activate", lambda *_args: app.activate())
    app.add_action(show)
    app.connect("activate", activate)
    return app.run([])


if __name__ == "__main__":
    raise SystemExit(main())
