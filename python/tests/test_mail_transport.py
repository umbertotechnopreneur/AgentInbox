"""Synthetic IMAP wire limits and SMTP outcomes; no socket or native API calls."""

import contextlib
import datetime as dt
import re
import smtplib
import unittest
from unittest import mock

from agentinbox import mail
from agentinbox.errors import MailError

from tests._support import ScriptedSmtp


class MemorySocket:
    """Expose a finite in-memory stream with an optional advancing fake clock."""

    def __init__(self, payload, *, chunk_size=4096, seconds_per_read=0):
        self.remaining = payload
        self.chunk_size = chunk_size
        self.bytes_read = 0
        self.clock = 0
        self.seconds_per_read = seconds_per_read

    def recv(self, capacity):
        count = min(capacity, self.chunk_size)
        result, self.remaining = self.remaining[:count], self.remaining[count:]
        self.bytes_read += len(result)
        self.clock += self.seconds_per_read
        return result

    def settimeout(self, value):
        self.timeout = value


def imap_reader(payload, *, maximum=65_536, literal=0, lines=256, line_limit=16_384,
                chunk_size=4096, seconds_per_read=0):
    """Initialize only the parser fields needed for in-memory wire responses."""
    connection = mail._IMAP4.__new__(mail._IMAP4)
    connection._wire_buffer = bytearray()
    connection.sock = MemorySocket(payload, chunk_size=chunk_size, seconds_per_read=seconds_per_read)
    connection._set_response_limits(maximum, literal=literal, lines=lines, deadline=20, line_limit=line_limit)
    connection._mode_ascii()
    connection.debug = 0
    connection._cmd_log_len, connection._cmd_log_idx, connection._cmd_log = 10, 0, {}
    connection.tagre = re.compile(rb"(?P<tag>A\d+) (?P<type>[A-Z]+) (?P<data>.*)")
    connection.tagged_commands, connection.untagged_responses = {}, {}
    return connection


class ImapWireTests(unittest.TestCase):
    """Reject oversized or slow responses before imaplib accumulates them."""

    def test_server_declared_header_and_body_literals_are_rejected_before_body_read(self):
        for limit in (mail.MAX_HEADER_BYTES, mail.MAX_MESSAGE_BYTES):
            with self.subTest(limit=limit):
                heading = f"* 1 FETCH (UID 7 BODY[] {{{limit + 1}}}\r\n".encode()
                connection = imap_reader(heading + b"unrequested body", literal=limit,
                                         maximum=limit + mail.MAX_CONTROL_BYTES, chunk_size=1)
                with mock.patch.object(mail.time, "monotonic", return_value=0):
                    with self.assertRaises(MailError) as failure:
                        connection._get_response()
                self.assertEqual("response_too_large", failure.exception.code)
                self.assertEqual(len(heading), connection.sock.bytes_read)

    def test_aggregate_literal_budget_is_not_reset_between_literals(self):
        connection = imap_reader(b"abcdefghijkl", maximum=10, literal=8)
        with mock.patch.object(mail.time, "monotonic", return_value=0):
            self.assertEqual(b"abcdef", connection.read(6))
            with self.assertRaises(MailError) as failure:
                connection.read(6)
        self.assertEqual("response_too_large", failure.exception.code)
        self.assertEqual(6, connection.sock.bytes_read)

    def test_list_rows_stop_at_the_shared_byte_budget(self):
        row = b'* LIST () "/" "INBOX"\r\n'
        connection = imap_reader(row * 1000, maximum=len(row) * 2, chunk_size=1)
        with mock.patch.object(mail.time, "monotonic", return_value=0):
            connection._get_response()
            connection._get_response()
            with self.assertRaises(MailError) as failure:
                connection._get_response()
        self.assertEqual("response_too_large", failure.exception.code)
        self.assertEqual(2, len(connection.untagged_responses["LIST"]))
        self.assertEqual(len(row) * 2, connection.sock.bytes_read)

    def test_search_line_is_bounded_before_split_or_result_creation(self):
        connection = imap_reader(b"* SEARCH " + b"7 " * 1000 + b"\r\n", line_limit=32)
        with mock.patch.object(mail.time, "monotonic", return_value=0):
            with self.assertRaises(MailError) as failure:
                connection._get_response()
        self.assertEqual("response_too_large", failure.exception.code)
        self.assertLessEqual(connection.sock.bytes_read, 32)

    def test_continuing_partial_data_cannot_extend_the_deadline(self):
        connection = imap_reader(b"* SEARCH 7\r\n", chunk_size=1, seconds_per_read=10)
        with mock.patch.object(mail.time, "monotonic", side_effect=lambda: connection.sock.clock):
            with self.assertRaises(MailError) as failure:
                connection.readline()
        self.assertEqual("operation_timeout", failure.exception.code)
        self.assertEqual(2, connection.sock.bytes_read)

    def test_search_passes_one_deadline_to_select_search_and_header_fetch(self):
        class ScriptedImap:
            def __init__(self):
                self.limits = []

            def _set_response_limits(self, maximum, **options):
                self.limits.append((maximum, options))

            def select(self, folder, *, readonly):
                return "OK", [b"1"]

            def response(self, name):
                return name, [b"42"]

            def uid(self, operation, *arguments):
                if operation == "SEARCH":
                    return "OK", [b"7"]
                metadata = b'1 (UID 7 FLAGS () INTERNALDATE "18-Oct-2026 12:00:00 +0000" RFC822.SIZE 60'
                return "OK", [(metadata, b"Subject: Synthetic\r\n\r\n"), b")"]

        connection = ScriptedImap()
        start = dt.datetime(2026, 10, 1, tzinfo=dt.timezone.utc)
        end = dt.datetime(2026, 10, 19, tzinfo=dt.timezone.utc)
        with mock.patch.object(mail.time, "monotonic", return_value=0):
            result = mail.search(connection, "INBOX", start, end, "", False, 10)
        self.assertEqual("7", result["messages"][0]["uid"])
        self.assertEqual([20, 20, 20], [options["deadline"] for _, options in connection.limits])
        self.assertEqual(mail.MAX_HEADER_BYTES, connection.limits[-1][1]["literal"])


