"""Profiles have no fixed mailbox-count ceiling; persistence stays validated."""

import copy

from agentinbox.storage import Store
from tests._support import MailApplicationCase


class AccountCapacityTests(MailApplicationCase):
    """Cross the former 50-account boundary with isolated synthetic accounts."""

    def test_one_hundred_accounts_survive_restart_update_and_removal(self):
        identities = {self.account["id"]}
        for index in range(1, 100):
            draft = copy.deepcopy(self.draft)
            draft.update(id="", label=f"Mailbox {index}", email=f"mailbox{index}@example.test")
            saved = self.app.store.save(
                draft, {"incoming": "synthetic-incoming", "smtp": "synthetic-smtp"}, None
            )
            identities.add(saved["id"])

        restarted = Store(self.root)
        accounts = restarted.accounts()
        self.assertEqual(100, len(accounts))
        self.assertEqual(identities, {account["id"] for account in accounts})

        draft = copy.deepcopy(self.draft)
        draft["label"] = "Updated after restart"
        updated = restarted.save(draft, {"incoming": "", "smtp": ""}, self.account["revision"])
        self.assertEqual(self.account["revision"] + 1, updated["revision"])
        self.assertEqual("Updated after restart", Store(self.root).account(updated["id"])["label"])
        self.assertEqual(100, len(restarted.accounts()))

        restarted.remove(updated["id"])
        remaining = Store(self.root).accounts()
        self.assertEqual(99, len(remaining))
        self.assertNotIn(updated["id"], {account["id"] for account in remaining})
