"""Strict configuration for mail protocols and explicitly selected authentication."""

import os
import ctypes
import re
import sys
import uuid
from pathlib import Path

from .errors import MailError

SCHEMA_VERSION = 3
MAX_MESSAGE_BYTES = 10 * 1024 * 1024
MAX_ATTACHMENT_BYTES = 5 * 1024 * 1024
MAX_BODY_CHARS = 100_000
MAX_RECIPIENTS = 25
PRESETS = {
    "custom": {"label": "Custom mail server", "incoming": {}, "smtp": None,
               "authentication_methods": ["password"]},
    "gmail": {"label": "Gmail", "incoming": {
                  "imap": {"host": "imap.gmail.com", "port": 993, "tls": "tls"},
                  "pop3": {"host": "pop.gmail.com", "port": 995, "tls": "tls"}},
              "smtp": {"host": "smtp.gmail.com", "port": 465, "tls": "tls"},
              "authentication_methods": ["password"],
              "notice": "Use an app password. Availability depends on your Google account settings."},
    "icloud": {"label": "iCloud", "incoming": {
                   "imap": {"host": "imap.mail.me.com", "port": 993, "tls": "tls"}},
               "smtp": {"host": "smtp.mail.me.com", "port": 587, "tls": "starttls"},
               "authentication_methods": ["password"],
               "notice": "Use an app-specific password. SMTP uses your full email address."},
    "outlook": {"label": "Outlook.com", "incoming": {
                    "imap": {"host": "outlook.office365.com", "port": 993, "tls": "tls"},
                    "pop3": {"host": "outlook.office365.com", "port": 995, "tls": "tls"}},
                "smtp": {"host": "smtp-mail.outlook.com", "port": 587, "tls": "starttls"},
                "authentication_methods": ["password", "microsoft_oauth"],
                "notice": "Outlook.com requires Microsoft web authorization. Select it yourself when setting up this account; AgentInbox will not switch automatically."},
    "microsoft365": {"label": "Microsoft 365", "incoming": {
                         "imap": {"host": "outlook.office365.com", "port": 993, "tls": "tls"},
                         "pop3": {"host": "outlook.office365.com", "port": 995, "tls": "tls"}},
                     "smtp": {"host": "smtp.office365.com", "port": 587, "tls": "starttls"},
                     "authentication_methods": ["password", "microsoft_oauth"],
                     "notice": "Microsoft 365 no longer accepts IMAP or POP3 passwords. Select Microsoft web authorization yourself. Your organization may still block access or sending."},
}


def windows_package_family() -> str | None:
    """Resolve package identity, accepting only the explicit unpackaged result."""
    if sys.platform != "win32":
        return None
    try:
        api = ctypes.WinDLL("kernel32", use_last_error=True)
        function = api.GetCurrentPackageFamilyName
        function.argtypes = [ctypes.POINTER(ctypes.c_uint32), ctypes.c_wchar_p]
        function.restype = ctypes.c_long
        length = ctypes.c_uint32()
        result = function(ctypes.byref(length), None)
        if result == 15700:  # APPMODEL_ERROR_NO_PACKAGE
            return None
        if result != 122 or not 1 < length.value <= 256:
            raise OSError()
        buffer = ctypes.create_unicode_buffer(length.value)
        if function(ctypes.byref(length), buffer) != 0 or not re.fullmatch(r"[A-Za-z0-9._-]+", buffer.value):
            raise OSError()
        return buffer.value
    except (OSError, AttributeError) as error:
        raise MailError("invalid_directory", "Windows package identity could not be resolved.") from error


