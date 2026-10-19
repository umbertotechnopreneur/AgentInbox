"""Isolated test fixtures with fake credentials and no permitted network access."""

import copy
import os
import re
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from agentinbox.application import MailApplication
from agentinbox.errors import MailError


class MemoryVault:
    """Keep synthetic secrets in memory instead of invoking system protection."""

    def __init__(self):
        self.values = {}

    def _key(self, key):
        if not isinstance(key, str) or not re.fullmatch(r"[a-z]+-[a-f0-9]{32}", key):
            raise MailError("invalid_reference", "Invalid synthetic storage reference.")
        return key

    def put(self, key, value):
        self.values[self._key(key)] = value

    def get(self, key):
        try:
            return self.values[self._key(key)]
        except KeyError as error:
            raise MailError("credential_unavailable", "Synthetic secret is absent.") from error

    def delete(self, key):
        self.values.pop(self._key(key), None)


class MailApplicationCase(unittest.TestCase):
    """Exercise real local policy and storage with fake external boundaries."""

    def setUp(self):
        repository = Path(__file__).resolve().parents[2]
        artifacts = repository / "artifacts"
        self.test_root = Path(
            os.environ.get("AGENTINBOX_TEST_ROOT", str(artifacts / "tests"))
        ).expanduser()
        # Refuse redirected artifact roots before creating or cleaning test output.
        if (
            not self.test_root.is_absolute()
            or artifacts.resolve() != artifacts
            or self.test_root.resolve() != self.test_root
            or self.test_root == artifacts
            or not self.test_root.is_relative_to(artifacts)
        ):
            raise RuntimeError("Test artifacts must not be redirected outside the checkout.")
        self.created_test_root = not self.test_root.exists()
        self.test_root.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix="mail-", dir=self.test_root)
        self.root = Path(self.temporary.name).resolve()
        if self.root.parent != self.test_root:
            raise RuntimeError("Unexpected test profile location.")
        self.addCleanup(self._cleanup_profile)

        self.vault = MemoryVault()
        self._patch("agentinbox.storage.Vault", return_value=self.vault)
        self._patch("socket.create_connection", side_effect=AssertionError("Live network is forbidden."))
        self.imap = self._patch(
            "agentinbox.mail.imap_connection",
            side_effect=AssertionError("Unexpected IMAP connection."),
        )
        self.smtp = self._patch(
            "agentinbox.mail.smtp_connection",
            side_effect=AssertionError("Unexpected SMTP connection."),
        )
        self._patch("agentinbox.application.MailApplication._open_review")
        self.app = MailApplication(self.root)
        self.draft = {
            "id": "",
            "label": "Synthetic account",
            "email": "sender@example.test",
            "imap": {
                "host": "imap.example.test",
                "port": 993,
                "tls": "tls",
                "username": "sender@example.test",
            },
            "smtp": {
                "host": "smtp.example.test",
                "port": 465,
                "tls": "tls",
                "username": "sender@example.test",
            },
            "read_enabled": True,
            "send_enabled": True,
            "folders": ["INBOX"],
        }
        self.account = self.app.store.save(
            copy.deepcopy(self.draft),
            {"imap": "synthetic-imap-password", "smtp": "synthetic-smtp-password"},
            None,
        )
        self.draft["id"] = self.account["id"]

    def _patch(self, target, **arguments):
        patcher = mock.patch(target, **arguments)
        result = patcher.start()
        self.addCleanup(patcher.stop)
        return result

    def _cleanup_profile(self):
        resolved = Path(self.temporary.name).resolve()
        if resolved != self.root or resolved.parent != self.test_root:
            raise RuntimeError("Refusing cleanup outside this test's profile.")
        self.temporary.cleanup()
        if self.created_test_root and self.test_root.exists():
            # Remove only an empty container created by this test, never other runs.
            if not any(self.test_root.iterdir()):
                self.test_root.rmdir()

    def update_account(self, **changes):
        draft = copy.deepcopy(self.draft)
        draft.update(changes)
        self.account = self.app.store.save(
            draft, {"imap": "", "smtp": ""}, self.account["revision"]
        )
        self.draft = draft
        return self.account

    def prepare(self, **changes):
        arguments = {
            "account_id": self.account["id"],
            "to": ["reader@example.test"],
            "subject": "Synthetic message",
            "body": "A message created only for this isolated test.",
        }
        arguments.update(changes)
        return self.app.prepare_email(**arguments)


class ScriptedSmtp:
    """Record SMTP envelope and DATA operations without opening a socket."""

    def __init__(self, recipient_codes=None, data_error=None):
        self.recipient_codes = recipient_codes or {}
        self.data_error = data_error
        self.operations = []

    def mail(self, sender):
        self.operations.append(("mail", sender))
        return 250, b"synthetic sender accepted"

    def rcpt(self, recipient):
        self.operations.append(("rcpt", recipient))
        return self.recipient_codes.get(recipient, 250), b"synthetic recipient response"

    def rset(self):
        self.operations.append(("rset", None))
        return 250, b"synthetic envelope reset"

    def data(self, content):
        self.operations.append(("data", content))
        if self.data_error is not None:
            raise self.data_error
        return 250, b"synthetic message accepted"
