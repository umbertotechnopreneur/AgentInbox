"""Bounded IMAP reads and explicit SMTP submission using standard protocols."""

import contextlib
import datetime as dt
import email
import email.policy
import email.utils
import imaplib
import re
import smtplib
import socket
import ssl
import time
from html.parser import HTMLParser

from .config import MAX_MESSAGE_BYTES
from .errors import MailError

TIMEOUT = 20


def _quote(value: str) -> str:
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def _ok(result: tuple, message: str) -> list:
    if result[0] != "OK":
        raise MailError("mail_operation_failed", message)
    return result[1]


@contextlib.contextmanager
def imap_connection(settings: dict, password: str):
    """Require verified TLS before transmitting credentials."""
    connection = None
    try:
        context = ssl.create_default_context()
        if settings["tls"] == "tls":
            connection = imaplib.IMAP4_SSL(settings["host"], settings["port"], ssl_context=context, timeout=TIMEOUT)
        else:
            connection = imaplib.IMAP4(settings["host"], settings["port"], timeout=TIMEOUT)
            _ok(connection.starttls(ssl_context=context), "The IMAP server did not establish STARTTLS.")
        try:
            connection.login(settings["username"], password)
        except imaplib.IMAP4.error as error:
            raise MailError("authentication_failed", "IMAP sign-in failed. Check the username, password or app password and the provider's IMAP settings.") from error
        yield connection
    except ssl.SSLError as error:
        raise MailError("tls_failed", "The server's TLS connection or certificate could not be verified.") from error
    except (OSError, socket.timeout, imaplib.IMAP4.abort) as error:
        raise MailError("connection_failed", "The mail server could not complete the connection. Check the server, port and TLS mode.") from error
    except imaplib.IMAP4.error as error:
        raise MailError("mail_operation_failed", "The IMAP server rejected the operation.") from error
    finally:
        if connection is not None:
            try:
                connection.logout()
            except (OSError, imaplib.IMAP4.error):
                pass


def select_folder(connection, folder: str) -> str:
    """Select a folder read-only and return its UID validity."""
    _ok(connection.select(_quote(folder), readonly=True), "The selected IMAP folder is unavailable.")
    validity = connection.response("UIDVALIDITY")[1]
    if not validity or not validity[0] or not validity[0].isdigit():
        raise MailError("invalid_server_response", "The server did not provide a stable folder identity.")
    return validity[0].decode("ascii")


def folders(connection) -> list[str]:
    """Return bounded raw folder descriptions for explicit user configuration."""
    rows = _ok(connection.list(), "The server could not list folders.")
    return [row.decode("utf-8", errors="replace")[:512] for row in rows[:100] if isinstance(row, bytes)]


def _fetch(connection, uid: str, parts: str) -> tuple[bytes, bytes]:
    rows = _ok(connection.uid("FETCH", uid, parts), "The message could not be read.")
    for row in rows:
        if isinstance(row, tuple) and len(row) == 2 and isinstance(row[1], bytes):
            return row[0], row[1]
    raise MailError("message_unavailable", "The message may have been moved or removed.")


def _received(metadata: bytes) -> dt.datetime:
    match = re.search(rb'INTERNALDATE "([^"]+)"', metadata)
    if not match:
        raise MailError("invalid_server_response", "The message has no valid received date.")
    try:
        return email.utils.parsedate_to_datetime(match[1].decode("ascii").replace("-", " ", 2)).astimezone(dt.timezone.utc)
    except (TypeError, ValueError) as error:
        raise MailError("invalid_server_response", "The server returned an invalid received date.") from error


def header(connection, uid: str) -> dict:
    """Read a limited header preview without changing the Seen flag."""
    metadata, raw = _fetch(connection, uid, "(UID FLAGS INTERNALDATE RFC822.SIZE BODY.PEEK[HEADER.FIELDS (SUBJECT FROM TO CC DATE MESSAGE-ID CONTENT-TYPE)]<0.16384>)")
    message = email.message_from_bytes(raw, policy=email.policy.default)
    size = re.search(rb"RFC822.SIZE (\d+)", metadata)
    return {"uid": uid, "subject": str(message.get("Subject", ""))[:500],
            "from": str(message.get("From", ""))[:500], "to": str(message.get("To", ""))[:1000],
            "received_at": _received(metadata).isoformat(), "unread": b"\\Seen" not in metadata,
            "size": int(size[1]) if size else None}


