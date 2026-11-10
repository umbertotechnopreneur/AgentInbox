# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Strict configuration for mail protocols and explicitly selected authentication.
# Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
# Modified with AI: OpenAI Codex; reject file paths used as profiles, 2026-11-02.
# Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

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
MAX_FOLDERS = 20
MICROSOFT_INCOMING_HOSTS = {
    "imap": ["outlook.office365.com", "imap-mail.outlook.com"],
    "pop3": ["outlook.office365.com", "pop-mail.outlook.com"],
}
MICROSOFT_SMTP_HOSTS = {"smtp.office365.com", "smtp-mail.outlook.com"}
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
                "microsoft_tenant": "consumers",
                "notice": "Sign in with Microsoft using your personal Microsoft account."},
    "microsoft365": {"label": "Microsoft 365", "incoming": {
                         "imap": {"host": "outlook.office365.com", "port": 993, "tls": "tls"},
                         "pop3": {"host": "outlook.office365.com", "port": 995, "tls": "tls"}},
                     "smtp": {"host": "smtp.office365.com", "port": 587, "tls": "starttls"},
                     "authentication_methods": ["password", "microsoft_oauth"],
                     "microsoft_tenant": "organizations",
                     "notice": "Sign in with Microsoft using your work or school account. Your organization may need to allow mail apps and sending."},
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
    if sys.platform not in {"win32", "darwin", "linux"}:
        raise MailError("unsupported_platform", "AgentInbox supports Windows, macOS and Linux only.")
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
        base = os.environ.get("XDG_DATA_HOME")
        path = (Path(base) if base else Path.home() / ".local" / "share") / "agentinbox"
    if not path.is_absolute():
        raise MailError("invalid_directory", "The profile base directory must be absolute.")
    path = path.resolve()
    if path.exists() and not path.is_dir():
        raise MailError("invalid_directory", "The profile path must be a directory, not an existing file.")
    if (path / "accounts.db").exists() or (path / "provider-settings.json").exists():
        raise MailError("unsupported_configuration", "This directory contains an incompatible profile. Choose a new directory and configure your mail servers.")
    return path


def text(value: object, name: str, maximum: int = 255, *, allow_empty: bool = False,
         field: str | None = None) -> str:
    """Validate a bounded single-line string without including it in an error.

    Args:
        value: Untrusted input to validate before it is used or persisted.
        name: Field or protocol name used in validation or dispatch.
        maximum: Maximum supplied string length in characters, before whitespace is trimmed.
        allow_empty: Whether a trimmed empty string is accepted.
        field: Optional local configuration field path for validation errors.
    """
    if not isinstance(value, str) or any(ord(c) < 32 or ord(c) == 127 for c in value):
        raise MailError("invalid_configuration", f"Enter {name.lower()} again on one line.", field=field)
    if len(value) > maximum:
        raise MailError("invalid_configuration", f"{name} must be {maximum} characters or fewer.", field=field)
    result = value.strip()
    if not result and not allow_empty:
        raise MailError("invalid_configuration", f"{name} is required.", field=field)
    return result


def address(value: object, *, field: str | None = None) -> str:
    """Accept one explicit ASCII mailbox, never a display-name or address list.

    Args:
        value: Untrusted input to validate before it is used or persisted.
        field: Optional local configuration field path for validation errors.
    """
    result = text(value, "Email address", 254, field=field)
    if not re.fullmatch(r"[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?", result):
        raise MailError("invalid_address", "Enter one email address, such as you@example.com, without a person's name.", field=field)
    if ".." in result or result.startswith(".") or ".@" in result:
        raise MailError("invalid_address", "Enter a valid email address.", field=field)
    return result


def flag(value: object, name: str) -> bool:
    """Reject truthy strings and numeric substitutes for a boolean.

    Args:
        value: Input that must be an actual bool; integers and strings are rejected.
        name: Field or protocol name used in validation or dispatch.
    """
    if type(value) is not bool:
        raise MailError("invalid_configuration", f"Choose whether {name.lower()} is on or off.")
    return value


def endpoint(value: object, protocol: str) -> ServerSettings:
    """Validate one server with mandatory transport encryption.

    Args:
        value: Object with exactly host, port, tls and username fields.
        protocol: Selected mail protocol, used to validate endpoints or choose
            credentials.
    """
    purpose = "sending" if protocol == "smtp" else "reading"
    section = "Send mail" if protocol == "smtp" else "Read mail"
    field = "smtp" if protocol == "smtp" else "incoming"
    if not isinstance(value, dict) or set(value) != {"host", "port", "tls", "username"}:
        raise MailError("invalid_configuration", f"Check the server, port, connection security and email address under {section}.")
    host = text(value["host"], f"Server for {purpose}", field=f"{field}.host").rstrip(".").lower()
    if not re.fullmatch(r"[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?", host) or ".." in host:
        raise MailError("invalid_configuration", f"Check the server under {section}. Enter a name such as mail.example.com, without https:// or a folder path.", field=f"{field}.host")
    port = value["port"]
    if type(port) is not int or not 1 <= port <= 65535:
        raise MailError("invalid_configuration", f"Enter a whole number from 1 to 65535 in Port under {section}.", field=f"{field}.port")
    if value["tls"] not in ("tls", "starttls"):
        raise MailError("invalid_configuration", f"Choose a Connection security option under {section}. A secure connection is required.", field=f"{field}.tls")
    return {"host": host, "port": port, "tls": value["tls"],
            "username": text(value["username"], f"Email address for {purpose}", field=f"{field}.username")}


