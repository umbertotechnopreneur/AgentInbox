"""Strict configuration for password-based IMAP and SMTP connections."""

import os
import re
import sys
from pathlib import Path

from .errors import MailError

SCHEMA_VERSION = 2
MAX_MESSAGE_BYTES = 10 * 1024 * 1024
MAX_ATTACHMENT_BYTES = 5 * 1024 * 1024
MAX_BODY_CHARS = 100_000
MAX_RECIPIENTS = 25
PRESETS = {
    "custom": {"label": "Custom IMAP / SMTP", "imap_host": "", "smtp_host": "",
               "imap_port": 993, "imap_tls": "tls", "smtp_port": 465, "smtp_tls": "tls"},
    "gmail": {"label": "Gmail", "imap_host": "imap.gmail.com", "smtp_host": "smtp.gmail.com",
              "imap_port": 993, "imap_tls": "tls", "smtp_port": 465, "smtp_tls": "tls",
              "notice": "Use an app password. Availability depends on your Google account settings."},
    "icloud": {"label": "iCloud", "imap_host": "imap.mail.me.com", "smtp_host": "smtp.mail.me.com",
               "imap_port": 993, "imap_tls": "tls", "smtp_port": 587, "smtp_tls": "starttls",
               "notice": "Use an app-specific password. SMTP uses your full email address."},
}


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
        path = Path(os.environ["LOCALAPPDATA"]) / "AgentInbox"
    elif sys.platform == "darwin":
        path = Path.home() / "Library" / "Application Support" / "AgentInbox"
    else:
        path = Path(os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local" / "share"))) / "agentinbox"
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
    """Validate one server and reject unsupported authentication requirements early."""
    if not isinstance(value, dict) or set(value) != {"host", "port", "tls", "username"}:
        raise MailError("invalid_configuration", f"{protocol.upper()} needs host, port, tls and username only.")
    host = text(value["host"], "Server").rstrip(".").lower()
    if not re.fullmatch(r"[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?", host) or ".." in host:
        raise MailError("invalid_configuration", "Enter a server hostname without a URL or path.")
    if host.endswith((".office365.com", ".outlook.com")) or host in {"outlook.com", "office365.com"}:
        raise MailError("unsupported_provider", "Outlook.com and Microsoft 365 do not support this client's password-based connection. Use a provider that accepts IMAP/SMTP passwords or app passwords.")
    port = value["port"]
    if type(port) is not int or not 1 <= port <= 65535:
        raise MailError("invalid_configuration", "Port must be an integer between 1 and 65535.")
    if value["tls"] not in {"tls", "starttls"}:
        raise MailError("invalid_configuration", "Choose TLS or mandatory STARTTLS. Unencrypted connections are not supported.")
    return {"host": host, "port": port, "tls": value["tls"], "username": text(value["username"], "Username")}


def account_input(value: object) -> dict:
    """Validate the new account schema without accepting compatibility fields."""
    keys = {"id", "label", "email", "imap", "smtp", "read_enabled", "send_enabled", "folders"}
    if not isinstance(value, dict) or set(value) != keys:
        raise MailError("invalid_configuration", "The account does not match the current IMAP/SMTP schema.")
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
    send_enabled = flag(value["send_enabled"], "Sending")
    smtp = endpoint(value["smtp"], "smtp") if value["smtp"] is not None else None
    if send_enabled and smtp is None:
        raise MailError("invalid_configuration", "Configure SMTP before enabling sending.")
    return {"id": identity, "label": text(value["label"], "Account name", 100), "email": address(value["email"]),
            "imap": endpoint(value["imap"], "imap"), "smtp": smtp,
            "read_enabled": flag(value["read_enabled"], "Reading"), "send_enabled": send_enabled, "folders": normalized}