def search(connection, folder: str, start: dt.datetime, end: dt.datetime,
           query: str, unread: bool, limit: int, offset: int = 0,
           candidates: list[str] | None = None, expected_validity: str | None = None) -> dict:
    """Search a bounded date interval and report any candidate truncation."""
    validity = select_folder(connection, folder)
    if expected_validity is not None and validity != expected_validity:
        raise MailError("reference_expired", "The folder changed. Start a new search.")
    # IMAP date searches are day-granular. Widen by one day, then filter exact UTC times.
    since = (start - dt.timedelta(days=1)).strftime("%d-%b-%Y")
    before = (end + dt.timedelta(days=1)).strftime("%d-%b-%Y")
    criteria = ["SINCE", since, "BEFORE", before]
    if unread:
        criteria.append("UNSEEN")
    if query:
        if not query.isascii():
            raise MailError("unsupported_query", "This preview accepts ASCII server search text. Use dates and inspect message text for other languages.")
        criteria.extend(["TEXT", _quote(query)])
    truncated = False
    if candidates is None:
        result = _ok(connection.uid("SEARCH", None, *criteria), "The server could not search this folder.")
        raw = result[0] if result and result[0] else b""
        if not isinstance(raw, bytes) or len(raw) > 1_000_000:
            raise MailError("search_too_large", "Narrow the search date range.")
        identities = raw.split()
        if any(not identity.isdigit() for identity in identities):
            raise MailError("invalid_server_response", "The server returned an invalid message identifier.")
        truncated = len(identities) > 300
        candidates = [identity.decode("ascii") for identity in reversed(identities[-300:])]
    matches = []
    inspected = 0
    deadline = time.monotonic() + 20
    for identity in candidates[offset:]:
        if inspected >= 50 or len(matches) >= limit or time.monotonic() >= deadline:
            break
        inspected += 1
        preview = header(connection, identity)
        received = dt.datetime.fromisoformat(preview["received_at"])
        if start <= received < end and (not unread or preview["unread"]):
            preview.update({"folder": folder, "uid_validity": validity})
            matches.append(preview)
    next_offset = offset + inspected
    return {"messages": matches, "more": next_offset < len(candidates), "next_offset": next_offset,
            "candidate_limit_reached": truncated, "uid_validity": validity, "candidates": candidates}


class _PlainHtml(HTMLParser):
    """Extract visible text without executing markup or fetching remote resources."""

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.parts = []
        self.hidden = 0

    def handle_starttag(self, tag, attrs):
        if tag in {"script", "style", "head"}:
            self.hidden += 1
        elif tag in {"br", "p", "div", "li"}:
            self.parts.append("\n")

    def handle_endtag(self, tag):
        if tag in {"script", "style", "head"}:
            self.hidden = max(0, self.hidden - 1)

    def handle_data(self, data):
        if not self.hidden:
            self.parts.append(data)


