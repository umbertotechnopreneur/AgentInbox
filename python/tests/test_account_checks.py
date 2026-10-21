"""Draft-account checks and exact folder discovery with synthetic boundaries."""

import contextlib
import copy
import io
import imaplib
import json
import smtplib
import ssl
import unittest
from types import SimpleNamespace
from unittest import mock

from agentinbox import cli, mail
from agentinbox.errors import MailError
from tests._support import MailApplicationCase


class AccountCheckTests(MailApplicationCase):
    """Keep checking distinct from saving, mail reading and credential reuse."""

    def new_account(self):
        """Return an unsaved account without optional SMTP settings."""
        draft = copy.deepcopy(self.draft)
        draft.update(id="", smtp=None, send_enabled=False)
        return draft

    def test_new_account_check_discovers_folders_without_saving_or_reading(self):
        draft = self.new_account()
        settings = self.app.store.settings.read_bytes()
        secrets = dict(self.vault.values)
        self.imap.side_effect = lambda *args, **kwargs: contextlib.nullcontext(object())
        updates = []
        with mock.patch.object(mail, "folders", return_value=["INBOX", "Projects, current"]), \
             mock.patch.object(mail, "check_read", side_effect=AssertionError("No message read was requested.")):
            result = self.app.manage("check_account", {
                "account": draft, "passwords": {"incoming": "synthetic-draft-password", "smtp": ""},
                "expected_revision": None,
            }, progress=lambda stage, state: updates.append((stage, state)))
        self.assertEqual(["INBOX", "Projects, current"], result["folders"])
        self.assertFalse(result["saved"])
        self.assertEqual("not_requested", result["message_read"])
        self.assertFalse(result["sent"])
        self.assertEqual(settings, self.app.store.settings.read_bytes())
        self.assertEqual(secrets, self.vault.values)
        self.assertEqual([("settings", "started"), ("settings", "passed"), ("folders", "started"), ("folders", "passed")], updates)
        self.assertNotIn("synthetic-draft-password", json.dumps(result))
        self.smtp.assert_not_called()

    def test_saved_password_cannot_be_reused_for_a_changed_host(self):
        draft = copy.deepcopy(self.draft)
        draft["incoming"]["host"] = "other.example.test"
        with self.assertRaises(MailError) as failure:
            self.app.manage("check_account", {
                "account": draft, "passwords": {"incoming": "", "smtp": ""},
                "expected_revision": self.account["revision"],
            })
        self.assertEqual("password_required", failure.exception.code)
        self.imap.assert_not_called()

    def test_draft_smtp_check_needs_no_incoming_password_save_or_send_permission(self):
        draft = copy.deepcopy(self.draft)
        draft.update(id="", send_enabled=False)
        settings = self.app.store.settings.read_bytes()
        secrets = dict(self.vault.values)
        self.smtp.side_effect = lambda *args, **kwargs: contextlib.nullcontext(object())
        result = self.app.manage("check_smtp", {
            "account": draft, "passwords": {"incoming": "", "smtp": "synthetic-draft-smtp"},
            "expected_revision": None,
        })
        self.assertEqual({"connection": "passed", "authentication": "passed", "saved": False, "sent": False}, result)
        self.assertEqual(settings, self.app.store.settings.read_bytes())
        self.assertEqual(secrets, self.vault.values)
        self.smtp.assert_called_once_with(draft["smtp"], password="synthetic-draft-smtp", progress=None)
        self.imap.assert_not_called()
        self.pop.assert_not_called()

    def test_smtp_check_reuses_only_the_unchanged_saved_connection_password(self):
        self.smtp.side_effect = lambda *args, **kwargs: contextlib.nullcontext(object())
        arguments = {"account": copy.deepcopy(self.draft), "passwords": {"incoming": "", "smtp": ""},
                     "expected_revision": self.account["revision"]}
        self.app.manage("check_smtp", arguments)
        self.smtp.assert_called_once_with(self.draft["smtp"], password="synthetic-smtp-password", progress=None)
        self.smtp.reset_mock()
        arguments["account"]["smtp"]["host"] = "other.example.test"
        with self.assertRaises(MailError) as failure:
            self.app.manage("check_smtp", arguments)
        self.assertEqual("password_required", failure.exception.code)
        self.smtp.assert_not_called()
        self.imap.assert_not_called()

    def test_stale_smtp_draft_cannot_connect(self):
        with self.assertRaises(MailError) as failure:
            self.app.manage("check_smtp", {
                "account": self.draft, "passwords": {"incoming": "", "smtp": ""},
                "expected_revision": self.account["revision"] + 1,
            })
        self.assertEqual("configuration_changed", failure.exception.code)
        self.smtp.assert_not_called()

    def test_local_management_error_includes_server_detail_without_changing_public_errors(self):
        error = MailError("authentication_failed", "Sign-in failed.", server_response="SMTP: 535 Access disabled")
        request = {"operation": "check_smtp", "arguments": {
            "account": self.draft, "passwords": {"incoming": "", "smtp": ""},
            "expected_revision": self.account["revision"],
        }}
        stream = io.BytesIO((json.dumps(request) + "\n").encode())
        records = []
        self.smtp.side_effect = error
        with mock.patch.object(cli.sys, "stdin", SimpleNamespace(buffer=stream)), \
             mock.patch.object(cli, "write", side_effect=records.append):
            self.assertEqual(1, cli.manage(self.app, False))
        self.assertEqual(error.server_response, records[0]["error"]["server_response"])
        self.assertNotIn("server_response", error.result())

    def test_stale_revision_blocks_check_before_any_connection(self):
        with self.assertRaises(MailError) as failure:
            self.app.manage("check_account", {
                "account": self.draft, "passwords": {"incoming": "", "smtp": ""},
                "expected_revision": self.account["revision"] + 1,
            })
        self.assertEqual("configuration_changed", failure.exception.code)
        self.imap.assert_not_called()

    def test_smtp_management_progress_stops_at_invalid_settings_without_connecting(self):
        request = {"operation": "check_smtp", "progress": True, "arguments": {
            "account": self.draft, "passwords": {"incoming": "", "smtp": ""},
            "expected_revision": self.account["revision"] + 1,
        }}
        stream = io.BytesIO((json.dumps(request) + "\n").encode())
        records = []
        with mock.patch.object(cli.sys, "stdin", SimpleNamespace(buffer=stream)), \
             mock.patch.object(cli, "write", side_effect=records.append):
            self.assertEqual(1, cli.manage(self.app, False))
        self.assertEqual(2, len(records))
        self.assertEqual({"progress": {"stage": "settings", "state": "started"}}, records[0])
        self.assertEqual("configuration_changed", records[1]["error"]["code"])
        self.smtp.assert_not_called()
        self.imap.assert_not_called()

    def test_management_progress_is_opt_in_and_contains_only_stage_metadata(self):
        draft = self.new_account()
        request = {"operation": "check_account", "progress": True, "arguments": {
            "account": draft, "passwords": {"incoming": "synthetic-draft-password", "smtp": ""},
            "expected_revision": None,
        }}
        self.imap.side_effect = lambda *args, **kwargs: contextlib.nullcontext(object())
        stream = io.BytesIO((json.dumps(request) + "\n").encode())
        records = []
        with mock.patch.object(mail, "folders", return_value=["INBOX"]), \
             mock.patch.object(cli.sys, "stdin", SimpleNamespace(buffer=stream)), \
             mock.patch.object(cli, "write", side_effect=records.append):
            result = cli.manage(self.app, False)
        self.assertEqual(0, result)
        self.assertEqual({"progress": {"stage": "folders", "state": "started"}}, records[-3])
        self.assertEqual({"progress": {"stage": "folders", "state": "passed"}}, records[-2])
        self.assertTrue(records[-1]["ok"])
        self.assertNotIn("synthetic-draft-password", json.dumps(records))

    def test_failed_folder_discovery_never_reports_folder_success(self):
        self.imap.side_effect = lambda *args, **kwargs: contextlib.nullcontext(object())
        updates = []
        with mock.patch.object(mail, "folders", side_effect=MailError("mail_operation_failed", "Synthetic LIST failure.")):
            with self.assertRaises(MailError):
                self.app.manage("check_account", {
                    "account": self.new_account(), "passwords": {"incoming": "synthetic-password", "smtp": ""},
                    "expected_revision": None,
                }, progress=lambda stage, state: updates.append((stage, state)))
        self.assertEqual([("settings", "started"), ("settings", "passed"), ("folders", "started")], updates)

    def test_optional_message_read_distinguishes_empty_folders_and_returns_no_content(self):
        self.imap.side_effect = lambda *args, **kwargs: contextlib.nullcontext(object())
        for sample, state in (("passed", "passed"), ("no_sample", "empty")):
            updates = []
            with self.subTest(sample=sample), mock.patch.object(mail, "folders", return_value=["INBOX"]), \
                 mock.patch.object(mail, "check_connection_read", return_value={"message_read": sample, "body": "Synthetic private content"}) as read:
                result = self.app.manage("check_account", {
                    "account": self.new_account(), "passwords": {"incoming": "synthetic-password", "smtp": ""},
                    "expected_revision": None, "read_message": True,
                }, progress=lambda stage, state: updates.append((stage, state)))
            self.assertEqual(sample, result["message_read"])
            self.assertEqual(("reading", state), updates[-1])
            self.assertEqual("INBOX", read.call_args.args[1])
            self.assertNotIn("Synthetic private content", json.dumps(result))
            self.assertFalse(result["saved"])
            self.assertFalse(result["sent"])

    def test_pop_check_returns_only_inbox_without_imap_discovery(self):
        draft = self.new_account()
        draft["incoming"].update(protocol="pop3", host="pop.example.test", port=995)
        self.pop.side_effect = lambda *args, **kwargs: contextlib.nullcontext(object())
        with mock.patch.object(mail, "folders", side_effect=AssertionError("POP3 has no server folders.")):
            result = self.app.manage("check_account", {
                "account": draft, "passwords": {"incoming": "synthetic-password", "smtp": ""},
                "expected_revision": None,
            })
        self.assertEqual(["INBOX"], result["folders"])
        self.assertEqual("pop3", result["protocol"])
        self.imap.assert_not_called()

    def test_read_option_rejects_numeric_boolean_before_connecting(self):
        with self.assertRaises(MailError):
            self.app.manage("check_account", {
                "account": self.new_account(), "passwords": {"incoming": "synthetic-password", "smtp": ""},
                "expected_revision": None, "read_message": 1,
            })
        self.imap.assert_not_called()

    def test_failed_smtp_signin_is_never_reported_as_success(self):
        draft = self.new_account()
        draft.update(smtp=self.draft["smtp"], send_enabled=False)
        self.imap.side_effect = lambda *args, **kwargs: contextlib.nullcontext(object())
        self.smtp.side_effect = MailError("authentication_failed", "Synthetic SMTP rejection.")
        updates = []
        with mock.patch.object(mail, "folders", return_value=["INBOX"]):
            with self.assertRaises(MailError):
                self.app.manage("check_account", {
                    "account": draft, "passwords": {"incoming": "synthetic-password", "smtp": "synthetic-smtp-password"},
                    "expected_revision": None,
                }, progress=lambda stage, state: updates.append((stage, state)))
        self.assertEqual(("smtp", "started"), updates[-1])
        self.assertNotIn(("smtp", "passed"), updates)


