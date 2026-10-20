"""Synthetic POP3 identity, framing, filtering and authentication scenarios."""

import base64
import datetime as dt
import poplib
import unittest
from unittest import mock

from agentinbox import pop
from agentinbox.errors import MailError


START = dt.datetime(2026, 10, 1, tzinfo=dt.timezone.utc)
END = dt.datetime(2026, 10, 19, tzinfo=dt.timezone.utc)


def header_bytes(subject="Synthetic header", dated=True):
    fields = [f"Subject: {subject}", "From: sender@example.test", "To: reader@example.test"]
    if dated:
        fields.append("Date: Sun, 18 Oct 2026 12:00:00 +0000")
    return ("\r\n".join(fields) + "\r\n\r\n").encode()


class ScriptedPop:
    """Provide protocol-shaped replies without any socket or stored mailbox."""

    def __init__(self, identities=None, headers=None, bodies=None):
        self.identities = identities if identities is not None else {1: "stable-message"}
        self.headers = headers or {}
        self.bodies = bodies or {}
        self.operations = []
        self.top_error = None
        self.advertised_size = None

    def _set_response_limits(self, maximum, lines, deadline):
        self.operations.append(("limit", maximum, lines))

    def uidl(self):
        self.operations.append(("uidl",))
        rows = [f"{number} {identity}".encode() for number, identity in self.identities.items()]
        return b"+OK", rows, sum(len(row) + 2 for row in rows)

    @staticmethod
    def _response(raw):
        return b"+OK", raw.split(b"\r\n")[:-1], len(raw)

    def _header(self, number):
        return self.headers.get(self.identities[number], header_bytes())

    def _body(self, number):
        return self.bodies.get(self.identities[number], self._header(number) + b"Synthetic body.\r\n")

    def top(self, number, count):
        self.operations.append(("top", number, count))
        if self.top_error is not None:
            raise self.top_error
        return self._response(self._header(number))

    def list(self, number):
        self.operations.append(("list", number))
        size = self.advertised_size if self.advertised_size is not None else len(self._body(number))
        return f"+OK {number} {size}".encode()

    def retr(self, number):
        self.operations.append(("retr", number))
        return self._response(self._body(number))


