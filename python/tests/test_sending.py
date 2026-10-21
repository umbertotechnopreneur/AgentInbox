"""Direct-send regression cases with fake SMTP and isolated local metadata."""

import contextlib
import email
import email.policy
import io
import json
import smtplib
import sqlite3
from types import SimpleNamespace
from unittest import mock

from agentinbox import mcp
from agentinbox.application import MailApplication
from agentinbox.errors import MailError
from tests._support import MailApplicationCase, ScriptedSmtp


class SendingTests(MailApplicationCase):
    """Protect the one-send boundary without real credentials, provider calls or UI."""

    def connect(self, server):
        self.smtp.side_effect = lambda *args, **kwargs: contextlib.nullcontext(server)

    def test_mcp_sends_directly_and_duplicate_id_never_sends_again(self):
        server = ScriptedSmtp()
        self.connect(server)
        identity = "a" * 32
        arguments = {"request_id": identity, "account_id": self.account["id"],
                     "to": ["reader@example.test"], "subject": "Synthetic", "body": "Synthetic content"}
        requests = [
            {"id": 1, "method": "initialize", "params": {}},
            {"method": "notifications/initialized"},
            {"id": 2, "method": "tools/call", "params": {"name": "send_email", "arguments": arguments}},
            {"id": 3, "method": "tools/call", "params": {"name": "send_email", "arguments": arguments}},
        ]
        stream = io.BytesIO(b"".join((json.dumps({"jsonrpc": "2.0", **item}) + "\n").encode() for item in requests))
        responses = []
        with mock.patch.object(mcp.sys, "stdin", SimpleNamespace(buffer=stream)), \
             mock.patch.object(mcp, "_write", side_effect=responses.append):
            mcp.serve(self.app)
        result = json.loads(responses[1]["result"]["content"][0]["text"])
        duplicate = json.loads(responses[2]["result"]["content"][0]["text"])
        self.assertEqual("accepted_by_server", result["status"])
        self.assertFalse(result["delivery_confirmed"])
        self.assertEqual("request_exists", duplicate["error"]["code"])
        self.assertEqual(1, sum(operation == "data" for operation, _ in server.operations))
        self.assertEqual(result, MailApplication(self.root).get_send_status(identity))

    def test_disabled_sending_and_invalid_request_id_fail_before_smtp(self):
        with self.assertRaises(MailError):
            self.send(request_id="invalid")
        self.update_account(send_enabled=False)
        with self.assertRaises(MailError) as failure:
            self.send()
        self.assertEqual("sending_disabled", failure.exception.code)
        self.assertFalse(self.app.sends.path.exists())
        self.smtp.assert_not_called()

    def test_account_change_before_data_prevents_transmission(self):
        server = ScriptedSmtp()

        def connect(*args, **kwargs):
            self.update_account(send_enabled=False)
            return contextlib.nullcontext(server)

        self.smtp.side_effect = connect
        result = self.send()
        self.assertEqual("rejected", result["status"])
        self.assertFalse(any(operation == "data" for operation, _ in server.operations))

    def test_bcc_and_unicode_reach_transport_without_persistent_content(self):
        server = ScriptedSmtp()
        self.connect(server)
        attachment = self.root / "sample.txt"
        attachment.write_bytes(b"synthetic attachment bytes")
        result = self.send(bcc=["hidden@example.test"], subject="Caffè", body="Un messaggio sintetico.",
                           attachments=[str(attachment)])
        content = next(value for operation, value in server.operations if operation == "data")
        message = email.message_from_bytes(content, policy=email.policy.default)
        self.assertIsNone(message["Bcc"])
        self.assertEqual("Caffè", message["Subject"])
        self.assertIn(("rcpt", "hidden@example.test"), server.operations)
        self.assertEqual(b"synthetic attachment bytes", next(message.iter_attachments()).get_payload(decode=True))
        with self.app.sends.database() as database:
            row = dict(database.execute("SELECT * FROM outgoing").fetchone())
        self.assertEqual({"id", "account_id", "created", "status", "error"}, set(row))
        self.assertEqual(result["request_id"], row["id"])
        self.assertFalse((self.root / "protected").exists())

    def test_lost_data_reply_stays_unknown_and_never_retries(self):
        server = ScriptedSmtp(data_error=smtplib.SMTPServerDisconnected("synthetic disconnect"))
        self.connect(server)
        identity = "b" * 32
        result = self.send(request_id=identity)
        self.assertEqual("outcome_unknown", result["status"])
        self.assertEqual(result, MailApplication(self.root).get_send_status(identity))
        with self.assertRaises(MailError):
            self.send(request_id=identity)
        self.assertEqual(1, self.smtp.call_count)

    def test_unfinished_record_is_not_claimed_as_success_or_retried(self):
        identity = "c" * 32
        self.app.sends.begin(identity, self.account["id"])
        result = MailApplication(self.root).get_send_status(identity)
        self.assertEqual("submitting", result["status"])
        self.assertIn("may have stopped", result["notice"])
        with self.assertRaises(MailError):
            self.send(request_id=identity)
        self.smtp.assert_not_called()

    def test_metadata_failure_preserves_observed_acceptance(self):
        self.connect(ScriptedSmtp())
        with mock.patch.object(self.app.sends, "finish", side_effect=OSError("synthetic disk failure")):
            result = self.send()
        self.assertEqual("accepted_by_server", result["status"])
        self.assertFalse(result["status_recorded"])
        self.assertFalse(result["delivery_confirmed"])
        self.assertIn("Do not resend", result["record_error"])

    def test_previous_ledger_schema_is_rejected_without_conversion(self):
        with contextlib.closing(sqlite3.connect(self.app.sends.path)) as database:
            database.execute("PRAGMA user_version=3")
            database.commit()
        original = self.app.sends.path.read_bytes()
        with self.assertRaises(MailError) as failure:
            self.send()
        self.assertEqual("invalid_profile", failure.exception.code)
        self.assertEqual(original, self.app.sends.path.read_bytes())
        self.smtp.assert_not_called()

    def test_management_interface_has_no_send_or_approval_operations(self):
        for operation in ("send_email", "prepare_email", "list_pending", "cancel_email", "send_reviewed"):
            with self.subTest(operation=operation), self.assertRaises(MailError) as failure:
                self.app.manage(operation, {})
            self.assertEqual("invalid_operation", failure.exception.code)
        self.smtp.assert_not_called()
