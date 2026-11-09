# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Linux tray registration, desktop notifications and menu dispatch.
# Created with AI: OpenAI Codex, 2026-11-10.
# Human guidance: Umberto Giacobbi; match the Windows tray menu.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Publish a StatusNotifierItem and menu through the user's desktop bus."""

from gi.repository import Gio, GLib

APP_ID = "io.github.umbertotechnopreneur.AgentInbox"
WATCHER = "org.kde.StatusNotifierWatcher"
ITEM_PATH = "/StatusNotifierItem"
MENU_PATH = "/AgentInboxMenu"
MENU = "com.canonical.dbusmenu"

ITEM_XML = """
<node><interface name="org.kde.StatusNotifierItem">
  <property name="Category" type="s" access="read"/>
  <property name="Id" type="s" access="read"/>
  <property name="Title" type="s" access="read"/>
  <property name="Status" type="s" access="read"/>
  <property name="IconName" type="s" access="read"/>
  <property name="Menu" type="o" access="read"/>
  <property name="ItemIsMenu" type="b" access="read"/>
  <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
  <method name="Activate"><arg type="i"/><arg type="i"/></method>
  <method name="SecondaryActivate"><arg type="i"/><arg type="i"/></method>
  <method name="ContextMenu"><arg type="i"/><arg type="i"/></method>
  <signal name="NewToolTip"/>
</interface></node>
"""
MENU_XML = """
<node><interface name="com.canonical.dbusmenu">
  <property name="Version" type="u" access="read"/>
  <property name="TextDirection" type="s" access="read"/>
  <property name="Status" type="s" access="read"/>
  <property name="IconThemePath" type="as" access="read"/>
  <method name="GetLayout"><arg type="i"/><arg type="i"/><arg type="as"/>
    <arg type="u" direction="out"/><arg type="(ia{sv}av)" direction="out"/>
  </method>
  <method name="GetGroupProperties"><arg type="ai"/><arg type="as"/>
    <arg type="a(ia{sv})" direction="out"/>
  </method>
  <method name="GetProperty"><arg type="i"/><arg type="s"/><arg type="v" direction="out"/></method>
  <method name="Event"><arg type="i"/><arg type="s"/><arg type="v"/><arg type="u"/></method>
  <method name="EventGroup"><arg type="a(isvu)"/><arg type="ai" direction="out"/></method>
  <method name="AboutToShow"><arg type="i"/><arg type="b" direction="out"/></method>
  <method name="AboutToShowGroup"><arg type="ai"/><arg type="ai" direction="out"/><arg type="ai" direction="out"/></method>
  <signal name="LayoutUpdated"><arg type="u"/><arg type="i"/></signal>
</interface></node>
"""