class PopReadTests(unittest.TestCase):
    """Check the differences between POP message dates and IMAP receive state."""

    def test_uidl_reference_survives_changed_session_message_number(self):
        first = ScriptedPop({1: "stable-message"})
        page = pop.search(first, "INBOX", START, END, "", False, 10)
        reference = page["messages"][0]
        later = ScriptedPop({7: "stable-message", 1: "different-message"})

        result = pop.read(later, "INBOX", reference["uid"], reference["uid_validity"])

        self.assertIn(("retr", 7), later.operations)
        self.assertNotIn(("retr", 1), later.operations)
        self.assertIn("Synthetic body.", result["body"])
        self.assertEqual("message_date_header", result["date_basis"])
        self.assertEqual("2026-10-18T12:00:00+00:00", result["message_date"])
        self.assertIsNone(result["unread"])
        self.assertNotIn("received_at", result)

    def test_missing_uidl_does_not_reuse_the_old_session_number(self):
        changed = ScriptedPop({1: "replacement-message"})
        with self.assertRaises(MailError) as failure:
            pop.read(changed, "INBOX", "removed-message", pop.UID_VALIDITY)
        self.assertEqual("message_unavailable", failure.exception.code)
        self.assertFalse(any(operation[0] == "retr" for operation in changed.operations))

    def test_search_does_not_download_bodies_to_find_text(self):
        connection = ScriptedPop(bodies={"stable-message": header_bytes() + b"body-only-needle\r\n"})
        page = pop.search(connection, "INBOX", START, END, "body-only-needle", False, 10)
        self.assertEqual([], page["messages"])
        self.assertEqual("subject_from_to_headers", page["search_scope"])
        self.assertFalse(any(operation[0] == "retr" for operation in connection.operations))

    def test_missing_date_is_reported_as_incomplete_filtering(self):
        connection = ScriptedPop(headers={"stable-message": header_bytes(dated=False)})
        page = pop.search(connection, "INBOX", START, END, "", False, 10)
        self.assertEqual([], page["messages"])
        self.assertEqual(1, page["undated_messages_skipped"])
        self.assertTrue(page["date_filter_incomplete"])

    def test_unread_filter_fails_before_reading_the_mailbox(self):
        connection = ScriptedPop()
        with self.assertRaises(MailError) as failure:
            pop.search(connection, "INBOX", START, END, "", True, 10)
        self.assertEqual("unsupported_filter", failure.exception.code)
        self.assertEqual([], connection.operations)

    def test_unsupported_top_has_no_full_message_fallback(self):
        connection = ScriptedPop()
        connection.top_error = poplib.error_proto("Synthetic TOP rejection.")
        with self.assertRaises(MailError) as failure:
            pop.search(connection, "INBOX", START, END, "", False, 10)
        self.assertEqual("top_required", failure.exception.code)
        self.assertFalse(any(operation[0] == "retr" for operation in connection.operations))

    def test_message_size_is_checked_before_retrieval(self):
        connection = ScriptedPop()
        connection.advertised_size = pop.MAX_MESSAGE_BYTES + 1
        with self.assertRaises(MailError) as failure:
            pop.read(connection, "INBOX", "stable-message", pop.UID_VALIDITY)
        self.assertEqual("message_too_large", failure.exception.code)
        self.assertFalse(any(operation[0] == "retr" for operation in connection.operations))

    def test_candidate_and_header_work_limits_have_a_continuation(self):
        connection = ScriptedPop({number: f"message-{number}" for number in range(1, 302)})
        page = pop.search(connection, "INBOX", START, END, "absent-header-text", False, 20)
        self.assertTrue(page["candidate_limit_reached"])
        self.assertEqual(300, len(page["candidates"]))
        self.assertEqual("message-301", page["candidates"][0])
        self.assertEqual(50, sum(operation[0] == "top" for operation in connection.operations))
        self.assertEqual(50, page["next_offset"])
        self.assertTrue(page["more"])

    def test_duplicate_uidl_cannot_select_an_arbitrary_message(self):
        connection = ScriptedPop({1: "duplicate", 2: "duplicate"})
        with self.assertRaises(MailError) as failure:
            pop.search(connection, "INBOX", START, END, "", False, 10)
        self.assertEqual("invalid_server_response", failure.exception.code)
        self.assertFalse(any(operation[0] == "top" for operation in connection.operations))


class MemorySocket:
    """Feed bounded chunks to the real framing code without opening a socket."""

    def __init__(self, payload):
        self.remaining = payload
        self.bytes_read = 0

    def settimeout(self, timeout):
        self.timeout = timeout

    def recv(self, capacity):
        result, self.remaining = self.remaining[:capacity], self.remaining[capacity:]
        self.bytes_read += len(result)
        return result


class PopFramingTests(unittest.TestCase):
    """Exercise limits inside the framing layer before multiline accumulation."""

    @staticmethod
    def reader(payload, maximum=16, lines=1000):
        connection = pop._POP3.__new__(pop._POP3)
        connection._wire_buffer = bytearray()
        connection._debugging = 0
        connection.sock = MemorySocket(payload)
        connection._set_response_limits(maximum, lines, 100)
        return connection

    def test_oversized_multiline_response_stops_before_consuming_the_stream(self):
        payload = b"+OK\r\n" + b"small line\r\n" * 1000 + b".\r\n"
        connection = self.reader(payload)
        with mock.patch.object(pop.time, "monotonic", return_value=0):
            with self.assertRaises(MailError) as failure:
                connection._getlongresp()
        self.assertEqual("response_too_large", failure.exception.code)
        self.assertLess(connection.sock.bytes_read, 100)

    def test_dot_stuffing_does_not_charge_an_extra_payload_byte(self):
        connection = self.reader(b"+OK\r\n..\r\n.\r\n", maximum=3)
        with mock.patch.object(pop.time, "monotonic", return_value=0):
            response = connection._getlongresp()
        self.assertEqual(b".\r\n", pop._raw_response(response, 3))

    def test_expired_deadline_does_not_start_another_read(self):
        connection = self.reader(b"+OK\r\n")
        with mock.patch.object(pop.time, "monotonic", return_value=101):
            with self.assertRaises(MailError) as failure:
                connection._getline()
        self.assertEqual("operation_timeout", failure.exception.code)
        self.assertEqual(0, connection.sock.bytes_read)


