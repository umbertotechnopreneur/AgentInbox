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

from .config import MAX_MESSAGE_BYTES, endpoint
from .errors import MailError, server_response

TIMEOUT = 20
MAX_CONTROL_BYTES = 65_536
MAX_SEARCH_BYTES = 1_048_576
MAX_HEADER_BYTES = 16_384


class _BoundedWire:
    """Bound response bytes, lines and elapsed time before library accumulation."""

    def __init__(self, *args, **kwargs):
        self._wire_buffer = bytearray()
        self._set_response_limits(MAX_CONTROL_BYTES, lines=256)
        try:
            super().__init__(*args, **kwargs)
        except BaseException:
            for resource in (getattr(self, "file", None), getattr(self, "sock", None)):
                if resource is not None:
                    try:
                        resource.close()
                    except OSError:
                        pass
            raise

    def _set_response_limits(self, maximum, *, literal=0, lines=256, deadline=None,
                             line_limit=16_384):
        self._bytes_left = maximum
        self._literal_limit = literal
        self._lines_left = lines
        self._line_limit = min(line_limit, maximum)
        self._deadline = time.monotonic() + TIMEOUT if deadline is None else deadline

    def _remaining_time(self):
        remaining = self._deadline - time.monotonic()
        if remaining <= 0:
            raise MailError("operation_timeout", "The bounded mail operation timed out.")
        return min(TIMEOUT, remaining)

    def _receive(self, maximum):
        self.sock.settimeout(self._remaining_time())
        capacity = min(4096, maximum, self._bytes_left - len(self._wire_buffer))
        if capacity <= 0:
            raise MailError("response_too_large", "The mail response exceeds its byte limit.")
        chunk = self.sock.recv(capacity)
        if not chunk:
            raise MailError("connection_failed", "The mail server closed an incomplete response.")
        self._wire_buffer.extend(chunk)

    def _take(self, size):
        self._remaining_time()
        if size > self._bytes_left:
            raise MailError("response_too_large", "The mail response exceeds its byte limit.")
        value = bytes(self._wire_buffer[:size])
        del self._wire_buffer[:size]
        self._bytes_left -= size
        return value

    def read(self, size):
        """Reject a server-declared literal before receiving or allocating it."""
        self._remaining_time()
        if type(size) is not int or not 0 <= size <= min(self._literal_limit, self._bytes_left):
            raise MailError("response_too_large", "The server-declared mail literal exceeds the permitted size.")
        result = bytearray()
        while len(result) < size:
            if not self._wire_buffer:
                self._receive(size - len(result))
            result.extend(self._take(min(size - len(result), len(self._wire_buffer))))
        return bytes(result)

    def readline(self):
        """Read one CRLF line under the operation's shared response budget."""
        while True:
            self._remaining_time()
            boundary = self._wire_buffer.find(b"\n")
            if boundary >= 0:
                size = boundary + 1
                if size > self._line_limit or self._lines_left <= 0:
                    raise MailError("response_too_large", "The mail response exceeds its line limit.")
                if boundary == 0 or self._wire_buffer[boundary - 1] != 13:
                    raise MailError("invalid_server_response", "The mail server returned malformed line endings.")
                self._lines_left -= 1
                return self._take(size)
            if len(self._wire_buffer) >= self._line_limit:
                raise MailError("response_too_large", "The mail response exceeds its line limit.")
            self._receive(self._line_limit - len(self._wire_buffer))


class _ImapLimits(_BoundedWire):
    def _simple_command(self, name, *arguments):
        result = super()._simple_command(name, *arguments)
        if name == "STARTTLS" and self._wire_buffer:
            raise MailError("invalid_server_response", "Unexpected plaintext followed the IMAP STARTTLS reply.")
        return result


class _IMAP4(_ImapLimits, imaplib.IMAP4):
    """IMAP with bounded wire reads before required STARTTLS."""


class _IMAP4SSL(_ImapLimits, imaplib.IMAP4_SSL):
    """Implicit-TLS IMAP with the same bounded wire contract."""


