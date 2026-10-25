"""Command-line configuration cases with synthetic accounts and fake boundaries."""

import contextlib
import copy
import io
import json
from types import SimpleNamespace
from unittest import mock

from agentinbox import cli, setup_cli
from agentinbox.errors import MailError
from tests._support import MailApplicationCase


class SetupCommandTests(MailApplicationCase):
    """Describe CLI account setup without native credentials or provider traffic."""

    def setUp(self):
        """Prepare one settings-only template inside the isolated test profile."""
        super().setUp()
        self.template = copy.deepcopy(self.draft)
        self.template["id"] = ""
        self.template["smtp"] = None
        self.template["send_enabled"] = False
        self.path = self.root / "accounts.setup.json"
        self._write_template()

    def _write_template(self):
        self.path.write_text(json.dumps({"accounts": {"sample": self.template}}), encoding="utf-8")

    def _options(self, action, **values):
        return SimpleNamespace(command="accounts", action=action, file=self.path, entry="sample", json=False, **values)

    def test_add_saves_settings_with_a_hidden_password(self):
        with mock.patch.object(setup_cli.getpass, "getpass", return_value="synthetic-app-password") as prompt:
            result = setup_cli.run(self.app, self._options("add"))
        self.assertNotEqual(self.account["id"], result["id"])
        self.assertEqual(1, result["revision"])
        self.assertFalse(result["send_enabled"])
        self.assertNotIn("synthetic-app-password", json.dumps(result))
        prompt.assert_called_once()
        self.imap.assert_not_called()
        self.smtp.assert_not_called()

    def test_oauth_add_never_prompts_for_a_password(self):
        self.template["authentication"] = {"method": "microsoft_oauth", "client_id": "11111111-2222-4333-8444-555555555555", "tenant": "organizations"}
        self.template["incoming"]["host"] = "outlook.office365.com"
        self._write_template()
        with mock.patch.object(setup_cli.getpass, "getpass") as prompt:
            result = setup_cli.run(self.app, self._options("add"))
        self.assertFalse(result["microsoft_connected"])
        prompt.assert_not_called()

    def test_setup_file_rejects_inline_secrets_before_prompting(self):
        self.template["password"] = "synthetic-must-not-save"
        self._write_template()
        with mock.patch.object(setup_cli.getpass, "getpass") as prompt:
            with self.assertRaises(MailError):
                setup_cli.run(self.app, self._options("add"))
        prompt.assert_not_called()
        self.assertEqual(1, len(self.app.store.accounts()))

    def test_duplicate_json_keys_are_rejected(self):
        self.path.write_text('{"accounts": {}, "accounts": {}}', encoding="utf-8")
        with self.assertRaises(MailError) as failure:
            setup_cli.load_entry(self.path, "sample")
        self.assertEqual("invalid_setup_file", failure.exception.code)

    def test_unknown_entry_does_not_create_an_account(self):
        with self.assertRaises(MailError) as failure:
            setup_cli.load_entry(self.path, "missing")
        self.assertEqual("setup_entry_missing", failure.exception.code)
        self.assertEqual(1, len(self.app.store.accounts()))

    def test_stale_update_fails_before_asking_for_passwords(self):
        options = self._options("update", account_id=self.account["id"], revision=999, replace_passwords=True)
        with mock.patch.object(setup_cli.getpass, "getpass") as prompt:
            with self.assertRaises(MailError) as failure:
                setup_cli.run(self.app, options)
        self.assertEqual("configuration_changed", failure.exception.code)
        prompt.assert_not_called()

    def test_hidden_input_unavailable_fails_without_saving(self):
        with mock.patch.object(setup_cli.getpass, "getpass", side_effect=setup_cli.getpass.GetPassWarning):
            with self.assertRaises(MailError) as failure:
                setup_cli.run(self.app, self._options("add"))
        self.assertEqual("terminal_required", failure.exception.code)
        self.assertEqual(1, len(self.app.store.accounts()))

    def test_signin_waits_for_each_backend_interval(self):
        application = mock.Mock()
        application.manage.side_effect = [
            {"flow_id": "a" * 32, "verification_uri": "https://login.example.test/device", "user_code": "ABCD-EFGH", "interval": 5},
            {"status": "pending", "interval": 10},
            {"status": "connected", "account": self.account, "cleanup_pending": True},
        ]
        with mock.patch.object(setup_cli.time, "sleep") as sleep, contextlib.redirect_stderr(io.StringIO()):
            result = setup_cli.run(application, self._options("signin", account_id=self.account["id"]))
        self.assertEqual("connected", result["status"])
        self.assertTrue(result["cleanup_pending"])
        self.assertEqual([mock.call(5), mock.call(10)], sleep.call_args_list)
        self.assertEqual(3, application.manage.call_count)

    def test_signin_interrupt_cancels_only_temporary_flow(self):
        application = mock.Mock()
        application.manage.side_effect = [
            {"flow_id": "a" * 32, "verification_uri": "https://login.example.test/device", "user_code": "ABCD-EFGH", "interval": 5},
            {"cleanup_pending": False},
        ]
        with mock.patch.object(setup_cli.time, "sleep", side_effect=KeyboardInterrupt), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(KeyboardInterrupt):
                setup_cli.run(application, self._options("signin", account_id=self.account["id"]))
        application.manage.assert_called_with("cancel_microsoft_signin", {"flow_id": "a" * 32})

    def test_help_does_not_open_a_profile(self):
        with mock.patch.object(cli, "MailApplication") as application, contextlib.redirect_stdout(io.StringIO()) as output:
            with self.assertRaises(SystemExit) as result:
                cli.main(["accounts", "add", "--help"])
        self.assertEqual(0, result.exception.code)
        self.assertIn("--entry", output.getvalue())
        application.assert_not_called()

    def test_json_list_is_one_machine_readable_result(self):
        responses = []
        with mock.patch.object(cli, "data_directory", return_value=self.root), \
             mock.patch.object(cli, "MailApplication", return_value=self.app), \
             mock.patch.object(cli, "write", side_effect=responses.append), \
             contextlib.redirect_stdout(io.StringIO()) as output:
            result = cli.main(["accounts", "list", "--json"])
        self.assertEqual(0, result)
        self.assertEqual("", output.getvalue())
        self.assertEqual(1, len(responses))
        self.assertTrue(responses[0]["ok"])
        self.assertEqual(self.account["id"], responses[0]["result"][0]["id"])
