"""Regression scenarios for account policy, review and outgoing state changes."""

import base64
import contextlib
import copy
import hashlib
import io
import json
import smtplib
from email import policy
from email.parser import BytesParser
from types import SimpleNamespace
from unittest import mock

from agentinbox import pop, review
from agentinbox.application import MailApplication
from agentinbox.errors import MailError

from tests._support import MailApplicationCase, ScriptedSmtp


class MailWorkflowTests(MailApplicationCase):
    """Check user-visible consequences of policy and state changes."""

    def test_pop_continuation_preserves_incomplete_date_coverage(self):
        draft = copy.deepcopy(self.draft)
        draft["incoming"].update(protocol="pop3", host="pop.example.test", port=995)
        self.account = self.app.store.save(
            draft, {"incoming": "synthetic-pop-password", "smtp": ""}, self.account["revision"]
        )
        self.pop.side_effect = lambda *args, **kwargs: contextlib.nullcontext(object())
        page = {
            "messages": [], "candidate_limit_reached": False,
            "uid_validity": pop.UID_VALIDITY, "candidates": ["undated", "dated"],
            "date_basis": pop.DATE_BASIS, "search_scope": pop.SEARCH_SCOPE,
        }
        pages = [
            {**page, "more": True, "next_offset": 1, "undated_messages_skipped": 1},
            {**page, "more": False, "next_offset": 2, "undated_messages_skipped": 0},
        ]
        with mock.patch("agentinbox.pop.search", side_effect=pages) as search:
            first = self.app.search_mail(
                self.account["id"], start="2026-10-01T00:00:00Z", end="2026-10-19T00:00:00Z"
            )
            final = self.app.search_mail(self.account["id"], continuation=first["continuation"])

        self.assertFalse(final["coverage_complete"])
        self.assertTrue(final["date_filter_incomplete"])
        self.assertEqual(1, final["undated_messages_skipped"])
        self.assertEqual(pop.DATE_BASIS, final["date_basis"])
        self.assertEqual(pop.SEARCH_SCOPE, final["search_scope"])
        self.assertIsNone(final["continuation"])
        self.assertEqual(1, search.call_args.kwargs["offset"])
        self.assertEqual(["undated", "dated"], search.call_args.kwargs["candidates"])
        self.assertEqual(2, self.pop.call_count)
        self.imap.assert_not_called()

    def test_disabled_smtp_blocks_preparation_without_network_or_outgoing_content(self):
        self.update_account(send_enabled=False)

        with self.assertRaises(MailError) as failure:
            self.prepare()

        self.assertEqual("sending_disabled", failure.exception.code)
        self.assertEqual([], self.app.manage("list_pending", {}))
        self.assertFalse(any(key.startswith("outgoing-") for key in self.vault.values))
        self.smtp.assert_not_called()
        self.imap.assert_not_called()

    def test_wrong_review_challenge_preserves_pending_message_without_sending(self):
        request = self.prepare(cc=["copy@example.test"], bcc=["private@example.test"])
        captured = []
        response = {"challenge": "belongs-to-a-different-review", "approved": True}
        incoming = SimpleNamespace(buffer=io.BytesIO(json.dumps(response).encode() + b"\n"))

        with mock.patch.object(review.sys, "stdin", incoming):
            with self.assertRaises(MailError) as failure:
                review.pipe_review(self.app, request["request_id"], captured.append)

        self.assertEqual("invalid_approval", failure.exception.code)
        self.assertEqual(1, len(captured))
        preview = captured[0]["result"]
        self.assertEqual(["reader@example.test"], preview["to"])
        self.assertEqual(["copy@example.test"], preview["cc"])
        self.assertEqual(["private@example.test"], preview["bcc"])
        self.assertEqual("Synthetic message", preview["subject"])
        self.assertIn("body", preview)
        self.assertIn("attachments", preview)
        self.assertNotIn("mime", preview)
        self.assertNotIn("recipients", preview)
        self.assertEqual(
            "pending_approval", self.app.get_send_status(request["request_id"])["status"]
        )
        self.smtp.assert_not_called()

    def test_changed_review_content_cannot_reuse_original_approval(self):
        request = self.prepare()
        record, frozen = self.app.review_snapshot(request["request_id"])
        changed = copy.deepcopy(frozen)
        changed["body"] = "Different content that the user did not review."

        with self.assertRaises(MailError) as failure:
            self.app.send_reviewed(record, changed, True)

        self.assertEqual("message_changed", failure.exception.code)
        self.assertEqual(
            "pending_approval", self.app.get_send_status(request["request_id"])["status"]
        )
        self.smtp.assert_not_called()

    def test_changed_protected_content_fails_before_local_review(self):
        request = self.prepare()
        slot = "outgoing-" + request["request_id"]
        changed = json.loads(self.vault.values[slot])
        changed["to"] = ["unreviewed@example.test"]
        self.vault.values[slot] = json.dumps(changed)

        with self.assertRaises(MailError) as failure:
            self.app.review_snapshot(request["request_id"])

        self.assertEqual("message_changed", failure.exception.code)
        self.smtp.assert_not_called()

    def test_attachment_changes_after_preparation_do_not_change_reviewed_or_sent_bytes(self):
        attachment = self.root / "sample.txt"
        original = b"Synthetic attachment approved with this message.\n"
        attachment.write_bytes(original)
        request = self.prepare(attachments=[str(attachment)])
        record, frozen = self.app.review_snapshot(request["request_id"])
        attachment.write_bytes(b"Different bytes written after preparation.\n")
        smtp = ScriptedSmtp()
        self.smtp.side_effect = None
        self.smtp.return_value = contextlib.nullcontext(smtp)

        result = self.app.send_reviewed(record, frozen, True)

        self.assertEqual("accepted_by_server", result["status"])
        self.assertEqual(hashlib.sha256(original).hexdigest(), frozen["attachments"][0]["sha256"])
        submitted = next(value for operation, value in smtp.operations if operation == "data")
        message = BytesParser(policy=policy.default).parsebytes(submitted)
        self.assertEqual(original, next(message.iter_attachments()).get_payload(decode=True))
        self.assertEqual(base64.b64decode(frozen["mime"]), submitted)

    def test_bcc_stays_in_envelope_and_unicode_message_uses_seven_bit_mime(self):
        body = "Ciao, l'email è pronta. 你好 🌍"
        request = self.prepare(
            subject="Conferma: caffè e tè",
            body=body,
            cc=["copy@example.test"],
            bcc=["private@example.test"],
        )
        _, frozen = self.app.review_snapshot(request["request_id"])
        raw = base64.b64decode(frozen["mime"])

        raw.decode("ascii")
        message = BytesParser(policy=policy.default).parsebytes(raw)
        self.assertIsNone(message["Bcc"])
        self.assertNotIn(b"private@example.test", raw)
        self.assertEqual(
            ["reader@example.test", "copy@example.test", "private@example.test"],
            frozen["recipients"],
        )
        self.assertEqual(body, message.get_body(preferencelist=("plain",)).get_content().rstrip("\r\n"))
        self.assertEqual("Conferma: caffè e tè", str(message["Subject"]))
        self.smtp.assert_not_called()

    def test_cancel_pending_message_removes_content_without_sending(self):
        request = self.prepare()
        record, frozen = self.app.review_snapshot(request["request_id"])

        result = self.app.send_reviewed(record, frozen, False)

        self.assertEqual("cancelled", result["status"])
        self.assertNotIn("outgoing-" + request["request_id"], self.vault.values)
        self.smtp.assert_not_called()

    def test_cancel_cannot_rewrite_an_inflight_submission(self):
        request_id = self.prepare()["request_id"]
        observer = MailApplication(self.root)
        with self.app.outbox.submission_lease(request_id) as acquired:
            self.assertTrue(acquired)
            self.app.outbox.claim(request_id)

            with self.assertRaises(MailError) as failure:
                observer.outbox.cancel(request_id)

            self.assertEqual("approval_unavailable", failure.exception.code)
            self.assertEqual("submitting", observer.get_send_status(request_id)["status"])
            self.assertIn("outgoing-" + request_id, self.vault.values)

    def test_cancel_and_second_approval_cannot_rewrite_server_acceptance(self):
        request_id = self.prepare()["request_id"]
        record, frozen = self.app.review_snapshot(request_id)
        smtp = ScriptedSmtp()
        self.smtp.side_effect = None
        self.smtp.return_value = contextlib.nullcontext(smtp)
        self.app.send_reviewed(record, frozen, True)

        with self.assertRaises(MailError) as cancellation:
            self.app.outbox.cancel(request_id)
        with self.assertRaises(MailError) as repeated:
            self.app.send_reviewed(record, frozen, True)

        self.assertEqual("approval_unavailable", cancellation.exception.code)
        self.assertEqual("approval_unavailable", repeated.exception.code)
        status = self.app.get_send_status(request_id)
        self.assertEqual("accepted_by_server", status["status"])
        self.assertFalse(status["delivery_confirmed"])
        self.assertEqual(1, sum(operation == "data" for operation, _ in smtp.operations))
        self.assertNotIn("outgoing-" + request_id, self.vault.values)

    def test_active_submission_lease_prevents_premature_crash_recovery(self):
        request_id = self.prepare()["request_id"]
        observer = MailApplication(self.root)

        with self.app.outbox.submission_lease(request_id) as acquired:
            self.assertTrue(acquired)
            self.app.outbox.claim(request_id)
            status = observer.get_send_status(request_id)
            self.assertEqual("submitting", status["status"])
            self.assertIn("outgoing-" + request_id, self.vault.values)

        # The claimant has left without recording a result, as after an interruption.
        recovered = observer.get_send_status(request_id)
        self.assertEqual("outcome_unknown", recovered["status"])
        self.assertEqual("submission_interrupted", recovered["error"])
        self.assertFalse(recovered["delivery_confirmed"])
        self.assertNotIn("outgoing-" + request_id, self.vault.values)
        with self.assertRaises(MailError) as repeated:
            observer.outbox.claim(request_id)
        self.assertEqual("approval_unavailable", repeated.exception.code)
        self.smtp.assert_not_called()

    def test_disconnect_after_data_remains_unknown_and_cannot_be_approved_again(self):
        request_id = self.prepare()["request_id"]
        record, frozen = self.app.review_snapshot(request_id)
        smtp = ScriptedSmtp(data_error=smtplib.SMTPServerDisconnected("Synthetic lost reply."))
        self.smtp.side_effect = None
        self.smtp.return_value = contextlib.nullcontext(smtp)

        result = self.app.send_reviewed(record, frozen, True)

        self.assertEqual("outcome_unknown", result["status"])
        self.assertFalse(self.app.get_send_status(request_id)["delivery_confirmed"])
        with self.assertRaises(MailError) as repeated:
            self.app.send_reviewed(record, frozen, True)
        self.assertEqual("approval_unavailable", repeated.exception.code)
        self.smtp.assert_called_once()
        self.assertEqual(1, sum(operation == "data" for operation, _ in smtp.operations))

    def test_account_revision_invalidates_message_reference_before_another_imap_read(self):
        page = {
            "messages": [
                {
                    "uid": "17",
                    "uid_validity": "42",
                    "folder": "INBOX",
                    "subject": "Synthetic result",
                    "received_at": "2026-10-18T12:00:00+00:00",
                }
            ],
            "more": False,
            "candidate_limit_reached": False,
            "uid_validity": "42",
            "candidates": ["17"],
            "next_offset": 1,
        }
        self.imap.side_effect = None
        self.imap.return_value = contextlib.nullcontext(object())
        with mock.patch("agentinbox.mail.search", return_value=page):
            result = self.app.search_mail(
                self.account["id"], start="2026-10-01T00:00:00Z", end="2026-10-19T00:00:00Z"
            )
        reference = result["messages"][0]["reference"]
        self.update_account(label="Changed synthetic account")
        self.imap.reset_mock()
        with mock.patch("agentinbox.mail.read") as read:
            with self.assertRaises(MailError) as failure:
                self.app.read_mail(reference)

        self.assertEqual("configuration_changed", failure.exception.code)
        self.imap.assert_not_called()
        read.assert_not_called()