class _SmtpLimits(_BoundedWire):
    def docmd(self, command, arguments=""):
        result = super().docmd(command, arguments)
        if command.upper() == "STARTTLS" and self._wire_buffer:
            raise smtplib.SMTPServerDisconnected("Unexpected plaintext followed the SMTP STARTTLS reply.")
        return result

    def getreply(self):
        """Bound the complete SMTP reply, including continuation lines."""
        self._set_response_limits(MAX_CONTROL_BYTES, lines=100, line_limit=8192)
        rows, raw_rows, expected = [], [], None
        try:
            while True:
                line = self.readline()
                raw_rows.append(line)
                if len(line) < 5 or not line[:3].isdigit() or (line[3:4] not in {b" ", b"-"} and line[3:] != b"\r\n"):
                    raise MailError("invalid_server_response", "The SMTP server returned an invalid status line.")
                code = int(line[:3])
                if expected is not None and code != expected:
                    raise MailError("invalid_server_response", "The SMTP server changed status within a multiline reply.")
                expected = code
                rows.append(line[4:-2])
                if line[3:4] != b"-":
                    return code, b"\n".join(rows)
        except MailError as error:
            # Once DATA is sent, losing a valid reply must become an unknown outcome.
            failure = smtplib.SMTPServerDisconnected(f"SMTP reply failed ({error.code}): {error}")
            # Keep bounded wire content private until the connection redacts credentials.
            failure._smtp_reply = b"".join(raw_rows) + bytes(self._wire_buffer)
            raise failure from error


class _SMTP(_SmtpLimits, smtplib.SMTP):
    """SMTP with bounded replies before required STARTTLS."""


class _SMTPSSL(_SmtpLimits, smtplib.SMTP_SSL):
    """Implicit-TLS SMTP with bounded replies."""


def _settings(settings, protocol):
    keys = {"host", "port", "tls", "username"}
    allowed = (keys, keys | {"protocol"}) if protocol == "imap" else (keys,)
    if (not isinstance(settings, dict) or set(settings) not in allowed
            or ("protocol" in settings and settings["protocol"] != protocol)
            or type(settings.get("port")) is not int
            or not isinstance(settings.get("tls"), str)):
        raise MailError("invalid_configuration", "Supply the supported mail protocol, port and TLS settings.")
    return endpoint({key: settings[key] for key in keys}, protocol)


def _imap_request(connection, method, *arguments, maximum=MAX_CONTROL_BYTES, literal=0,
                  lines=256, deadline=None, line_limit=16_384, **keywords):
    connection._set_response_limits(maximum, literal=literal, lines=lines,
                                    deadline=deadline, line_limit=line_limit)
    return getattr(connection, method)(*arguments, **keywords)


def _quote(value: str) -> str:
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def _ok(result: tuple, message: str) -> list:
    if result[0] != "OK":
        replies = [item.decode("utf-8", errors="replace") if isinstance(item, bytes) else item
                   for item in (result[1] or []) if isinstance(item, (str, bytes))]
        error = MailError("mail_operation_failed", message)
        # Keep the raw reply private until the connection can redact its credentials.
        error._imap_reply = str(result[0]) + " " + " ".join(replies)
        raise error
    return result[1]


def _connection_detail(protocol, error, settings, password, access_token):
    username = settings.get("username", "")
    secrets = (username, password, access_token or "", f"\0{username}\0{password}",
               f"{username}\0{username}\0{password}",
               f"user={username}\x01auth=Bearer {access_token}\x01\x01")
    if isinstance(error, smtplib.SMTPResponseException):
        reply = error.smtp_error
        reply = reply.decode("utf-8", errors="replace") if isinstance(reply, bytes) else str(reply)
        reply = f"{error.smtp_code} {reply}"
    else:
        reply = error.args[0] if error.args and isinstance(error.args[0], (str, bytes)) else str(error)
    received = getattr(error, "_smtp_reply", b"")
    if received:
        reply = reply.decode("utf-8", errors="replace") if isinstance(reply, bytes) else reply
        reply += " Received from server: " + received.decode("utf-8", errors="replace")
    return server_response(protocol, reply, *secrets)


