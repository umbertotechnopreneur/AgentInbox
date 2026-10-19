"""Bounded POP3 mailbox reads using stable UIDL identities and verified TLS."""

import base64
import contextlib
import datetime as dt
import email
import email.policy
import email.utils
import poplib
import re
import ssl
import time

from .config import MAX_MESSAGE_BYTES, text
from .errors import MailError
from .mail import _PlainHtml

TIMEOUT = 20
MAX_UIDL_BYTES = 1024 * 1024
MAX_HEADER_BYTES = 16 * 1024
MAX_CANDIDATES = 300
UID_VALIDITY = "pop3-uidl-v1"
DATE_BASIS = "message_date_header"
SEARCH_SCOPE = "subject_from_to_headers"
_UID = re.compile(rb"[!-~]{1,70}")


class _BoundedReads:
    """Bound socket reads before poplib accumulates a multiline response."""

    def __init__(self, *args, **kwargs):
        self._wire_buffer = bytearray()
        self._set_response_limits(4096, 32, time.monotonic() + TIMEOUT)
        try:
            super().__init__(*args, **kwargs)
        except BaseException:
            # POP3.__init__ may fail after allocating a socket or file object.
            for resource in (getattr(self, "file", None), getattr(self, "sock", None)):
                if resource is not None:
                    try:
                        resource.close()
                    except OSError:
                        pass
            raise

    def _set_response_limits(self, maximum, lines, deadline):
        if self._wire_buffer:
            raise MailError("invalid_server_response", "The POP3 server sent unexpected data between commands.")
        # Reserve framing and at most one escaped dot per payload line.
        self._bytes_left = maximum + lines + 4099
        self._lines_left = lines + 2
        self._line_limit = min(maximum + 2, 65_536)
        self._deadline = deadline
        self._payload_limit = maximum
        self._multiline = False
        self._payload_active = False

    def _getline(self):
        while True:
            remaining_time = self._deadline - time.monotonic()
            if remaining_time <= 0:
                raise MailError("operation_timeout", "The bounded POP3 read timed out. Try a smaller search.")
            boundary = self._wire_buffer.find(b"\n")
            if boundary >= 0:
                count = boundary + 1
                if count > self._line_limit or count > self._bytes_left or self._lines_left <= 0:
                    raise MailError("response_too_large", "The POP3 response exceeds its byte or line limit.")
                if boundary == 0 or self._wire_buffer[boundary - 1] != 13:
                    raise MailError("invalid_server_response", "The POP3 server returned malformed line endings.")
                if self._payload_active and not (count == 3 and self._wire_buffer[0] == 46):
                    payload_size = count - (1 if self._wire_buffer[:2] == b".." else 0)
                    if payload_size > self._payload_left:
                        raise MailError("response_too_large", "The POP3 payload exceeds the permitted size.")
                    self._payload_left -= payload_size
                line = bytes(self._wire_buffer[:boundary - 1])
                del self._wire_buffer[:count]
                self._bytes_left -= count
                self._lines_left -= 1
                return line, count
            if len(self._wire_buffer) >= min(self._bytes_left, self._line_limit):
                raise MailError("response_too_large", "The POP3 response exceeds its byte or line limit.")
            self.sock.settimeout(min(TIMEOUT, remaining_time))
            capacity = min(4096, self._bytes_left + 1 - len(self._wire_buffer),
                           self._line_limit + 1 - len(self._wire_buffer))
            chunk = self.sock.recv(capacity)
            if not chunk:
                raise MailError("connection_failed", "The POP3 server closed the connection before completing its response.")
            self._wire_buffer.extend(chunk)

    def _getresp(self):
        response = super()._getresp()
        if response != b"+OK" and not response.startswith(b"+OK "):
            raise MailError("invalid_server_response", "The POP3 server returned an invalid status response.")
        if self._multiline:
            self._payload_active = True
        return response

    def _getlongresp(self):
        self._multiline = True
        self._payload_active = False
        self._payload_left = self._payload_limit
        try:
            return super()._getlongresp()
        finally:
            self._multiline = False
            self._payload_active = False

    def _shortcmd(self, command):
        response = super()._shortcmd(command)
        # In particular, never carry plaintext bytes across the STLS handshake.
        if self._wire_buffer:
            raise MailError("invalid_server_response", "The POP3 server sent unexpected data after a command.")
        return response


