"""Synthetic persistence failures must not hide committed settings or mail outcomes."""

import copy
from unittest import mock

from agentinbox.errors import MailError
from agentinbox.storage import Store

from tests._support import MailApplicationCase


class StorageCleanupTests(MailApplicationCase):
    """Exercise durable cleanup references with real metadata and an in-memory vault."""

    def test_remove_keeps_failed_and_unattempted_secret_references_for_retry(self):
        account = self.app.store.account(self.account["id"])
        failed, other = account["incoming_secret"], account["smtp_secret"]
        delete = self.vault.delete

        def fail_one(key):
            if key == failed:
                raise MailError("secure_storage_unavailable", "Synthetic deletion failed.")
            delete(key)

        with mock.patch.object(self.vault, "delete", side_effect=fail_one):
            result = self.app.store.remove(account["id"])

        self.assertTrue(result["removed"])
        self.assertTrue(result["cleanup_pending"])
        self.assertEqual(2, result["cleanup_pending_count"])
        self.assertEqual([], self.app.store.accounts())
        self.assertIn(failed, self.vault.values)
        self.assertIn(other, self.vault.values)
        self.assertTrue(Store(self.root).cleanup_queued(failed))
        self.assertFalse(Store(self.root).retry_cleanup()["cleanup_pending"])
        self.assertNotIn(failed, self.vault.values)
        self.assertNotIn(other, self.vault.values)

    def test_password_rotation_reports_commit_even_when_old_secret_cannot_be_removed(self):
        before = self.app.store.account(self.account["id"])
        delete = self.vault.delete

        def fail_old(key):
            if key == before["incoming_secret"]:
                raise MailError("secure_storage_unavailable", "Synthetic old secret is locked.")
            delete(key)

        with mock.patch.object(self.vault, "delete", side_effect=fail_old):
            result = self.app.store.save(
                copy.deepcopy(self.draft), {"incoming": "replacement-synthetic-password", "smtp": ""},
                before["revision"],
            )

        current = self.app.store.account(before["id"])
        self.assertEqual(before["revision"] + 1, result["revision"])
        self.assertEqual(result["revision"], current["revision"])
        self.assertTrue(result["cleanup_pending"])
        self.assertEqual("replacement-synthetic-password", self.vault.get(current["incoming_secret"]))
        self.assertTrue(self.app.store.cleanup_queued(before["incoming_secret"]))
        self.assertFalse(self.app.store.retry_cleanup()["cleanup_pending"])
        self.assertEqual("replacement-synthetic-password", self.vault.get(current["incoming_secret"]))
        self.assertEqual("synthetic-smtp-password", self.vault.get(current["smtp_secret"]))

    def test_valid_account_inputs_cannot_overwrite_settings_with_oversized_json(self):
        draft = copy.deepcopy(self.draft)
        draft["id"] = ""
        draft["folders"] = [f"{index:02}-" + "x" * 252 for index in range(20)]
        for index in range(50):
            draft["label"] = f"Synthetic account {index}"
            previous_bytes = self.app.store.settings.read_bytes()
            previous_secrets = dict(self.vault.values)
            try:
                self.app.store.save(
                    draft, {"incoming": "synthetic-incoming", "smtp": "synthetic-smtp"}, None
                )
            except MailError as error:
                self.assertEqual("profile_too_large", error.code)
                self.assertEqual(previous_bytes, self.app.store.settings.read_bytes())
                self.assertEqual(previous_secrets, self.vault.values)
                self.assertLessEqual(len(previous_bytes), 256_000)
                self.assertTrue(self.app.store.accounts())
                break
        else:
            self.fail("Large, individually valid account inputs never reached the settings byte limit.")

    def test_failed_settings_write_keeps_cleanup_retryable_without_deleting_live_secrets(self):
        before = self.app.store.account(self.account["id"])
        live = {before["incoming_secret"], before["smtp_secret"]}
        delete = self.vault.delete

        def fail_new(key):
            if key not in live:
                raise MailError("secure_storage_unavailable", "Synthetic new secret cannot be removed.")
            delete(key)

        with mock.patch.object(self.app.store, "_write", side_effect=OSError("Synthetic disk failure")):
            with mock.patch.object(self.vault, "delete", side_effect=fail_new):
                with self.assertRaises(MailError) as failure:
                    self.app.store.save(
                        copy.deepcopy(self.draft), {"incoming": "uncommitted-synthetic-password", "smtp": ""},
                        before["revision"],
                    )

        self.assertEqual("save_failed_cleanup_pending", failure.exception.code)
        self.assertEqual(before, self.app.store.account(before["id"]))
        self.assertTrue(self.app.store.cleanup_status()["cleanup_pending"])
        self.assertFalse(Store(self.root).retry_cleanup()["cleanup_pending"])
        self.assertEqual(live, set(self.vault.values))