@contextlib.contextmanager
def imap_connection(settings: dict, password: str, *, access_token: str | None = None, progress=None):
    """Require verified TLS before transmitting credentials."""
    connection = None
    try:
        settings = _settings(settings, "imap")
        if progress:
            progress("connection", "started")
        context = ssl.create_default_context()
        if settings["tls"] == "tls":
            connection = _IMAP4SSL(settings["host"], settings["port"], ssl_context=context, timeout=TIMEOUT)
        else:
            connection = _IMAP4(settings["host"], settings["port"], timeout=TIMEOUT)
            _ok(_imap_request(connection, "starttls", ssl_context=context), "The IMAP server did not establish STARTTLS.")
        if progress:
            progress("connection", "passed")
            progress("authentication", "started")
        try:
            if access_token is not None:
                response = f"user={settings['username']}\x01auth=Bearer {access_token}\x01\x01".encode("utf-8")
                responses = iter((response,))
                _imap_request(connection, "authenticate", "XOAUTH2", lambda _: next(responses, b""), lines=8)
            else:
                _imap_request(connection, "login", settings["username"], password)
        except imaplib.IMAP4.error as error:
            raise MailError("authentication_failed", "Your mail provider did not let this app sign in to read mail. Check the sign-in help under Email provider.",
                            server_response=_connection_detail("IMAP", error, settings, password, access_token)) from error
        if progress:
            progress("authentication", "passed")
        yield connection
    except MailError as error:
        if hasattr(error, "_imap_reply"):
            error.server_response = _connection_detail("IMAP", Exception(error._imap_reply), settings, password, access_token)
            del error._imap_reply
        raise
    except ssl.SSLError as error:
        raise MailError("tls_failed", "The server's TLS connection or certificate could not be verified.",
                        server_response=_connection_detail("IMAP", error, settings, password, access_token)) from error
    except (OSError, socket.timeout, imaplib.IMAP4.abort) as error:
        raise MailError("connection_failed", "The mail server could not complete the connection. Check the server, port and TLS mode.",
                        server_response=_connection_detail("IMAP", error, settings, password, access_token)) from error
    except imaplib.IMAP4.error as error:
        raise MailError("mail_operation_failed", "The IMAP server rejected the operation.",
                        server_response=_connection_detail("IMAP", error, settings, password, access_token)) from error
    finally:
        if connection is not None:
            try:
                connection.shutdown()
            except (OSError, imaplib.IMAP4.error):
                pass


def select_folder(connection, folder: str, *, deadline=None) -> str:
    """Select a folder read-only and return its UID validity."""
    _ok(_imap_request(connection, "select", _quote(folder), readonly=True, deadline=deadline), "The selected IMAP folder is unavailable.")
    validity = connection.response("UIDVALIDITY")[1]
    if not validity or not validity[0] or not validity[0].isdigit():
        raise MailError("invalid_server_response", "The server did not provide a stable folder identity.")
    return validity[0].decode("ascii")


def _folder_name(row) -> str | None:
    """Extract an exact selectable ASCII mailbox from a bounded IMAP LIST row."""
    literal = None
    if isinstance(row, tuple) and len(row) == 2:
        row, literal = row
    if not isinstance(row, bytes) or (literal is not None and not isinstance(literal, bytes)):
        raise MailError("invalid_server_response", "The server returned an invalid folder listing.")
    match = re.fullmatch(rb'\(([^()]*)\) +(?:NIL|"(?:[^"\\\r\n]|\\["\\])*") +(.+)', row)
    if match is None:
        raise MailError("invalid_server_response", "The server returned an invalid folder listing.")
    if b"\\noselect" in match[1].lower().split():
        return None
    mailbox = match[2]
    if literal is not None:
        marker = re.fullmatch(rb'\{(\d+)\}', mailbox)
        if marker is None or int(marker[1]) != len(literal):
            raise MailError("invalid_server_response", "The server returned an invalid folder name literal.")
        mailbox = literal
    elif mailbox.startswith(b'"'):
        quoted = re.fullmatch(rb'"((?:[^"\\\x00-\x1f\x7f]|\\["\\])*)"', mailbox)
        if quoted is None:
            raise MailError("invalid_server_response", "The server returned an invalid quoted folder name.")
        mailbox = re.sub(rb'\\(["\\])', rb'\1', quoted[1])
    elif re.fullmatch(rb'[^\x00-\x20\x7f(){"\\*%]+', mailbox) is None:
        raise MailError("invalid_server_response", "The server returned an invalid folder name.")
    if not 1 <= len(mailbox) <= 255 or any(byte < 32 or byte > 126 for byte in mailbox) or mailbox != mailbox.strip():
        raise MailError("unsupported_folder", "This preview requires exact ASCII folder names of at most 255 characters.")
    return mailbox.decode("ascii")