class Tray:
    """Expose the setup actions and connection status through the desktop shell."""

    def __init__(self, app, model, icon_name, actions):
        """Watch for a tray host and the configuration model's activity snapshots.

        Args:
            app: GTK application sending native desktop notifications.
            model: Configuration model owning status polling.
            icon_name: Theme icon name, with the installed AgentInbox icon preferred.
            actions: Ordered mapping of native menu labels to no-argument callbacks.
        """
        self.app = app
        self.model = model
        self.icon = icon_name
        self.actions = tuple(actions.items())
        self.available = False
        self.owner = None
        self.connection = None
        self.exports = []
        self.revision = 1
        self.status = "MCP status unavailable"
        self.previous = None
        self.signal = model.connect("notify::activity", self._activity_changed)
        self.watch = Gio.bus_watch_name(Gio.BusType.SESSION, WATCHER, Gio.BusNameWatcherFlags.NONE,
                                       self._watcher_appeared, self._watcher_vanished)

    def close(self):
        """Stop watching the bus and release this item's D-Bus objects."""
        self.model.disconnect(self.signal)
        Gio.bus_unwatch_name(self.watch)
        if self.connection:
            for registration in self.exports:
                self.connection.unregister_object(registration)
        self.exports.clear()

    def _watcher_appeared(self, connection, _name, owner):
        """Export the item and register it with the current tray host.

        Args:
            connection: Shared desktop session bus connection.
            _name: Well-known tray watcher name.
            owner: Unique bus name of this watcher instance.
        """
        if self.connection != connection:
            self.connection = connection
            self.exports = [
                connection.register_object(ITEM_PATH, Gio.DBusNodeInfo.new_for_xml(ITEM_XML).interfaces[0],
                                           self._item_method, self._item_property, None),
                connection.register_object(MENU_PATH, Gio.DBusNodeInfo.new_for_xml(MENU_XML).interfaces[0],
                                           self._menu_method, self._menu_property, None),
            ]
        self.owner = owner
        connection.call(owner, "/StatusNotifierWatcher", WATCHER, "RegisterStatusNotifierItem",
                        GLib.Variant("(s)", (ITEM_PATH,)), None, Gio.DBusCallFlags.NONE,
                        3000, None, self._registered, owner)

    def _watcher_vanished(self, _connection, _name):
        """Allow ordinary window closing while the desktop has no tray host.

        Args:
            _connection: Session bus connection, if one could be opened.
            _name: Well-known watcher name that lost its owner.
        """
        self.owner = None
        self.available = False

    def _registered(self, connection, result, owner):
        """Mark the current watcher ready only after it acknowledges the item.

        Args:
            connection: Session bus used for registration.
            result: Asynchronous D-Bus response.
            owner: Unique watcher name used for this registration.
        """
        try:
            connection.call_finish(result)
        except GLib.Error:
            return
        self.available = self.owner == owner

    def _item_property(self, _bus, _sender, _path, _interface, name):
        """Return one StatusNotifierItem property requested by the shell.

        Args:
            _bus: Session bus connection.
            _sender: Tray host's unique bus name.
            _path: Exported item object path.
            _interface: StatusNotifierItem interface name.
            name: Requested property key.
        """
        values = {
            "Category": ("s", "Communications"), "Id": ("s", "agentinbox"),
            "Title": ("s", "AgentInbox"), "Status": ("s", "Active"),
            "IconName": ("s", self.icon), "Menu": ("o", MENU_PATH),
            "ItemIsMenu": ("b", False), "ToolTip": ("(sa(iiay)ss)", (self.icon, [], "AgentInbox", self.status)),
        }
        return GLib.Variant(*values[name])

    def _item_method(self, _bus, _sender, _path, _interface, method, _parameters, invocation):
        """Forward tray activation to the same action as the first menu entry.

        Args:
            _bus: Session bus connection.
            _sender: Tray host's unique bus name.
            _path: Exported item object path.
            _interface: StatusNotifierItem interface name.
            method: Native activate or context-menu request.
            _parameters: Cursor coordinates supplied by the shell.
            invocation: Pending native D-Bus method call.
        """
        invocation.return_value(None)
        if method in {"Activate", "SecondaryActivate"}:
            self.actions[0][1]()

    @staticmethod
    def _menu_property(_bus, _sender, _path, _interface, name):
        """Return fixed metadata about the exported desktop menu.

        Args:
            _bus: Session bus connection.
            _sender: Tray host's unique bus name.
            _path: Exported menu object path.
            _interface: D-Bus menu interface name.
            name: Requested menu property key.
        """
        values = {"Version": ("u", 3), "TextDirection": ("s", "ltr"),
                  "Status": ("s", "normal"), "IconThemePath": ("as", [])}
        return GLib.Variant(*values[name])

    def _items(self):
        """Describe the flat menu, including an insensitive status row."""
        items = {
            0: {"children-display": GLib.Variant("s", "submenu")},
            1: {"label": GLib.Variant("s", self.status), "enabled": GLib.Variant("b", False)},
        }
        for index, (name, _) in enumerate(self.actions, start=2):
            items[index] = {"label": GLib.Variant("s", name)}
        return items

    def _menu_method(self, _bus, _sender, _path, _interface, method, parameters, invocation):
        """Serve the flat D-Bus menu and forward clicks on known action IDs.

        Args:
            _bus: Session bus connection.
            _sender: Tray host's unique bus name.
            _path: Exported menu object path.
            _interface: D-Bus menu interface name.
            method: Menu layout, property or event operation.
            parameters: Typed parameters supplied by the desktop shell.
            invocation: Pending native D-Bus method call.
        """
        args = parameters.unpack()
        items = self._items()
        if method == "GetLayout":
            parent, depth, _names = args
            children = [] if parent or depth == 0 else [
                GLib.Variant("(ia{sv}av)", (index, props, []))
                for index, props in items.items() if index
            ]
            response = GLib.Variant("(u(ia{sv}av))", (self.revision, (parent, items.get(parent, {}), children)))
        elif method == "GetGroupProperties":
            response = GLib.Variant("(a(ia{sv}))", ([(index, items[index]) for index in args[0] if index in items],))
        elif method == "GetProperty":
            value = items.get(args[0], {}).get(args[1])
            if value is None:
                invocation.return_dbus_error("com.canonical.dbusmenu.UnknownProperty", "Unknown menu property")
                return
            response = GLib.Variant("(v)", (value,))
        elif method in {"Event", "EventGroup"}:
            events = [args] if method == "Event" else args[0]
            if method == "EventGroup":
                invocation.return_value(GLib.Variant("(ai)", ([],)))
            else:
                invocation.return_value(None)
            for index, event, _data, _time in events:
                if event == "clicked" and 2 <= index < len(self.actions) + 2:
                    self.actions[index - 2][1]()
            return
        elif method == "AboutToShow":
            response = GLib.Variant("(b)", (False,))
        else:
            response = GLib.Variant("(aiai)", ([], []))
        invocation.return_value(response)

    def _activity_changed(self, model, _property):
        """Refresh the menu and notify on observed connection or mailbox changes.

        Args:
            model: Observable configuration containing the latest status.
            _property: Metadata for the changed activity property.
        """
        status = model.activity
        if status is None:
            self._update_status("MCP status unavailable")
            return
        connected, has_mailboxes = status
        previous_connected, previous_mailboxes = self.previous or (False, True)
        messages = []
        connection = "MCP connected" if connected else "MCP disconnected"
        if connected != previous_connected:
            messages.append(connection)
        if not has_mailboxes and previous_mailboxes:
            messages.append("No mailboxes configured")
        self.previous = status
        self._update_status(connection if has_mailboxes else connection + " · No mailboxes configured")
        if messages:
            notification = Gio.Notification.new("AgentInbox")
            notification.set_body("\n".join(messages))
            notification.set_default_action("app.open")
            self.app.send_notification("activity", notification)

    def _update_status(self, text):
        """Publish changed tooltip and menu labels to the desktop host.

        Args:
            text: Safe summary with no account names or mail content.
        """
        if text == self.status:
            return
        self.status = text
        if self.connection:
            self.revision += 1
            self.connection.emit_signal(None, ITEM_PATH, "org.kde.StatusNotifierItem", "NewToolTip", None)
            self.connection.emit_signal(None, MENU_PATH, MENU, "LayoutUpdated",
                                        GLib.Variant("(ui)", (self.revision, 0)))