class SmtpReplyTests(unittest.TestCase):
    """Distinguish confirmed server decisions from lost or malformed outcomes."""

    @staticmethod
    def reader(payload):
        connection = mail._SMTP.__new__(mail._SMTP)
        connection._wire_buffer = bytearray()
        connection.sock = MemorySocket(payload, chunk_size=1)
        return connection

    def test_code_only_replies_remain_valid_without_optional_text(self):
        for payload, code in ((b"250\r\n", 250), (b"334\r\n", 334), (b"250 \r\n", 250)):
            with self.subTest(payload=payload), mock.patch.object(mail.time, "monotonic", return_value=0):
                self.assertEqual((code, b""), self.reader(payload).getreply())

    def test_multiline_reply_has_a_total_line_limit(self):
        connection = self.reader(b"250-continuation\r\n" * 1000 + b"250 done\r\n")
        with mock.patch.object(mail.time, "monotonic", return_value=0):
            with self.assertRaises(smtplib.SMTPServerDisconnected):
                connection.getreply()
        self.assertLess(connection.sock.bytes_read, 2000)

    def test_malformed_final_reply_does_not_become_a_rejection(self):
        connection = self.reader(b"not a valid SMTP reply\r\n")
        with mock.patch.object(mail.time, "monotonic", return_value=0):
            with self.assertRaises(smtplib.SMTPServerDisconnected):
                connection.getreply()

    def test_changed_multiline_status_is_not_a_success(self):
        connection = self.reader(b"550-rejected\r\n250 accepted\r\n")
        with mock.patch.object(mail.time, "monotonic", return_value=0):
            with self.assertRaises(smtplib.SMTPServerDisconnected):
                connection.getreply()

    def test_only_explicit_negative_final_data_replies_are_rejected(self):
        outcomes = {250: "accepted_by_server", 450: "rejected", 550: "rejected",
                    -1: "outcome_unknown", 354: "outcome_unknown", 251: "outcome_unknown"}
        for code, expected in outcomes.items():
            with self.subTest(code=code):
                connection = ScriptedSmtp()
                connection.data = mock.Mock(return_value=(code, b"synthetic reply"))
                with mock.patch.object(mail, "smtp_connection", return_value=contextlib.nullcontext(connection)):
                    outcome = mail.submit({}, "synthetic", "sender@example.test",
                                          ["reader@example.test"], b"synthetic content", lambda: None)
                self.assertEqual(expected, outcome)
                connection.data.assert_called_once()


class TransportSettingsTests(unittest.TestCase):
    def test_starttls_rejects_buffered_plaintext_before_tls_upgrade(self):
        imap = imap_reader(b"")
        imap._wire_buffer.extend(b"plaintext after upgrade reply\r\n")
        with mock.patch.object(mail.imaplib.IMAP4, "_simple_command", return_value=("OK", [])):
            with self.assertRaises(MailError):
                imap._simple_command("STARTTLS")

        smtp = SmtpReplyTests.reader(b"")
        smtp._wire_buffer.extend(b"plaintext after upgrade reply\r\n")
        with mock.patch.object(mail.smtplib.SMTP, "docmd", return_value=(220, b"ready")):
            with self.assertRaises(smtplib.SMTPServerDisconnected):
                smtp.docmd("STARTTLS")

    def test_invalid_tls_and_port_types_fail_before_socket_or_tls_setup(self):
        baseline = {"host": "mail.example.test", "port": 993, "tls": "tls", "username": "reader@example.test"}
        for connect in (mail.imap_connection, mail.smtp_connection):
            for invalid in ({"tls": "plain"}, {"tls": False}, {"port": True}, {"port": "993"}):
                with self.subTest(connect=connect.__name__, invalid=invalid):
                    with mock.patch.object(mail.ssl, "create_default_context") as context, \
                         mock.patch("socket.create_connection") as socket:
                        with self.assertRaises(MailError):
                            with connect({**baseline, **invalid}, "synthetic-password"):
                                self.fail("Unsupported settings cannot yield a connection.")
                    context.assert_not_called()
                    socket.assert_not_called()