class _POP3(_BoundedReads, poplib.POP3):
    """POP3 transport with bounded framing and explicit response deadlines."""


class _POP3SSL(_BoundedReads, poplib.POP3_SSL):
    """TLS POP3 transport with the same bounded framing contract."""


def _request(connection, method, *arguments, maximum=4096, lines=32, deadline=None, **keywords):
    connection._set_response_limits(maximum, lines, deadline if deadline is not None else time.monotonic() + TIMEOUT)
    return getattr(connection, method)(*arguments, **keywords)


def _xoauth2(connection, username, access_token):
    """Complete one bounded token exchange without retrying with a password."""
    if not isinstance(access_token, str) or not access_token or len(access_token) > 65_536 or any(not 33 <= ord(c) <= 126 for c in access_token):
        raise MailError("invalid_credentials", "The Microsoft access token is invalid. Connect the account again.")
    payload = base64.b64encode(f"user={username}\x01auth=Bearer {access_token}\x01\x01".encode("utf-8"))
    connection._set_response_limits(16_384, 8, time.monotonic() + TIMEOUT)
    connection._putcmd("AUTH XOAUTH2")
    challenge, _ = connection._getline()
    if challenge not in {b"+", b"+ "} or connection._wire_buffer:
        raise MailError("authentication_failed", "The POP3 server did not accept Microsoft token authentication.")
    connection._putline(payload)
    response, _ = connection._getline()
    if response == b"+OK" or response.startswith(b"+OK "):
        if connection._wire_buffer:
            raise MailError("invalid_server_response", "The POP3 server sent unexpected data after authentication.")
        return
    if response == b"+" or response.startswith(b"+ "):
        # A SASL error challenge is acknowledged once; its contents are never logged.
        connection._putline(b"")
        connection._getline()
    raise MailError("authentication_failed", "Microsoft token authentication failed. Connect the account again; password fallback is disabled.")


@contextlib.contextmanager
def pop_connection(settings: dict, password: str | None = None, *, access_token: str | None = None):
    """Use the explicitly selected authentication after verified TLS or STLS."""
    connection = None
    try:
        if settings["tls"] not in {"tls", "starttls"}:
            raise MailError("invalid_configuration", "POP3 requires TLS or mandatory STARTTLS.")
        credentials = (settings["username"], password) if access_token is None else (settings["username"],)
        for credential in credentials:
            if not isinstance(credential, str) or not credential or any(ord(c) < 32 or ord(c) == 127 for c in credential):
                raise MailError("invalid_credentials", "Enter valid POP3 credentials in local setup.")
        context = ssl.create_default_context()
        if settings["tls"] == "tls":
            connection = _POP3SSL(settings["host"], settings["port"], timeout=TIMEOUT, context=context)
        else:
            connection = _POP3(settings["host"], settings["port"], timeout=TIMEOUT)
            try:
                _request(connection, "stls", context=context)
            except poplib.error_proto as error:
                raise MailError("tls_required", "The POP3 server did not establish required STLS. Credentials were not sent.") from error
        if access_token is not None:
            _xoauth2(connection, settings["username"], access_token)
        else:
            try:
                _request(connection, "user", settings["username"])
                _request(connection, "pass_", password)
            except poplib.error_proto as error:
                raise MailError("authentication_failed", "POP3 sign-in failed. Check its username, password or app password and provider settings.") from error
        yield connection
    except ssl.SSLError as error:
        raise MailError("tls_failed", "The POP3 TLS connection or certificate could not be verified.") from error
    except OSError as error:
        raise MailError("connection_failed", "The POP3 server could not complete the connection. Check the server, port and TLS mode.") from error
    except poplib.error_proto as error:
        raise MailError("mail_operation_failed", "The POP3 server rejected the requested read.") from error
    finally:
        if connection is not None:
            # Closing without an UPDATE transaction keeps this transport read-only.
            try:
                connection.close()
            except OSError:
                pass


def _folder(folder):
    if folder != "INBOX":
        raise MailError("unsupported_folder", "POP3 exposes only INBOX. Use IMAP for server folders.")


