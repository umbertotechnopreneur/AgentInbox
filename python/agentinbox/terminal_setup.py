# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Guided terminal drafts, local prerequisites and stdio MCP launch settings.
# Modified with AI: OpenAI Codex; implemented Linux terminal setup, 2026-11-02.
# Human guidance: Umberto Giacobbi; complete and test Linux CLI and MCP support.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Terminal-only setup helpers; account policy remains in MailApplication."""

import ctypes.util
import os
import secrets
import ssl
import sys
import uuid
from pathlib import Path

from .config import account_input, address, authentication, endpoint, text
from .errors import MailError

PROVIDER_HELP = {
    "gmail": "https://support.google.com/accounts/answer/185833",
    "icloud": "https://support.apple.com/en-us/102654",
    "outlook": "https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth",
    "microsoft365": "https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth",
}


def _ask(label, default="", choices=()):
    """Read one bounded terminal answer, sending prompts only to stderr.

    Args:
        label: Non-secret question shown to the person configuring the account.
        default: Value selected by an empty answer; empty means an answer is required.
        choices: Exact accepted answers, or an empty tuple for free text.
    """
    suffix = f" [{default}]" if default else ""
    while True:
        sys.stderr.write(f"{label}{suffix}: ")
        sys.stderr.flush()
        value = sys.stdin.readline(4098)
        if not value:
            raise MailError("setup_cancelled", "Input ended. No account was saved.")
        if len(value) > 4096 or not value.endswith("\n"):
            raise MailError("invalid_configuration", "Keep each setup answer within 4096 characters on one line.")
        answer = value.strip() or default
        if answer and (not choices or answer in choices):
            return answer
        sys.stderr.write("Choose " + ", ".join(choices) + ".\n" if choices else "Enter a value.\n")


def _server(protocol, username):
    """Collect and validate one custom TLS endpoint without requesting a secret.

    Args:
        protocol: imap, pop3 or smtp, selecting default ports and endpoint validation.
        username: Mailbox address used as the default server username.
    """
    host = _ask(f"{protocol.upper()} server")
    security = _ask("Connection security (tls/starttls)", "tls", ("tls", "starttls"))
    ports = {"imap": (993, 143), "pop3": (995, 110), "smtp": (465, 587)}
    port = _ask("Port", str(ports[protocol][security == "starttls"]))
    if not port.isascii() or not port.isdecimal() or len(port) > 5:
        raise MailError("invalid_configuration", "Port must be a whole number from 1 to 65535. No account was saved.")
    return endpoint({"host": host, "port": int(port), "tls": security,
                     "username": _ask("Server username", username)}, protocol)


def account_draft(application, client_id=None):
    """Create a validated draft interactively, leaving credentials and saving to the caller.

    Args:
        application: Shared application exposing the same provider catalog as Windows.
        client_id: Public Microsoft registration GUID, required only for Microsoft presets.
    """
    if not sys.stdin.isatty():
        raise MailError("terminal_required", "Open an interactive terminal for guided setup, or supply --file and --entry.")
    catalog = application.manage("presets", {})
    sys.stderr.write("Connect an existing mailbox. Press Ctrl+C to cancel.\n"
                     "Providers: gmail, icloud, outlook, microsoft365, custom (including GoDaddy).\n")
    provider = _ask("Provider", "gmail", tuple(catalog))
    preset = catalog[provider]
    if preset.get("notice"):
        sys.stderr.write(preset["notice"] + "\n")
    if provider in PROVIDER_HELP:
        sys.stderr.write("Provider help: " + PROVIDER_HELP[provider] + "\n")
    auth = {"method": "password"}
    if provider in {"outlook", "microsoft365"}:
        if not client_id:
            raise MailError("microsoft_configuration", "Guided Microsoft setup requires --client-id PUBLIC_GUID from your app registration. See docs/CLI_SETUP_GUIDE.md#supply-a-public-client-registration.")
        auth = authentication({"method": "microsoft_oauth", "client_id": client_id,
                               "tenant": "consumers" if provider == "outlook" else "organizations"})
    elif client_id:
        raise MailError("invalid_arguments", "--client-id applies only to Outlook.com or Microsoft 365.")
    mailbox = address(_ask("Existing email address"))
    label = text(_ask("Connection name", mailbox), "Connection name", 100)
    protocols = tuple(preset["incoming"]) or ("imap", "pop3")
    protocol = _ask("Read mail using " + "/".join(protocols), "imap", protocols)
    incoming = dict(preset["incoming"][protocol], username=mailbox) if preset["incoming"] else _server(protocol, mailbox)
    folders = ["INBOX"]
    if protocol == "imap":
        folders = [item.strip() for item in _ask("Selected folders, separated by commas (up to 20)", "INBOX").split(",")]
    send = _ask("Allow your AI agent to send when you request it? (yes/no)", "no", ("yes", "no")) == "yes"
    smtp = None
    if send:
        smtp = dict(preset["smtp"], username=mailbox) if preset["smtp"] else _server("smtp", mailbox)
    draft = account_input({"id": "", "preset": provider, "label": label, "email": mailbox,
                           "authentication": auth, "incoming": {"protocol": protocol, **incoming},
                           "smtp": smtp, "read_enabled": True, "send_enabled": send, "folders": folders})
    sys.stderr.write(f"Ready to save {label}:\n"
                     f"  Email: {mailbox}\n"
                     f"  Provider: {preset['label']}\n"
                     f"  Incoming: {incoming['host']}:{incoming['port']} ({incoming['tls']})\n"
                     f"  Folders: {', '.join(folders)}\n"
                     f"  Reading: yes. Sending: {'yes' if send else 'no'}.\n")
    if smtp:
        sys.stderr.write(f"  SMTP: {smtp['host']}:{smtp['port']} ({smtp['tls']})\n")
    if _ask("Save this connection? (yes/no)", "yes", ("yes", "no")) != "yes":
        raise MailError("setup_cancelled", "Setup cancelled. No account was saved.")
    return draft