class AuthenticationPop:
    """Record authentication choices and return a bounded scripted exchange."""

    def __init__(self, responses=(), stls_error=None):
        self.responses = iter(responses)
        self.operations = []
        self._wire_buffer = bytearray()
        self.stls_error = stls_error

    def _set_response_limits(self, maximum, lines, deadline):
        self.operations.append(("limit", maximum, lines))

    def _putcmd(self, command):
        self.operations.append(("command", command))

    def _putline(self, line):
        self.operations.append(("line", line))

    def _getline(self):
        response = next(self.responses)
        return response, len(response) + 2

    def user(self, value):
        self.operations.append(("user", value))

    def pass_(self, value):
        self.operations.append(("password", value))

    def stls(self, *, context):
        self.operations.append(("stls",))
        if self.stls_error is not None:
            raise self.stls_error

    def close(self):
        self.operations.append(("close",))


class PopAuthenticationTests(unittest.TestCase):
    """Keep password and explicitly requested token authentication separate."""

    def setUp(self):
        self.settings = {"host": "pop.example.test", "port": 995, "tls": "tls", "username": "reader@example.test"}

    def test_default_authentication_uses_password_only(self):
        connection = AuthenticationPop()
        with mock.patch.object(pop, "_POP3SSL", return_value=connection), mock.patch.object(pop.ssl, "create_default_context", return_value=object()):
            with pop.pop_connection(self.settings, "synthetic-password"):
                pass
        self.assertIn(("user", "reader@example.test"), connection.operations)
        self.assertIn(("password", "synthetic-password"), connection.operations)
        self.assertFalse(any(operation[0] == "command" for operation in connection.operations))

    def test_stls_failure_does_not_transmit_credentials(self):
        connection = AuthenticationPop(stls_error=poplib.error_proto("Synthetic unavailable STLS."))
        settings = {**self.settings, "tls": "starttls", "port": 110}
        with mock.patch.object(pop, "_POP3", return_value=connection), mock.patch.object(pop.ssl, "create_default_context", return_value=object()):
            with self.assertRaises(MailError) as failure:
                with pop.pop_connection(settings, "synthetic-password"):
                    self.fail("A rejected upgrade cannot yield a connection.")
        self.assertEqual("tls_required", failure.exception.code)
        self.assertFalse(any(operation[0] in {"user", "password"} for operation in connection.operations))

    def test_explicit_token_uses_two_step_xoauth2_without_password_fallback(self):
        connection = AuthenticationPop([b"+", b"+OK authenticated"])
        with mock.patch.object(pop, "_POP3SSL", return_value=connection), mock.patch.object(pop.ssl, "create_default_context", return_value=object()):
            with pop.pop_connection(self.settings, access_token="synthetic-access-token"):
                pass
        self.assertIn(("command", "AUTH XOAUTH2"), connection.operations)
        encoded = next(entry[1] for entry in connection.operations if entry[0] == "line")
        self.assertEqual(b"user=reader@example.test\x01auth=Bearer synthetic-access-token\x01\x01", base64.b64decode(encoded))
        self.assertFalse(any(operation[0] in {"user", "password"} for operation in connection.operations))

    def test_token_error_challenge_is_acknowledged_once_without_retry(self):
        connection = AuthenticationPop([b"+", b"+ synthetic-error-challenge", b"-ERR rejected"])
        with mock.patch.object(pop, "_POP3SSL", return_value=connection), mock.patch.object(pop.ssl, "create_default_context", return_value=object()):
            with self.assertRaises(MailError) as failure:
                with pop.pop_connection(self.settings, "unused-password", access_token="synthetic-access-token"):
                    self.fail("Rejected tokens cannot yield a connection.")
        self.assertEqual("authentication_failed", failure.exception.code)
        self.assertEqual(2, sum(operation[0] == "line" for operation in connection.operations))
        self.assertIn(("line", b""), connection.operations)
        self.assertFalse(any(operation[0] in {"user", "password"} for operation in connection.operations))
        self.assertNotIn("synthetic-access-token", str(failure.exception))
