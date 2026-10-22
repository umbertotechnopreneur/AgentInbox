"""Strict configuration for mail protocols and explicitly selected authentication."""

import os
import ctypes
import re
import sys
import uuid
from pathlib import Path

from .errors import MailError
from .models import AccountSettings, AuthenticationSettings, ProviderPreset, ServerSettings

SCHEMA_VERSION = 3
MAX_MESSAGE_BYTES = 10 * 1024 * 1024
MAX_ATTACHMENT_BYTES = 5 * 1024 * 1024
MAX_BODY_CHARS = 100_000
MAX_RECIPIENTS = 25
PRESETS: dict[str, ProviderPreset] = {
    "custom": {"label": "Custom mail server", "incoming": {}, "smtp": None,
               "authentication_methods": ["password"]},
    "gmail": {"label": "Gmail", "incoming": {
                  "imap": {"host": "imap.gmail.com", "port": 993, "tls": "tls"},
                  "pop3": {"host": "pop.gmail.com", "port": 995, "tls": "tls"}},
              "smtp": {"host": "smtp.gmail.com", "port": 465, "tls": "tls"},
              "authentication_methods": ["password"],
              "notice": "Your usual Gmail password will not work. Create a Google app password for reading and sending."},
    "icloud": {"label": "iCloud", "incoming": {
                   "imap": {"host": "imap.mail.me.com", "port": 993, "tls": "tls"}},
               "smtp": {"host": "smtp.mail.me.com", "port": 587, "tls": "starttls"},
               "authentication_methods": ["password"],
               "notice": "Create an Apple app-specific password for reading and sending. Use your full email address for sending."},
    "outlook": {"label": "Outlook.com", "incoming": {
                    "imap": {"host": "outlook.office365.com", "port": 993, "tls": "tls"},
                    "pop3": {"host": "outlook.office365.com", "port": 995, "tls": "tls"}},
                "smtp": {"host": "smtp-mail.outlook.com", "port": 587, "tls": "starttls"},
                "authentication_methods": ["password", "microsoft_oauth"],
                "notice": "Sign in with Microsoft. Windows setup selects this for you when you choose Outlook.com."},
    "microsoft365": {"label": "Microsoft 365", "incoming": {
                         "imap": {"host": "outlook.office365.com", "port": 993, "tls": "tls"},
                         "pop3": {"host": "outlook.office365.com", "port": 995, "tls": "tls"}},
                     "smtp": {"host": "smtp.office365.com", "port": 587, "tls": "starttls"},
                     "authentication_methods": ["password", "microsoft_oauth"],
                     "notice": "Sign in with Microsoft. Windows setup selects this for you. Your work or school may need to allow mail apps and sending."},
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
    if not isinstance(value, str) or any(ord(c) < 32 or ord(c) == 127 for c in value):
        raise MailError("invalid_configuration", f"Enter {name.lower()} again on one line.")
    if len(value) > maximum:
        raise MailError("invalid_configuration", f"{name} must be {maximum} characters or fewer.")
    result = value.strip()
    if not result and not allow_empty:
        raise MailError("invalid_configuration", f"{name} is required.")
    return result


def address(value: object) -> str:
    """Accept one explicit ASCII mailbox, never a display-name or address list."""
    result = text(value, "Email address", 254)
    if not re.fullmatch(r"[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?", result):
        raise MailError("invalid_address", "Enter one email address, such as you@example.com, without a person's name.")
    if ".." in result or result.startswith(".") or ".@" in result:
        raise MailError("invalid_address", "Enter a valid email address.")
    return result


def flag(value: object, name: str) -> bool:
    """Reject truthy strings and numeric substitutes for a boolean."""
    if type(value) is not bool:
        raise MailError("invalid_configuration", f"Choose whether {name.lower()} is on or off.")
    return value


def endpoint(value: object, protocol: str) -> ServerSettings:
    """Validate one server with mandatory transport encryption."""
    purpose = "sending" if protocol == "smtp" else "reading"
    section = "Send mail" if protocol == "smtp" else "Read mail"
    if not isinstance(value, dict) or set(value) != {"host", "port", "tls", "username"}:
        raise MailError("invalid_configuration", f"Check the server, port, connection security and email address under {section}.")
    host = text(value["host"], f"Server for {purpose}").rstrip(".").lower()
    if not re.fullmatch(r"[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?", host) or ".." in host:
        raise MailError("invalid_configuration", f"Check the server under {section}. Enter a name such as mail.example.com, without https:// or a folder path.")
    port = value["port"]
    if type(port) is not int or not 1 <= port <= 65535:
        raise MailError("invalid_configuration", f"Enter a whole number from 1 to 65535 in Port under {section}.")
    if value["tls"] not in {"tls", "starttls"}:
        raise MailError("invalid_configuration", f"Choose a Connection security option under {section}. A secure connection is required.")
    return {"host": host, "port": port, "tls": value["tls"],
            "username": text(value["username"], f"Email address for {purpose}")}


def authentication(value: object) -> AuthenticationSettings:
    """Accept password auth or an explicit Microsoft public-client registration."""
    if value == {"method": "password"}:
        return dict(value)
    if not isinstance(value, dict) or set(value) != {"method", "client_id", "tenant"} or value["method"] != "microsoft_oauth":
        raise MailError("invalid_configuration", "Choose Use a password or Sign in with Microsoft.")
    try:
        client_id = str(uuid.UUID(value["client_id"]))
        tenant = value["tenant"]
        if tenant not in {"common", "organizations", "consumers"}:
            tenant = str(uuid.UUID(tenant))
    except (ValueError, TypeError, AttributeError) as error:
        raise MailError("invalid_configuration", "Check the Microsoft account type. This app's Microsoft settings may also need fixing.") from error
    return {"method": "microsoft_oauth", "client_id": client_id, "tenant": tenant}


def account_input(value: object) -> AccountSettings:
    """Validate connection settings and optional provider-preset metadata."""
    keys = {"id", "label", "email", "incoming", "smtp", "authentication", "read_enabled", "send_enabled", "folders"}
    if not isinstance(value, dict) or set(value) not in (keys, keys | {"preset"}):
        raise MailError("invalid_configuration", "These account settings could not be read. Reload the account and try again.")
    metadata = {}
    if "preset" in value:
        preset = text(value["preset"], "Email provider", 32)
        if preset not in PRESETS:
            raise MailError("invalid_configuration", "Choose one of the listed email providers.")
        metadata["preset"] = preset
    email_address = address(value["email"])
    incoming = value["incoming"]
    if not isinstance(incoming, dict) or set(incoming) != {"protocol", "host", "port", "tls", "username"} or incoming["protocol"] not in {"imap", "pop3"}:
        raise MailError("invalid_configuration", "Under Read mail, choose how to read mail and enter your provider's server settings.")
    protocol = incoming["protocol"]
    incoming = {"protocol": protocol, **endpoint({k: v for k, v in incoming.items() if k != "protocol"}, protocol)}
    auth = authentication(value["authentication"])
    identity = text(value["id"], "Account ID", 64, allow_empty=True)
    if identity and not re.fullmatch(r"[a-f0-9]{32}", identity):
        raise MailError("invalid_configuration", "Invalid account ID.")
    folders = value["folders"]
    if not isinstance(folders, list) or not 1 <= len(folders) <= 20:
        raise MailError("invalid_configuration", "Choose between one and twenty mail folders under Read mail.")
    normalized = [text(folder, "Folder", 255) for folder in folders]
    if len(set(normalized)) != len(normalized):
        raise MailError("invalid_configuration", "Choose each mail folder only once.")
    if any(not folder.isascii() for folder in normalized):
        raise MailError("unsupported_folder", "Folder names currently need basic English letters, numbers or punctuation, such as INBOX.")
    if protocol == "pop3" and normalized != ["INBOX"]:
        raise MailError("unsupported_folder", "POP3 exposes only INBOX.")
    send_enabled = flag(value["send_enabled"], "Sending")
    smtp = endpoint(value["smtp"], "smtp") if value["smtp"] is not None else None
    if send_enabled and smtp is None:
        raise MailError("invalid_configuration", "Open Send mail and enter the sending server settings before enabling sending.")
    if auth["method"] == "microsoft_oauth":
        hosts = {"outlook.office365.com", "imap-mail.outlook.com" if protocol == "imap" else "pop-mail.outlook.com"}
        if incoming["host"] not in hosts or (smtp and smtp["host"] not in {"smtp.office365.com", "smtp-mail.outlook.com"}):
            raise MailError("invalid_configuration", "Microsoft sign-in only works with Microsoft mail servers.")
        if incoming["username"].casefold() != email_address.casefold() or (smtp and smtp["username"].casefold() != incoming["username"].casefold()):
            raise MailError("invalid_configuration", "For Microsoft, use the same email address under Account, Read mail and Send mail.")
    return {**metadata, "id": identity, "label": text(value["label"], "Connection name", 100), "email": email_address,
            "incoming": incoming, "smtp": smtp, "authentication": auth,
            "read_enabled": flag(value["read_enabled"], "Reading"), "send_enabled": send_enabled, "folders": normalized}