def authentication(value: object) -> AuthenticationSettings:
    """Accept password auth or an explicit Microsoft public-client registration.

    Args:
        value: Password choice or explicit Microsoft client_id and tenant configuration.
    """
    if value == {"method": "password"}:
        return dict(value)
    if not isinstance(value, dict) or set(value) != {"method", "client_id", "tenant"} or value["method"] != "microsoft_oauth":
        raise MailError("invalid_configuration", "Choose Use a password or Sign in with Microsoft.", field="authentication.method")
    try:
        client_id = str(uuid.UUID(value["client_id"]))
    except (ValueError, TypeError, AttributeError) as error:
        raise MailError("invalid_configuration", "This app's Microsoft settings need fixing. The app ID is invalid.") from error
    try:
        tenant = value["tenant"]
        if tenant not in {"common", "organizations", "consumers"}:
            tenant = str(uuid.UUID(tenant))
    except (ValueError, TypeError, AttributeError) as error:
        raise MailError("invalid_configuration", "Check the Microsoft account type.", field="authentication.tenant") from error
    return {"method": "microsoft_oauth", "client_id": client_id, "tenant": tenant}


def account_id(value: object) -> str:
    """Validate an existing account ID or an empty new-account ID.

    Args:
        value: Untrusted ID from a settings or connection-check request.
    """
    identity = text(value, "Account ID", 64, allow_empty=True)
    if identity and not re.fullmatch(r"[a-f0-9]{32}", identity):
        raise MailError("invalid_configuration", "Invalid account ID.")
    return identity


def incoming_input(value: object) -> dict:
    """Validate an IMAP or POP3 endpoint with an explicit protocol.

    Args:
        value: Incoming server settings from the account or OAuth check context.
    """
    if not isinstance(value, dict) or set(value) != {"protocol", "host", "port", "tls", "username"} or value["protocol"] not in ("imap", "pop3"):
        raise MailError("invalid_configuration", "Under Read mail, choose how to read mail and enter your provider's server settings.", field="incoming.protocol")
    protocol = value["protocol"]
    return {"protocol": protocol, **endpoint({k: v for k, v in value.items() if k != "protocol"}, protocol)}


def validate_microsoft_endpoints(email: str, incoming: dict, smtp: ServerSettings | None) -> None:
    """Require supported hosts and one mailbox for the shared Microsoft authorization.

    Args:
        email: Normalized account mailbox.
        incoming: Validated reading endpoint and protocol.
        smtp: Validated sending endpoint, or None when sending is unconfigured.
    """
    if incoming["host"] not in MICROSOFT_INCOMING_HOSTS[incoming["protocol"]]:
        raise MailError("invalid_configuration", "Microsoft sign-in only works with Microsoft mail servers.", field="incoming.host")
    if smtp and smtp["host"] not in MICROSOFT_SMTP_HOSTS:
        raise MailError("invalid_configuration", "Microsoft sign-in only works with Microsoft mail servers.", field="smtp.host")
    if incoming["username"].casefold() != email.casefold():
        raise MailError("invalid_configuration", "For Microsoft, use the same email address under Account, Read mail and Send mail.", field="incoming.username")
    if smtp and smtp["username"].casefold() != email.casefold():
        raise MailError("invalid_configuration", "For Microsoft, use the same email address under Account, Read mail and Send mail.", field="smtp.username")


