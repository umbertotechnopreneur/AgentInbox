"""Terminal setup policy checks with synthetic accounts and in-memory credentials."""

import contextlib
import copy
import io
import json
from unittest import mock

from agentinbox import cli, setup_cli, terminal_setup
from agentinbox.errors import MailError
from tests._support import MailApplicationCase


class TerminalSetupTests(MailApplicationCase):
    """Check guided permissions, cancellation, diagnostics and portable launch paths."""

    def _guided(self, answers, client_id=None):
        """Collect a draft with a fake terminal and scripted non-secret answers."""
        stream = io.StringIO("\n".join(answers) + "\n")
        with mock.patch.object(stream, "isatty", return_value=True), \
             mock.patch.object(terminal_setup.sys, "stdin", stream), \
             contextlib.redirect_stderr(io.StringIO()):
            return terminal_setup.account_draft(self.app, client_id)

    def test_gmail_guided_defaults_never_enable_sending(self):
        draft = self._guided(["", "reader@example.test", "", "", "", "", ""])
        self.assertEqual("gmail", draft["preset"])
        self.assertEqual(["INBOX"], draft["folders"])
        self.assertFalse(draft["send_enabled"])
        self.assertIsNone(draft["smtp"])
        self.assertEqual("imap.gmail.com", draft["incoming"]["host"])
        self.assertEqual(1, len(self.app.store.accounts()))

    def test_pop3_uses_one_inbox_and_sending_needs_explicit_yes(self):
        draft = self._guided(["gmail", "reader@example.test", "", "pop3", "yes", ""])
        self.assertEqual(["INBOX"], draft["folders"])
        self.assertEqual(995, draft["incoming"]["port"])
        self.assertTrue(draft["send_enabled"])
        self.assertEqual(465, draft["smtp"]["port"])

    def test_custom_starttls_has_mandatory_tls_and_protocol_ports(self):
        draft = self._guided(["custom", "reader@example.test", "", "imap",
                              "imap.example.test", "starttls", "", "", "INBOX,Archive", "", ""])
        self.assertEqual("starttls", draft["incoming"]["tls"])
        self.assertEqual(143, draft["incoming"]["port"])
        self.assertEqual(["INBOX", "Archive"], draft["folders"])

    def test_microsoft_guided_setup_always_selects_oauth(self):
        draft = self._guided(["outlook", "reader@example.test", "", "", "", "", ""],
                              "11111111-2222-4333-8444-555555555555")
        self.assertEqual("microsoft_oauth", draft["authentication"]["method"])
        self.assertEqual("consumers", draft["authentication"]["tenant"])

    def test_microsoft_requires_registration_before_more_questions(self):
        with self.assertRaises(MailError) as failure:
            self._guided(["microsoft365"])
        self.assertEqual("microsoft_configuration", failure.exception.code)

    def test_cancellation_and_eof_never_save(self):
        for answers in (["gmail", "reader@example.test", "", "", "", "", "no"], ["gmail"]):
            with self.subTest(answers=answers), self.assertRaises(MailError) as failure:
                self._guided(answers)
            self.assertEqual("setup_cancelled", failure.exception.code)
        self.assertEqual(1, len(self.app.store.accounts()))

    def test_noninteractive_guided_setup_fails_before_questions(self):
        with mock.patch.object(terminal_setup.sys.stdin, "isatty", return_value=False):
            with self.assertRaises(MailError) as failure:
                terminal_setup.account_draft(self.app)
        self.assertEqual("terminal_required", failure.exception.code)

    def test_bad_folder_selection_is_validated_before_passwords(self):
        with self.assertRaises(MailError):
            self._guided(["gmail", "reader@example.test", "", "", "INBOX,INBOX", ""])
        self.assertEqual(1, len(self.app.store.accounts()))

    def test_guided_add_uses_shared_save_and_hidden_password(self):
        draft = copy.deepcopy(self.draft)
        draft.update(id="", smtp=None, send_enabled=False)
        with mock.patch.object(cli, "data_directory", return_value=self.root), \
             mock.patch.object(cli, "MailApplication", return_value=self.app), \
             mock.patch.object(terminal_setup, "account_draft", return_value=draft), \
             mock.patch.object(setup_cli.getpass, "getpass", return_value="synthetic-secret"), \
             mock.patch.object(cli, "write") as output:
            self.assertEqual(0, cli.main(["accounts", "add", "--json"]))
        self.assertTrue(output.call_args.args[0]["ok"])
        self.assertEqual(2, len(self.app.store.accounts()))

    def test_partial_setup_file_options_fail_without_prompt(self):
        for options in (["--file", "missing.json"], ["--entry", "sample"]):
            with self.subTest(options=options), \
                 mock.patch.object(cli, "data_directory", return_value=self.root), \
                 mock.patch.object(cli, "MailApplication", return_value=self.app), \
                 mock.patch.object(terminal_setup, "account_draft") as guided, \
                 mock.patch.object(cli, "write") as output:
                self.assertEqual(1, cli.main(["accounts", "add", "--json", *options]))
                self.assertEqual("invalid_arguments", output.call_args.args[0]["error"]["code"])
                guided.assert_not_called()

    def test_mcp_config_keeps_virtual_environment_path_and_separate_arguments(self):
        interpreter = str(self.root / "venv with spaces" / "bin" / "python")
        with mock.patch.object(terminal_setup.sys, "executable", interpreter):
            result = terminal_setup.mcp_configuration(self.root)
        server = result["mcpServers"]["agentinbox"]
        self.assertEqual(interpreter, server["command"])
        self.assertEqual("-B", server["args"][0])
        self.assertTrue(server["args"][1].endswith("/python/run.py"))
        self.assertEqual("mcp", server["args"][-1])
        self.assertEqual(str(self.root), server["env"]["AGENTINBOX_DATA_DIR"])
        self.assertNotIn("password", json.dumps(result))

    def test_doctor_probe_removes_only_its_disposable_value(self):
        before = dict(self.vault.values)
        result = terminal_setup.diagnose(self.app, check_storage=True)
        self.assertEqual(before, self.vault.values)
        check = next(item for item in result["checks"] if item["name"] == "protected_storage")
        self.assertEqual("ok", check["status"])
        self.imap.assert_not_called()
        self.smtp.assert_not_called()

    def test_doctor_never_drains_preexisting_cleanup_entries(self):
        reference = "incoming-" + "b" * 32
        self.vault.put(reference, "synthetic-retired-password")
        self.app.store.queue_protected_cleanup([reference])
        queue_before = self.app.store.cleanup_path.read_bytes()
        values_before = dict(self.vault.values)
        with mock.patch.object(self.vault, "delete", wraps=self.vault.delete) as delete:
            result = terminal_setup.diagnose(self.app, check_storage=True)
        self.assertEqual(values_before, self.vault.values)
        self.assertEqual(queue_before, self.app.store.cleanup_path.read_bytes())
        self.assertTrue(result["cleanup_pending"])
        self.assertEqual(1, result["cleanup_pending_count"])
        self.assertEqual(1, delete.call_count)
        self.assertTrue(delete.call_args.args[0].startswith("signin-"))
        probe = next(check for check in result["checks"] if check["name"] == "protected_storage")
        self.assertEqual("ok", probe["status"])

    def test_doctor_unavailable_service_is_failure_with_no_plaintext_fallback(self):
        with mock.patch.object(terminal_setup.sys, "platform", "linux"), \
             mock.patch.object(terminal_setup.ctypes.util, "find_library", return_value=None):
            result = terminal_setup.diagnose(self.app)
        self.assertFalse(result["ready"])
        self.assertFalse((self.root / "protected").exists())

    def test_doctor_preserves_failed_probe_cleanup_for_retry(self):
        before = set(self.vault.values)
        with mock.patch.object(self.vault, "delete", side_effect=MailError("secure_storage_unavailable", "Synthetic locked keyring.")):
            result = terminal_setup.diagnose(self.app, check_storage=True)
        self.assertFalse(result["ready"])
        self.assertTrue(self.app.store.cleanup_status()["cleanup_pending"])
        self.assertEqual(1, len(set(self.vault.values) - before))
        self.assertFalse(self.app.store.retry_cleanup()["cleanup_pending"])
        self.assertEqual(before, set(self.vault.values))

    def test_doctor_reports_cleanup_when_probe_reference_cannot_be_queued(self):
        with mock.patch.object(self.vault, "delete", side_effect=MailError("secure_storage_unavailable", "Synthetic locked keyring.")), \
             mock.patch.object(self.app.store, "queue_protected_cleanup", side_effect=MailError("profile_busy", "Synthetic busy profile.")):
            result = terminal_setup.diagnose(self.app, check_storage=True)
        self.assertFalse(result["ready"])
        self.assertTrue(result["cleanup_pending"])
        self.assertEqual("cleanup_unrecorded", result["cleanup_error"])
        check = next(item for item in result["checks"] if item["name"] == "protected_storage")
        self.assertIn("signin-", check["message"])

    def test_doctor_json_failure_has_nonzero_exit_status(self):
        with mock.patch.object(cli, "data_directory", return_value=self.root), \
             mock.patch.object(cli, "MailApplication", return_value=self.app), \
             mock.patch.object(terminal_setup, "diagnose", return_value={"ready": False, "checks": []}), \
             mock.patch.object(cli, "write") as output:
            self.assertEqual(1, cli.main(["doctor", "--json"]))
        self.assertFalse(output.call_args.args[0]["result"]["ready"])