def folders(connection, *, deadline=None) -> list[str]:
    """Return bounded exact selectable folder names, without LIST flags or delimiters."""
    rows = _ok(_imap_request(connection, "list", literal=512, lines=256, deadline=deadline), "The server could not list folders.")
    names = []
    for row in rows:
        if row is None or row == b"":
            continue
        name = _folder_name(row)
        if name is not None and name not in names:
            names.append(name)
            if len(names) > 100:
                raise MailError("folder_limit", "The folder listing exceeds the 100-folder limit.")
    return names


def _fetch(connection, uid: str, parts: str, maximum: int, *, deadline=None) -> tuple[bytes, bytes]:
    rows = _ok(_imap_request(connection, "uid", "FETCH", uid, parts,
                             maximum=maximum + MAX_CONTROL_BYTES, literal=maximum, deadline=deadline), "The message could not be read.")
    for row in rows:
        if isinstance(row, tuple) and len(row) == 2 and isinstance(row[1], bytes):
            identity = re.search(rb"\bUID (\d+)\b", row[0])
            if identity is None or identity[1].decode("ascii") != uid:
                raise MailError("invalid_server_response", "The server returned a different message identity.")
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


def header(connection, uid: str, *, deadline=None) -> dict:
    """Read a limited header preview without changing the Seen flag."""
    metadata, raw = _fetch(connection, uid, "(UID FLAGS INTERNALDATE RFC822.SIZE BODY.PEEK[HEADER.FIELDS (SUBJECT FROM TO CC DATE MESSAGE-ID CONTENT-TYPE)]<0.16384>)", MAX_HEADER_BYTES, deadline=deadline)
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
    deadline = time.monotonic() + TIMEOUT
    validity = select_folder(connection, folder, deadline=deadline)
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
        result = _ok(_imap_request(connection, "uid", "SEARCH", None, *criteria,
                                   maximum=MAX_SEARCH_BYTES + MAX_CONTROL_BYTES,
                                   line_limit=MAX_SEARCH_BYTES + 64, deadline=deadline), "The server could not search this folder.")
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
    for identity in candidates[offset:]:
        if inspected >= 50 or len(matches) >= limit or time.monotonic() >= deadline:
            break
        inspected += 1
        preview = header(connection, identity, deadline=deadline)
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


def read(connection, folder: str, uid: str, validity: str, *, deadline=None) -> dict:
    """Read one message, refusing an excessive payload before fetching its body."""
    deadline = time.monotonic() + TIMEOUT if deadline is None else deadline
    if select_folder(connection, folder, deadline=deadline) != validity:
        raise MailError("reference_expired", "The folder identity changed. Search again.")
    preview = header(connection, uid, deadline=deadline)
    if preview["size"] is None or preview["size"] > MAX_MESSAGE_BYTES:
        raise MailError("message_too_large", "This message exceeds the 10 MiB read limit.")
    _, raw = _fetch(connection, uid, f"(UID BODY.PEEK[]<0.{MAX_MESSAGE_BYTES}>)", MAX_MESSAGE_BYTES, deadline=deadline)
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


def check_read(settings: dict, password: str, folder: str, *, access_token: str | None = None) -> dict:
    """Verify a real bounded read, distinguishing an empty folder from a read sample."""
    with imap_connection(settings, password, access_token=access_token) as connection:
        deadline = time.monotonic() + TIMEOUT
        available = folders(connection, deadline=deadline)
        return {**check_connection_read(connection, folder, deadline=deadline), "folders": available}