def _uidl(connection, deadline):
    try:
        _, rows, octets = _request(connection, "uidl", maximum=MAX_UIDL_BYTES, lines=100_000, deadline=deadline)
    except poplib.error_proto as error:
        raise MailError("uidl_required", "This POP3 server must support UIDL so message references remain stable.") from error
    if octets > MAX_UIDL_BYTES:
        raise MailError("mailbox_too_large", "The POP3 UIDL listing exceeds the 1 MiB limit. Use a smaller mailbox or IMAP.")
    identities, numbers = {}, set()
    for row in rows:
        fields = row.split()
        if len(fields) != 2 or not re.fullmatch(rb"[1-9][0-9]{0,9}", fields[0]) or not _UID.fullmatch(fields[1]):
            raise MailError("invalid_server_response", "The POP3 server returned an invalid UIDL identity.")
        number, identity = int(fields[0]), fields[1].decode("ascii")
        if number in numbers or identity in identities:
            raise MailError("invalid_server_response", "The POP3 server returned ambiguous UIDL identities.")
        identities[identity] = number
        numbers.add(number)
    return identities


def _raw_response(response, maximum):
    _, lines, octets = response
    if octets > maximum:
        raise MailError("response_too_large", "The POP3 payload exceeds the permitted size.")
    raw = b"\r\n".join(lines) + (b"\r\n" if lines else b"")
    if len(raw) > maximum or len(raw) != octets:
        raise MailError("invalid_server_response", "The POP3 server returned an incomplete bounded payload.")
    return raw


def _message_date(message):
    try:
        value = email.utils.parsedate_to_datetime(str(message.get("Date", "")))
        if value.tzinfo is None or value.utcoffset() is None:
            return None
        return value.astimezone(dt.timezone.utc).isoformat()
    except (TypeError, ValueError, OverflowError):
        return None


def _header(connection, number, identity, deadline):
    try:
        response = _request(connection, "top", number, 0, maximum=MAX_HEADER_BYTES, lines=2048, deadline=deadline)
    except poplib.error_proto as error:
        raise MailError("top_required", "This POP3 server must support TOP 0 for bounded header searches. Full-message fallback is disabled.") from error
    raw = _raw_response(response, MAX_HEADER_BYTES)
    separator = raw.find(b"\r\n\r\n")
    if separator >= 0 and raw[separator + 4:]:
        raise MailError("invalid_server_response", "The POP3 server returned message content for a header-only request.")
    message = email.message_from_bytes(raw, policy=email.policy.default)
    fields = {name: str(message.get(name, "")) for name in ("Subject", "From", "To")}
    preview = {"uid": identity, "uid_validity": UID_VALIDITY, "folder": "INBOX",
               "subject": fields["Subject"][:500], "from": fields["From"][:500], "to": fields["To"][:1000],
               "message_date": _message_date(message), "date_basis": DATE_BASIS,
               "unread": None, "size": None, "search_scope": SEARCH_SCOPE,
               "content_is_untrusted": True}
    return preview, "\n".join(fields.values()).casefold()


def search(connection, folder: str, start: dt.datetime, end: dt.datetime,
           query: str, unread: bool, limit: int, offset: int = 0,
           candidates: list[str] | None = None, expected_validity: str | None = None) -> dict:
    """Search at most 300 server-numbered candidates using header dates and text."""
    _folder(folder)
    if type(unread) is not bool or unread:
        raise MailError("unsupported_filter", "POP3 does not expose read or unread status. Disable the unread filter or use IMAP.")
    if type(limit) is not int or not 1 <= limit <= 20 or type(offset) is not int or offset < 0:
        raise MailError("invalid_limit", "Use a page size from 1 to 20 and a valid continuation offset.")
    query = text(query, "Search text", 128, allow_empty=True).casefold()
    if not all(isinstance(value, dt.datetime) and value.tzinfo is not None for value in (start, end)) or not dt.timedelta(0) < end - start <= dt.timedelta(days=31):
        raise MailError("invalid_date_range", "Choose a positive range of at most 31 days with explicit timezones.")
    if expected_validity is not None and expected_validity != UID_VALIDITY:
        raise MailError("reference_expired", "The POP3 reference is incompatible. Search again.")
    deadline = time.monotonic() + TIMEOUT
    identities = _uidl(connection, deadline)
    truncated = False
    if candidates is None:
        ordered = sorted(identities, key=identities.get, reverse=True)
        truncated = len(ordered) > MAX_CANDIDATES
        candidates = ordered[:MAX_CANDIDATES]
    elif not isinstance(candidates, list) or len(candidates) > MAX_CANDIDATES or any(not isinstance(uid, str) for uid in candidates):
        raise MailError("invalid_continuation", "The POP3 continuation has an invalid format. Search again.")
    if offset > len(candidates):
        raise MailError("invalid_continuation", "The POP3 continuation offset is invalid. Search again.")
    matches, inspected, undated = [], 0, 0
    for identity in candidates[offset:]:
        if inspected >= 50 or len(matches) >= limit or time.monotonic() >= deadline:
            break
        if identity not in identities:
            raise MailError("reference_expired", "A message in this POP3 search has been removed. Start a new search.")
        preview, searchable = _header(connection, identities[identity], identity, deadline)
        inspected += 1
        if preview["message_date"] is None:
            undated += 1
            continue
        when = dt.datetime.fromisoformat(preview["message_date"])
        if start <= when < end and (not query or query in searchable):
            matches.append(preview)
    next_offset = offset + inspected
    return {"messages": matches, "more": next_offset < len(candidates), "next_offset": next_offset,
            "candidate_limit_reached": truncated, "uid_validity": UID_VALIDITY, "candidates": candidates,
            "date_basis": DATE_BASIS, "search_scope": SEARCH_SCOPE,
            "candidate_order": "server_message_number_descending", "undated_messages_skipped": undated,
            "date_filter_incomplete": undated > 0}