def data_directory() -> Path:
    """Resolve only the current profile; never migrate or fall back to old data."""
    if os.environ.get("MAILMEUP_DATA_DIR"):
        raise MailError("unsupported_configuration", "Remove MAILMEUP_DATA_DIR. Configure a new AgentInbox profile.")
    explicit = os.environ.get("AGENTINBOX_DATA_DIR")
    if explicit:
        path = Path(explicit).expanduser()
        if not path.is_absolute():
            raise MailError("invalid_directory", "AGENTINBOX_DATA_DIR must be an absolute path.")
    elif sys.platform == "win32":
        if not os.environ.get("LOCALAPPDATA"):
            raise MailError("invalid_directory", "The Windows local application directory is unavailable.")
        family = windows_package_family()
        base = Path(os.environ["LOCALAPPDATA"])
        path = base / "Packages" / family / "LocalState" / "AgentInbox" if family else base / "AgentInbox"
    elif sys.platform == "darwin":
        path = Path.home() / "Library" / "Application Support" / "AgentInbox"
    else:
        path = Path(os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local" / "share"))) / "agentinbox"
    if not path.is_absolute():
        raise MailError("invalid_directory", "The profile base directory must be absolute.")
    path = path.resolve()
    if (path / "accounts.db").exists() or (path / "provider-settings.json").exists():
        raise MailError("unsupported_configuration", "This directory contains an incompatible profile. Choose a new directory and configure your mail servers.")
    return path


def text(value: object, name: str, maximum: int = 255, *, allow_empty: bool = False) -> str:
    """Validate a bounded single-line string without including it in an error."""
    if not isinstance(value, str) or len(value) > maximum or any(ord(c) < 32 or ord(c) == 127 for c in value):
        raise MailError("invalid_configuration", f"{name} must be a valid single-line value.")
    result = value.strip()
    if not result and not allow_empty:
        raise MailError("invalid_configuration", f"{name} is required.")
    return result


def address(value: object) -> str:
    """Accept one explicit ASCII mailbox, never a display-name or address list."""
    result = text(value, "Email address", 254)
    if not re.fullmatch(r"[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?", result):
        raise MailError("invalid_address", "Enter one email address without a display name.")
    if ".." in result or result.startswith(".") or ".@" in result:
        raise MailError("invalid_address", "Enter a valid email address.")
    return result


def flag(value: object, name: str) -> bool:
    """Reject truthy strings and numeric substitutes for a boolean."""
    if type(value) is not bool:
        raise MailError("invalid_configuration", f"{name} must be true or false.")
    return value


def endpoint(value: object, protocol: str) -> dict:
    """Validate one server with mandatory transport encryption."""
    if not isinstance(value, dict) or set(value) != {"host", "port", "tls", "username"}:
        raise MailError("invalid_configuration", f"{protocol.upper()} needs host, port, tls and username only.")
    host = text(value["host"], "Server").rstrip(".").lower()
    if not re.fullmatch(r"[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?", host) or ".." in host:
        raise MailError("invalid_configuration", "Enter a server hostname without a URL or path.")
    port = value["port"]
    if type(port) is not int or not 1 <= port <= 65535:
        raise MailError("invalid_configuration", "Port must be an integer between 1 and 65535.")
    if value["tls"] not in {"tls", "starttls"}:
        raise MailError("invalid_configuration", "Choose TLS or mandatory STARTTLS. Unencrypted connections are not supported.")
    return {"host": host, "port": port, "tls": value["tls"], "username": text(value["username"], "Username")}


def authentication(value: object) -> dict:
    """Accept password auth or an explicit Microsoft public-client registration."""
    if value == {"method": "password"}:
        return dict(value)
    if not isinstance(value, dict) or set(value) != {"method", "client_id", "tenant"} or value["method"] != "microsoft_oauth":
        raise MailError("invalid_configuration", "Choose a password or Microsoft web authorization.")
    try:
        client_id = str(uuid.UUID(value["client_id"]))
        tenant = value["tenant"]
        if tenant not in {"common", "organizations", "consumers"}:
            tenant = str(uuid.UUID(tenant))
    except (ValueError, TypeError, AttributeError) as error:
        raise MailError("invalid_configuration", "Microsoft web authorization needs a public app ID and a valid Microsoft account type or directory ID.") from error
    return {"method": "microsoft_oauth", "client_id": client_id, "tenant": tenant}


def account_input(value: object) -> dict:
    """Validate the new account schema without accepting compatibility fields."""
    keys = {"id", "label", "email", "incoming", "smtp", "authentication", "read_enabled", "send_enabled", "folders"}
    if not isinstance(value, dict) or set(value) != keys:
        raise MailError("invalid_configuration", "The account does not match the current mail schema.")
    incoming = value["incoming"]
    if not isinstance(incoming, dict) or set(incoming) != {"protocol", "host", "port", "tls", "username"} or incoming["protocol"] not in {"imap", "pop3"}:
        raise MailError("invalid_configuration", "Choose IMAP or POP3 and supply its server settings.")
    protocol = incoming["protocol"]
    incoming = {"protocol": protocol, **endpoint({k: v for k, v in incoming.items() if k != "protocol"}, protocol)}
    auth = authentication(value["authentication"])
    email_address = address(value["email"])
    identity = text(value["id"], "Account ID", 64, allow_empty=True)
    if identity and not re.fullmatch(r"[a-f0-9]{32}", identity):
        raise MailError("invalid_configuration", "Invalid account ID.")
    folders = value["folders"]
    if not isinstance(folders, list) or not 1 <= len(folders) <= 20:
        raise MailError("invalid_configuration", "Choose between one and twenty distinct IMAP folders.")
    normalized = [text(folder, "Folder", 255) for folder in folders]
    if len(set(normalized)) != len(normalized):
        raise MailError("invalid_configuration", "Choose distinct IMAP folder names.")
    if any(not folder.isascii() for folder in normalized):
        raise MailError("unsupported_folder", "This preview requires an ASCII IMAP folder name, such as INBOX.")
    if protocol == "pop3" and normalized != ["INBOX"]:
        raise MailError("unsupported_folder", "POP3 exposes only INBOX.")
    send_enabled = flag(value["send_enabled"], "Sending")
    smtp = endpoint(value["smtp"], "smtp") if value["smtp"] is not None else None
    if send_enabled and smtp is None:
        raise MailError("invalid_configuration", "Configure SMTP before enabling sending.")
    if auth["method"] == "microsoft_oauth":
        hosts = {"outlook.office365.com", "imap-mail.outlook.com" if protocol == "imap" else "pop-mail.outlook.com"}
        if incoming["host"] not in hosts or (smtp and smtp["host"] not in {"smtp.office365.com", "smtp-mail.outlook.com"}):
            raise MailError("invalid_configuration", "Microsoft tokens can be used only with Microsoft mail servers.")
        if incoming["username"].casefold() != email_address.casefold() or (smtp and smtp["username"].casefold() != incoming["username"].casefold()):
            raise MailError("invalid_configuration", "Microsoft mail usernames must match the account email address.")
    return {"id": identity, "label": text(value["label"], "Account name", 100), "email": email_address,
            "incoming": incoming, "smtp": smtp, "authentication": auth,
            "read_enabled": flag(value["read_enabled"], "Reading"), "send_enabled": send_enabled, "folders": normalized}