def read(connection, folder: str, uid: str, validity: str) -> dict:
    """Read one message, refusing an excessive payload before fetching its body."""
    if select_folder(connection, folder) != validity:
        raise MailError("reference_expired", "The folder identity changed. Search again.")
    preview = header(connection, uid)
    if preview["size"] is None or preview["size"] > MAX_MESSAGE_BYTES:
        raise MailError("message_too_large", "This message exceeds the 10 MiB read limit.")
    _, raw = _fetch(connection, uid, f"(UID BODY.PEEK[]<0.{MAX_MESSAGE_BYTES + 1}>)")
    if len(raw) > MAX_MESSAGE_BYTES or len(raw) != preview["size"]:
        raise MailError("incomplete_message", "The server did not return the complete bounded message.")
    message = email.message_from_bytes(raw, policy=email.policy.default)
    part = message.get_body(preferencelist=("plain", "html"))
    content = ""
    if part is not None:
        try:
            content = part.get_content()
        except (LookupError, UnicodeError, ValueError) as error:
            raise MailError("invalid_message", "The message text could not be decoded.") from error
        if part.get_content_type() == "text/html":
            parser = _PlainHtml()
            parser.feed(content)
            content = "".join(parser.parts)
    attachments = [{"name": str(item.get_filename() or "attachment")[:255], "content_type": item.get_content_type()}
                   for item in message.iter_attachments()][:100]
    return {**preview, "body": content[:30_000], "body_truncated": len(content) > 30_000,
            "attachments": attachments, "content_is_untrusted": True}


def check_read(settings: dict, password: str, folder: str) -> dict:
    """Verify a real bounded read, distinguishing an empty folder from a read sample."""
    with imap_connection(settings, password) as connection:
        available = folders(connection)
        validity = select_folder(connection, folder)
        counts = connection.response("EXISTS")[1]
        if not counts or not counts[0] or not counts[0].isdigit():
            raise MailError("invalid_server_response", "The server did not return a valid folder message count.")
        if int(counts[0]) == 0:
            return {"connection": "passed", "authentication": "passed", "folder": "passed",
                    "message_read": "no_sample", "folders": available,
                    "message": "Connected and authenticated. The folder is empty; no message read was tested."}
        rows = _ok(connection.fetch(counts[0].decode("ascii"), "(UID)"), "The last message could not be inspected.")
        identity_match = next((match for row in rows if isinstance(row, bytes)
                               if (match := re.search(rb"UID (\d+)", row))), None)
        if identity_match is None:
            raise MailError("invalid_server_response", "The server returned an invalid message identifier.")
        read(connection, folder, identity_match[1].decode("ascii"), validity)
        return {"connection": "passed", "authentication": "passed", "folder": "passed",
                "message_read": "passed", "folders": available,
                "message": "Connected, authenticated and read one message without changing its read status."}


@contextlib.contextmanager
def smtp_connection(settings: dict, password: str):
    """Connect and authenticate to a configured submission server over verified TLS."""
    connection = None
    try:
        context = ssl.create_default_context()
        if settings["tls"] == "tls":
            connection = smtplib.SMTP_SSL(settings["host"], settings["port"], timeout=TIMEOUT, context=context)
        else:
            connection = smtplib.SMTP(settings["host"], settings["port"], timeout=TIMEOUT)
            connection.ehlo()
            connection.starttls(context=context)
        connection.ehlo()
        connection.login(settings["username"], password)
        yield connection
    except smtplib.SMTPAuthenticationError as error:
        raise MailError("authentication_failed", "SMTP sign-in failed. Check its username, password or app password and provider settings.") from error
    except ssl.SSLError as error:
        raise MailError("tls_failed", "The SMTP TLS connection or certificate could not be verified.") from error
    except (OSError, smtplib.SMTPException) as error:
        raise MailError("smtp_failed", "The SMTP server could not complete the operation.") from error
    finally:
        if connection is not None:
            try:
                connection.close()
            except OSError:
                pass


def submit(settings: dict, password: str, sender: str, recipients: list[str], content: bytes, before_data) -> str:
    """Submit once; reject any recipient failure before transmitting message DATA."""
    with smtp_connection(settings, password) as connection:
        if connection.mail(sender)[0] != 250:
            return "rejected"
        for recipient in recipients:
            if connection.rcpt(recipient)[0] not in (250, 251):
                connection.rset()
                return "rejected"
        before_data()
        try:
            code, _ = connection.data(content)
            return "accepted_by_server" if code == 250 else "rejected"
        except smtplib.SMTPDataError:
            return "rejected"
        except (OSError, smtplib.SMTPException):
            # The server may have accepted DATA even when its final response was lost.
            return "outcome_unknown"