def _size(connection, number, deadline):
    response = _request(connection, "list", number, deadline=deadline)
    fields = response.split()
    if len(fields) < 3 or fields[0] != b"+OK" or not fields[1].isdigit() or int(fields[1]) != number or not fields[2].isdigit():
        raise MailError("invalid_server_response", "The POP3 server returned an invalid message size.")
    size = int(fields[2])
    if size > MAX_MESSAGE_BYTES:
        raise MailError("message_too_large", "This message exceeds the 10 MiB read limit.")
    return size


def read(connection, folder: str, uid: str, validity: str) -> dict:
    """Resolve a UIDL in this session before reading a bounded complete message."""
    _folder(folder)
    if validity != UID_VALIDITY or not isinstance(uid, str) or not uid.isascii() or not _UID.fullmatch(uid.encode("ascii")):
        raise MailError("reference_expired", "The POP3 message reference is invalid. Search again.")
    deadline = time.monotonic() + TIMEOUT
    identities = _uidl(connection, deadline)
    if uid not in identities:
        raise MailError("message_unavailable", "The POP3 message was removed or is no longer available.")
    number = identities[uid]
    size = _size(connection, number, deadline)
    preview, _ = _header(connection, number, uid, deadline)
    response = _request(connection, "retr", number, maximum=MAX_MESSAGE_BYTES, lines=200_000, deadline=deadline)
    raw = _raw_response(response, MAX_MESSAGE_BYTES)
    if len(raw) != size:
        raise MailError("incomplete_message", "The POP3 server did not return the complete advertised message.")
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
    attachments = []
    for item in message.iter_attachments():
        if len(attachments) == 100:
            break
        attachments.append({"name": str(item.get_filename() or "attachment")[:255],
                            "content_type": item.get_content_type()})
    return {**preview, "size": size, "body": content[:30_000], "body_truncated": len(content) > 30_000,
            "attachments": attachments, "content_is_untrusted": True}


def check_read(settings: dict, password: str | None = None, folder: str = "INBOX", *, access_token: str | None = None) -> dict:
    """Check real POP3 authentication and one bounded read without saving mail."""
    _folder(folder)
    with pop_connection(settings, password, access_token=access_token) as connection:
        identities = _uidl(connection, time.monotonic() + TIMEOUT)
        result = {"connection": "passed", "authentication": "passed", "folder": "passed",
                  "folders": ["INBOX"], "protocol": "pop3", "date_basis": DATE_BASIS,
                  "search_scope": SEARCH_SCOPE, "unread_supported": False}
        if not identities:
            return {**result, "message_read": "no_sample",
                    "message": "Connected and authenticated. The mailbox is empty; no message read was tested."}
        identity = max(identities, key=identities.get)
        read(connection, "INBOX", identity, UID_VALIDITY)
        return {**result, "message_read": "passed",
                "message": "Connected, authenticated and read one message. POP3 does not expose read or unread status."}