def account_input(value: object, *, discover_folders: bool = False) -> AccountSettings:
    """Validate connection settings and optional provider-preset metadata.

    Args:
        value: Account JSON payload without saved revisions or protected credential references.
        discover_folders: Allow an empty IMAP selection before a connection check lists folders.
    """
    keys = {"id", "label", "email", "incoming", "smtp", "authentication", "read_enabled", "send_enabled", "folders"}
    if not isinstance(value, dict) or set(value) not in (keys, keys | {"preset"}):
        raise MailError("invalid_configuration", "These account settings could not be read. Reload the account and try again.")
    metadata = {}
    if "preset" in value:
        preset = text(value["preset"], "Email provider", 32, field="preset")
        if preset not in PRESETS:
            raise MailError("invalid_configuration", "Choose one of the listed email providers.", field="preset")
        metadata["preset"] = preset
    label = text(value["label"], "Connection name", 100, field="label")
    email_address = address(value["email"], field="email")
    incoming = incoming_input(value["incoming"])
    protocol = incoming["protocol"]
    auth = authentication(value["authentication"])
    identity = account_id(value["id"])
    folders = value["folders"]
    minimum = 0 if discover_folders and protocol == "imap" else 1
    if not isinstance(folders, list) or not minimum <= len(folders) <= MAX_FOLDERS:
        raise MailError("invalid_configuration", f"Choose between one and {MAX_FOLDERS} mail folders under Read mail.", field="folders")
    normalized = [text(folder, "Folder", 255, field="folders") for folder in folders]
    if len(set(normalized)) != len(normalized):
        raise MailError("invalid_configuration", "Choose each mail folder only once.", field="folders")
    if any(not folder.isascii() for folder in normalized):
        raise MailError("unsupported_folder", "Folder names currently need basic English letters, numbers or punctuation, such as INBOX.", field="folders")
    if protocol == "pop3" and normalized != ["INBOX"]:
        raise MailError("unsupported_folder", "POP3 exposes only INBOX.", field="folders")
    send_enabled = flag(value["send_enabled"], "Sending")
    smtp = endpoint(value["smtp"], "smtp") if value["smtp"] is not None else None
    if send_enabled and smtp is None:
        raise MailError("invalid_configuration", "Open Send mail and enter the sending server settings before enabling sending.", field="smtp.configured")
    if auth["method"] == "microsoft_oauth":
        validate_microsoft_endpoints(email_address, incoming, smtp)
    return {**metadata, "id": identity, "label": label, "email": email_address,
            "incoming": incoming, "smtp": smtp, "authentication": auth,
            "read_enabled": flag(value["read_enabled"], "Reading"), "send_enabled": send_enabled, "folders": normalized}


def smtp_check_input(value: object) -> dict:
    """Validate the fields relevant to an SMTP check from a check or full account payload.

    Args:
        value: Account ID, SMTP settings and authentication. Microsoft also requires
            the email, incoming endpoint and sending scope bound to saved authorization.
    """
    if not isinstance(value, dict) or not {"id", "smtp", "authentication"} <= value.keys():
        raise MailError("invalid_configuration", "These sending settings could not be read.")
    if value["smtp"] is None:
        raise MailError("smtp_not_configured", "Configure an SMTP server before checking its connection.", field="smtp.configured")
    account = {"id": account_id(value["id"]), "smtp": endpoint(value["smtp"], "smtp"),
               "authentication": authentication(value["authentication"])}
    if account["authentication"]["method"] == "microsoft_oauth":
        if not {"email", "incoming", "send_enabled"} <= value.keys():
            raise MailError("invalid_configuration", "Include the account settings tied to Microsoft sign-in.")
        email = address(value["email"], field="email")
        incoming = incoming_input(value["incoming"])
        validate_microsoft_endpoints(email, incoming, account["smtp"])
        account.update(email=email, incoming=incoming, send_enabled=flag(value["send_enabled"], "Sending"))
    return account


def validate_passwords(passwords: object, *, smtp_only: bool = False) -> None:
    """Validate transient password fields without exposing their contents.

    Args:
        passwords: Password fields supplied by the caller; empty requests saved reuse.
        smtp_only: Allow a sending-only password payload for an SMTP connection check.
    """
    fields = ({"incoming", "smtp"}, {"smtp"}) if smtp_only else ({"incoming", "smtp"},)
    if not isinstance(passwords, dict) or set(passwords) not in fields:
        raise MailError("invalid_credentials", "Check the passwords for reading and sending in AgentInbox.")
    for protocol, secret in passwords.items():
        if not isinstance(secret, str) or len(secret) > 4096 or any(c in secret for c in "\0\r\n"):
            raise MailError("invalid_credentials", "Check the password in AgentInbox.", field=f"passwords.{protocol}")


def password_required(protocol: str) -> MailError:
    """Return the same missing-password error for checks and persistence.

    Args:
        protocol: Endpoint key, incoming or smtp.
    """
    purpose, section = ("reading", "Read mail") if protocol == "incoming" else ("sending", "Send mail")
    return MailError("password_required", f"Enter the password for {purpose} under {section}. A new server or email address needs its password entered again.", field=f"passwords.{protocol}")


def saved_password_reference(previous: dict | None, account: dict, protocol: str) -> str | None:
    """Find a reusable protected password reference for an unchanged endpoint.

    Args:
        previous: Saved account, or None when this is a new account.
        account: Validated settings being checked or saved.
        protocol: Endpoint key, incoming or smtp.
    """
    if previous and previous["authentication"] == account["authentication"] and previous[protocol] == account[protocol]:
        return previous[protocol + "_secret"]
    return None


def same_microsoft_context(previous: dict, account: dict) -> bool:
    """Check whether saved Microsoft authorization still covers current settings.

    Args:
        previous: Saved Microsoft account and its authorized sending scope.
        account: Validated settings with the required Microsoft account context.
    """
    return all(previous[key] == account[key] for key in ("authentication", "incoming", "smtp", "email")) and (
        not account["send_enabled"] or previous["send_enabled"])
