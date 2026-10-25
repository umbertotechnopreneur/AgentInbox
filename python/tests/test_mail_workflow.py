"""Regression scenarios for account access and search state changes."""

import contextlib
import copy
from unittest import mock

from agentinbox import pop
from agentinbox.errors import MailError

from tests._support import MailApplicationCase


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
