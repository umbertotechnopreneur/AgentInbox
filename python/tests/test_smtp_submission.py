"""SMTP transaction tests with explicit envelope decisions and fake DATA replies."""

import contextlib
import unittest
from unittest import mock

from agentinbox import mail

from tests._support import ScriptedSmtp


class SmtpSubmissionTests(unittest.TestCase):
    """Keep content off the wire unless every recipient and local policy permits it."""

    def setUp(self):
        network = mock.patch(
            "socket.create_connection", side_effect=AssertionError("Live network is forbidden.")
        )
        network.start()
        self.addCleanup(network.stop)
        self.settings = {
            "host": "smtp.example.test",
            "port": 465,
            "tls": "tls",
            "username": "sender@example.test",
        }
        self.recipients = ["reader@example.test", "copy@example.test", "private@example.test"]
        self.content = b"From: sender@example.test\r\nTo: reader@example.test\r\n\r\nSynthetic.\r\n"

    def test_every_recipient_is_accepted_before_policy_check_and_data(self):
        smtp = ScriptedSmtp(recipient_codes={"copy@example.test": 251})

        def before_data():
            smtp.operations.append(("policy_check", None))

        with mock.patch("agentinbox.mail.smtp_connection", return_value=contextlib.nullcontext(smtp)):
            result = mail.submit(
                self.settings, "synthetic-password", "sender@example.test",
                self.recipients, self.content, before_data,
            )

        self.assertEqual("accepted_by_server", result)
        self.assertEqual(
            ["mail", "rcpt", "rcpt", "rcpt", "policy_check", "data"],
            [operation for operation, _ in smtp.operations],
        )
        self.assertEqual(
            self.recipients, [value for operation, value in smtp.operations if operation == "rcpt"]
        )
        self.assertEqual(self.content, smtp.operations[-1][1])

    def test_last_recipient_rejection_resets_envelope_without_transmitting_data(self):
        smtp = ScriptedSmtp(recipient_codes={"private@example.test": 550})
        before_data = mock.Mock()

        with mock.patch("agentinbox.mail.smtp_connection", return_value=contextlib.nullcontext(smtp)):
            result = mail.submit(
                self.settings, "synthetic-password", "sender@example.test",
                self.recipients, self.content, before_data,
            )

        self.assertEqual("rejected", result)
        self.assertEqual(
            ["mail", "rcpt", "rcpt", "rcpt", "rset"],
            [operation for operation, _ in smtp.operations],
        )
        before_data.assert_not_called()
        self.assertFalse(any(operation == "data" for operation, _ in smtp.operations))

    def test_revoked_local_permission_after_recipient_acceptance_blocks_data(self):
        from agentinbox.errors import MailError

        smtp = ScriptedSmtp()
        before_data = mock.Mock(side_effect=MailError("sending_disabled", "Synthetic revocation."))

        with mock.patch("agentinbox.mail.smtp_connection", return_value=contextlib.nullcontext(smtp)):
            with self.assertRaises(MailError) as failure:
                mail.submit(
                    self.settings, "synthetic-password", "sender@example.test",
                    self.recipients, self.content, before_data,
                )

        self.assertEqual("sending_disabled", failure.exception.code)
        before_data.assert_called_once()
        self.assertFalse(any(operation == "data" for operation, _ in smtp.operations))