def mcp_configuration(root):
    """Build a stdio registration without editing the client's configuration.

    Args:
        root: Absolute selected profile directory, shared with local setup commands.
    """
    # Keep a virtual environment's interpreter symlink rather than its base executable.
    interpreter = os.path.abspath(sys.executable)
    source = Path(__file__).resolve().parents[1] / "run.py"
    if not source.is_file():
        raise MailError("source_entry_unavailable", "The source launcher python/run.py is missing. Restore the complete AgentInbox source directory.")
    arguments = ["-B", str(source), "mcp"]
    return {"mcpServers": {"agentinbox": {"command": interpreter, "args": arguments,
                                          "env": {"AGENTINBOX_DATA_DIR": str(root)}}}}


def _storage_probe(store):
    """Round-trip a unique disposable value and durably track any failed cleanup.

    Args:
        store: Selected profile's shared store, owning protected values and cleanup policy.
    """
    key, value = "signin-" + uuid.uuid4().hex, secrets.token_urlsafe(32)
    cleanup_pending = False
    try:
        store.vault.put(key, value)
        if store.vault.get(key) != value:
            raise MailError("secure_storage_unavailable", "Protected storage did not return the disposable value unchanged.")
    finally:
        try:
            # A diagnostic owns only its random value, never the shared cleanup queue.
            store.vault.delete(key)
        except Exception:
            try:
                store.queue_protected_cleanup([key])
            except Exception:
                raise MailError("cleanup_unrecorded", "The disposable value could not be removed or queued. Preserve this reference for local cleanup: " + key) from None
            cleanup_pending = True
    if cleanup_pending:
        raise MailError("cleanup_pending", "The storage probe needs cleanup. Run profile cleanup; no account was changed.")


def diagnose(application, check_storage=False):
    """Report local prerequisites separately from optional native storage verification.

    Args:
        application: Shared application bound to the selected profile; mail is never accessed.
        check_storage: Whether to round-trip and remove one random protected value.
    """
    profile = application.manage("profile_info", {})
    checks = [{"name": "python", "status": "ok", "message": sys.version.split()[0]},
              {"name": "profile", "status": "ok", "message": str(application.store.root)}]
    try:
        application.store.accounts()
        if profile["cleanup_pending"]:
            raise MailError("cleanup_pending", "Run profile cleanup to retry pending protected-data cleanup.")
    except MailError as error:
        checks.append({"name": "settings", "status": "error", "message": str(error)})
    try:
        ssl.create_default_context()
        checks.append({"name": "tls", "status": "ok", "message": "System certificate validation is available."})
    except (OSError, ssl.SSLError):
        checks.append({"name": "tls", "status": "error", "message": "System certificate trust is unavailable."})
    if sys.platform == "linux":
        libraries = all(ctypes.util.find_library(name) for name in ("secret-1", "glib-2.0", "gio-2.0", "gobject-2.0"))
        session = bool(os.environ.get("DBUS_SESSION_BUS_ADDRESS"))
        checks.append({"name": "secret_service", "status": "ok" if libraries and session else "error",
                       "message": "Libraries and session bus found; keyring access is not yet verified." if libraries and session
                       else "Linux needs libsecret and an unlocked Secret Service keyring in the MCP user's D-Bus session."})
    if check_storage:
        try:
            _storage_probe(application.store)
            checks.append({"name": "protected_storage", "status": "ok", "message": "Disposable value stored, read and removed."})
        except MailError as error:
            checks.append({"name": "protected_storage", "status": "error", "message": str(error), "code": error.code})
    else:
        checks.append({"name": "protected_storage", "status": "skipped", "message": "Run doctor --check-storage to verify native storage."})
    cleanup = application.store.cleanup_status()
    if any(check.get("code") == "cleanup_unrecorded" for check in checks):
        cleanup = {"cleanup_pending": True, "cleanup_pending_count": max(1, cleanup["cleanup_pending_count"]),
                   "cleanup_error": "cleanup_unrecorded"}
    return {"ready": all(check["status"] != "error" for check in checks), "checks": checks, **cleanup}