class FolderNameTests(unittest.TestCase):
    """Parse server folder identities without exposing IMAP response syntax in the UI."""

    def test_quoted_literal_and_comma_names_are_exact_and_noselect_is_excluded(self):
        connection = mock.Mock()
        connection.list.return_value = ("OK", [
            b'(\\HasNoChildren) "/" "INBOX"',
            b'(\\Noselect) "/" "Parent"',
            b'() "/" "Projects, current"',
            b'() "/" "A \\"quote\\""',
            (b'() "/" {9}', b'All items'), b'',
        ])
        self.assertEqual(["INBOX", "Projects, current", 'A "quote"', "All items"], mail.folders(connection))

    def test_malformed_or_control_character_names_fail_without_partial_results(self):
        for row in (b'() "/" "unterminated', (b'() "/" {3}', b'longer'), b'() "/" "bad\rname"'):
            with self.subTest(row=row), self.assertRaises(MailError):
                mail._folder_name(row)


class ConnectionProgressTests(unittest.TestCase):
    """A failed TLS handshake or sign-in cannot paint a successful stage."""

    def test_failed_tls_reports_no_connection_or_authentication_success(self):
        updates = []
        settings = {"host": "imap.example.test", "port": 993, "tls": "tls", "username": "reader@example.test"}
        with mock.patch.object(mail, "_IMAP4SSL", side_effect=ssl.SSLError("Synthetic certificate failure.")):
            with self.assertRaises(MailError) as failure:
                with mail.imap_connection(settings, "synthetic-password", progress=lambda stage, state: updates.append((stage, state))):
                    self.fail("TLS failure must prevent authenticated work.")
        self.assertEqual("tls_failed", failure.exception.code)
        self.assertEqual([("connection", "started")], updates)

    def test_failed_signin_preserves_tls_success_but_never_authentication_success(self):
        updates = []
        settings = {"host": "imap.example.test", "port": 993, "tls": "tls", "username": "reader@example.test"}
        connection = mock.Mock()
        connection.login.side_effect = imaplib.IMAP4.error("Synthetic credentials rejected.")
        with mock.patch.object(mail, "_IMAP4SSL", return_value=connection):
            with self.assertRaises(MailError) as failure:
                with mail.imap_connection(settings, "synthetic-password", progress=lambda stage, state: updates.append((stage, state))):
                    self.fail("Rejected credentials must prevent authenticated work.")
        self.assertEqual("authentication_failed", failure.exception.code)
        self.assertEqual([("connection", "started"), ("connection", "passed"), ("authentication", "started")], updates)

    def test_smtp_progress_requires_verified_tls_and_accepted_authentication(self):
        for tls in ("tls", "starttls"):
            for failure_stage in (None, "connection", "authentication"):
                with self.subTest(tls=tls, failure_stage=failure_stage):
                    updates = []
                    settings = {"host": "smtp.example.test", "port": 465 if tls == "tls" else 587,
                                "tls": tls, "username": "reader@example.test"}
                    connection = mock.Mock()
                    tls_error = ssl.SSLError("Synthetic certificate failure.")
                    if failure_stage == "connection" and tls == "starttls":
                        connection.starttls.side_effect = tls_error
                    if failure_stage == "authentication":
                        connection.login.side_effect = smtplib.SMTPAuthenticationError(535, b"5.7.8 Access denied")
                    factory = "_SMTPSSL" if tls == "tls" else "_SMTP"
                    with mock.patch.object(mail, factory, return_value=connection,
                                           side_effect=tls_error if failure_stage == "connection" and tls == "tls" else None):
                        def check():
                            with mail.smtp_connection(settings, "synthetic-password",
                                                      progress=lambda stage, state: updates.append((stage, state))):
                                self.assertEqual(("authentication", "passed"), updates[-1])
                        if failure_stage:
                            with self.assertRaises(MailError) as failure:
                                check()
                            self.assertEqual("tls_failed" if failure_stage == "connection" else "authentication_failed",
                                             failure.exception.code)
                        else:
                            check()
                    expected = [("connection", "started")]
                    if failure_stage != "connection":
                        expected += [("connection", "passed"), ("authentication", "started")]
                        connection.login.assert_called_once_with(settings["username"], "synthetic-password")
                    else:
                        connection.login.assert_not_called()
                    if failure_stage is None:
                        expected.append(("authentication", "passed"))
                    self.assertEqual(expected, updates)
                    connection.sendmail.assert_not_called()
                    if failure_stage != "connection" or tls == "starttls":
                        connection.close.assert_called_once()
