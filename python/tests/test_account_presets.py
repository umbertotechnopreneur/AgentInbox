"""Provider choices survive saving without changing connection or secret data."""

import copy

from agentinbox.errors import MailError
from agentinbox.storage import Store
from tests._support import MailApplicationCase


class AccountPresetTests(MailApplicationCase):
    """Exercise persisted provider metadata with an isolated profile and vault."""

    def test_provider_choice_survives_restart_and_keeps_passwords(self):
        before = self.app.store.account(self.account["id"])
        for preset in ("gmail", "icloud", "outlook", "microsoft365", "custom"):
            with self.subTest(preset=preset):
                saved = self.update_account(preset=preset)
                restarted = Store(self.root)
                account = restarted.account(saved["id"])
                self.assertEqual(preset, account["preset"])
                self.assertEqual(preset, restarted.public(account)["preset"])
                for field in ("incoming", "smtp", "incoming_secret", "smtp_secret"):
                    self.assertEqual(before[field], account[field])
                self.assertEqual("synthetic-incoming-password", self.vault.get(account["incoming_secret"]))
                self.assertEqual("synthetic-smtp-password", self.vault.get(account["smtp_secret"]))

    def test_unknown_provider_fails_without_replacing_saved_settings(self):
        original = self.app.store.settings.read_bytes()
        draft = copy.deepcopy(self.draft)
        draft["preset"] = "unknown-provider"
        with self.assertRaises(MailError) as failure:
            self.app.store.save(draft, {"incoming": "", "smtp": ""}, self.account["revision"])
        self.assertEqual("invalid_configuration", failure.exception.code)
        self.assertEqual(original, self.app.store.settings.read_bytes())

    def test_missing_provider_is_not_inferred_or_written_on_read(self):
        original = self.app.store.settings.read_bytes()
        restarted = Store(self.root)
        self.assertNotIn("preset", restarted.account(self.account["id"]))
        self.assertEqual(original, restarted.settings.read_bytes())