def check_connection_read(connection, folder: str, *, deadline=None) -> dict:
    """Check one bounded non-marking read on an already authenticated IMAP connection."""
    deadline = time.monotonic() + TIMEOUT if deadline is None else deadline
    validity = select_folder(connection, folder, deadline=deadline)
    counts = connection.response("EXISTS")[1]
    if not counts or not counts[0] or not counts[0].isdigit():
        raise MailError("invalid_server_response", "The server did not return a valid folder message count.")
    if int(counts[0]) == 0:
        return {"connection": "passed", "authentication": "passed", "folder": "passed",
                "message_read": "no_sample",
                "message": "Connected and authenticated. The folder is empty; no message read was tested."}
    rows = _ok(_imap_request(connection, "fetch", counts[0].decode("ascii"), "(UID)", deadline=deadline), "The last message could not be inspected.")
    identity_match = next((match for row in rows if isinstance(row, bytes)
                           if (match := re.search(rb"UID (\d+)", row))), None)
    if identity_match is None:
        raise MailError("invalid_server_response", "The server returned an invalid message identifier.")
    read(connection, folder, identity_match[1].decode("ascii"), validity, deadline=deadline)
    return {"connection": "passed", "authentication": "passed", "folder": "passed", "message_read": "passed",
            "message": "Connected, authenticated and read one message without changing its read status."}


@contextlib.contextmanager
def smtp_connection(settings: dict, password: str, *, access_token: str | None = None, progress=None):
    """Connect over verified TLS and report each completed connection and sign-in stage."""
    connection = None
    try:
        settings = _settings(settings, "smtp")
        if progress:
            progress("connection", "started")
        context = ssl.create_default_context()
        if settings["tls"] == "tls":
            connection = _SMTPSSL(settings["host"], settings["port"], timeout=TIMEOUT, context=context)
        else:
            connection = _SMTP(settings["host"], settings["port"], timeout=TIMEOUT)
            connection.ehlo()
            connection.starttls(context=context)
        if progress:
            progress("connection", "passed")
            progress("authentication", "started")
        connection.ehlo()
        if access_token is not None:
            response = f"user={settings['username']}\x01auth=Bearer {access_token}\x01\x01"
            connection.auth("XOAUTH2", lambda challenge=None: response if challenge is None else "", initial_response_ok=True)
        else:
            connection.login(settings["username"], password)
        if progress:
            progress("authentication", "passed")
        yield connection
    except smtplib.SMTPAuthenticationError as error:
        raise MailError("authentication_failed", "Your mail provider did not let this app sign in to send mail. Check the sign-in help under Email provider.",
                        server_response=_connection_detail("SMTP", error, settings, password, access_token)) from error
    except ssl.SSLError as error:
        raise MailError("tls_failed", "The SMTP TLS connection or certificate could not be verified.",
                        server_response=_connection_detail("SMTP", error, settings, password, access_token)) from error
    except (OSError, smtplib.SMTPException) as error:
        raise MailError("smtp_failed", "Your mail provider could not finish this sending request.",
                        server_response=_connection_detail("SMTP", error, settings, password, access_token)) from error
    finally:
        if connection is not None:
            try:
                connection.close()
            except OSError:
                pass


def submit(settings: dict, password: str, sender: str, recipients: list[str], content: bytes, before_data, *, access_token: str | None = None) -> str:
    """Submit once; reject any recipient failure before transmitting message DATA."""
    with smtp_connection(settings, password, access_token=access_token) as connection:
        if connection.mail(sender)[0] != 250:
            return "rejected"
        for recipient in recipients:
            if connection.rcpt(recipient)[0] not in (250, 251):
                connection.rset()
                return "rejected"
        before_data()
        try:
            code, _ = connection.data(content)
            if code == 250:
                return "accepted_by_server"
            return "rejected" if 400 <= code <= 599 else "outcome_unknown"
        except smtplib.SMTPDataError:
            return "rejected"
        except (OSError, smtplib.SMTPException):
            # The server may have accepted DATA even when its final response was lost.
            return "outcome_unknown"
