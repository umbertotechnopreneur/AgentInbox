#!/usr/bin/env python3
"""Run local synthetic regressions for the manual runner; never contact providers."""

import contextlib
import copy
import importlib.util
import io
import json
import os
import subprocess
import sys
import tempfile
import unittest
from datetime import datetime
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


sys.dont_write_bytecode = True
SPEC = importlib.util.spec_from_file_location("real_provider_check", Path(__file__).with_name("real-provider-check.py"))
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
PRIVATE = "synthetic-private-content@example.test"


class FakeSession:
    """Supply in-memory MCP responses and record requested account/calendar scopes."""

    def __init__(self, count=2):
        self.accounts = [{
            "id": f"synthetic-account-{ordinal}",
            "provider": "google" if ordinal % 2 else "microsoft",
            "display_name": PRIVATE, "email_address": f"account{ordinal}@example.test",
            "mail_read_enabled": True, "calendar_read_enabled": True,
        } for ordinal in range(1, count + 1)]
        self.calendar_counts = {account["id"]: 1 for account in self.accounts}
        self.no_events = set()
        self.no_mail = set()
        self.failures = set()
        self.hook = None
        self.tools = [{"name": name, "annotations": {"readOnlyHint": True, "destructiveHint": False}}
                      for name in sorted(RUNNER.READ_ONLY_TOOLS)]
        self.calls = []
        self.details = {}
        self.started = False
        self.closed = False

    def start(self, executable):
        self.started = True

    def close(self):
        self.closed = True

    def request(self, method, params=None, work_units=1):
        if method == "initialize":
            return {"protocolVersion": "2025-11-25", "capabilities": {}}
        if method == "tools/list":
            return {"tools": self.tools}
        raise AssertionError("Unexpected protocol method in the synthetic fixture.")

    def send(self, message):
        if message != {"method": "notifications/initialized"}:
            raise AssertionError("Unexpected notification in the synthetic fixture.")

    def calendars(self, account_ids):
        return [{"reference": f"calendar/{account_id}/{index}", "account_id": account_id,
                 "name": PRIVATE, "primary": index == 0, "time_zone": "UTC"}
                for account_id in account_ids for index in range(self.calendar_counts[account_id])]

    def content(self, name, arguments, work_units=1):
        if name not in RUNNER.READ_ONLY_TOOLS:
            raise AssertionError("Unexpected non-read-only tool in the synthetic fixture.")
        self.calls.append((name, copy.deepcopy(arguments), work_units))
        if name == "get_status":
            result = {"read_only": True}
        elif name == "list_accounts":
            result = {"accounts": copy.deepcopy(self.accounts)}
        elif name in {"read_mail", "read_event"}:
            result = copy.deepcopy(self.details[arguments["reference"]])
            result.update({"text": PRIVATE} if name == "read_mail" else {"description": PRIVATE})
        else:
            if name == "search_events":
                calendars = {item["reference"]: item for item in self.calendars(self.calendar_counts)}
                selected = [calendars[reference] for reference in arguments["calendarReferences"]]
                account_ids = list(dict.fromkeys(item["account_id"] for item in selected))
            else:
                account_ids = arguments["accountIds"]
            failed = [account_id for account_id in account_ids if (name, account_id) in self.failures]
            healthy = [account_id for account_id in account_ids if account_id not in failed]
            result = {"searched_account_ids": list(account_ids), "coverage_complete": not failed,
                      "failed_accounts": [{"account_id": account_id, "reason": PRIVATE} for account_id in failed]}
            if name == "list_calendars":
                result["calendars"] = self.calendars(healthy)
            elif name == "search_mail":
                items = [{"reference": f"mail/{account_id}/{bool(arguments.get('cursor'))}",
                          "account_id": account_id, "subject": PRIVATE, "sender": PRIVATE,
                          "received_at": "2026-09-01T12:00:00+00:00", "preview": PRIVATE}
                         for account_id in healthy if account_id not in self.no_mail]
                result["items"] = items[:arguments["limit"]]
                result["next_cursor"] = "synthetic-mail-cursor" if items and not arguments.get("cursor") else None
                self.details.update({item["reference"]: item for item in items})
            elif name == "search_events":
                items = [{"reference": f"event/{item['reference']}/{bool(arguments.get('cursor'))}",
                          "calendar_reference": item["reference"], "account_id": item["account_id"],
                          "title": PRIVATE, "start": arguments["start"], "end": arguments["end"],
                          "all_day": False, "cancelled": False, "location": PRIVATE}
                         for item in selected if item["account_id"] in healthy and item["account_id"] not in self.no_events]
                result["events"] = items[:arguments["limit"]]
                result["next_cursor"] = "synthetic-event-cursor" if items and not arguments.get("cursor") else None
                self.details.update({item["reference"]: item for item in items})
            else:
                raise AssertionError("Unexpected read tool in the synthetic fixture.")
        return self.hook(name, arguments, result) if self.hook else result


class RunnerRegressionTests(unittest.TestCase):
    """Exercise the runner entry point, failure boundaries and real validation logic."""

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="agentinbox-manual-runner-tests-")
        self.addCleanup(self.directory.cleanup)
        self.executable = Path(self.directory.name) / "synthetic-agentinbox.exe"

    def invoke(self, session=None, argv=None, environment=None):
        stdout, stderr = io.StringIO(), io.StringIO()
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.dict(os.environ, environment or {}, clear=True))
            stack.enter_context(mock.patch.object(sys, "argv", ["real-provider-check.py"] +
                                                 (argv if argv is not None else [str(self.executable)])))
            stack.enter_context(contextlib.redirect_stdout(stdout))
            stack.enter_context(contextlib.redirect_stderr(stderr))
            if session is not None:
                stack.enter_context(mock.patch.object(RUNNER, "McpSession", return_value=session))
            code = RUNNER.main()
        output = stdout.getvalue()
        self.assertNotIn(PRIVATE, output + stderr.getvalue())
        self.assertNotIn("Traceback", output + stderr.getvalue())
        self.assertEqual("", stderr.getvalue())
        return code, json.loads(output)

    def test_ci_refuses_before_path_resolution_or_subprocess(self):
        for environment in ({"CI": "true"}, {"GITHUB_ACTIONS": "true"}, {"TF_BUILD": "true"},
                            {"JENKINS_URL": "https://ci.example.test"}):
            with self.subTest(environment=environment), mock.patch.object(Path, "resolve") as resolve, \
                    mock.patch.object(RUNNER.subprocess, "Popen") as start:
                code, result = self.invoke(environment=environment)
                self.assertEqual(1, code)
                self.assertIn("cannot run in CI", result["error"])
                resolve.assert_not_called()
                start.assert_not_called()

    def test_disabled_generic_ci_flag_allows_local_synthetic_run(self):
        code, result = self.invoke(FakeSession(), environment={"CI": "false"})
        self.assertEqual(0, code)
        self.assertTrue(result["success"])

    def test_missing_executable_is_sanitized_before_subprocess(self):
        with mock.patch.object(RUNNER.subprocess, "Popen") as start:
            code, result = self.invoke(argv=[str(Path(self.directory.name) / PRIVATE)])
        self.assertEqual(1, code)
        self.assertIn("Could not start", result["error"])
        start.assert_not_called()

    def test_missing_argument_is_sanitized(self):
        with mock.patch.object(RUNNER.subprocess, "Popen") as start:
            code, result = self.invoke(argv=[])
        self.assertEqual(1, code)
        self.assertIn("Usage:", result["error"])
        start.assert_not_called()

    def test_unexpected_argument_does_not_echo_its_value(self):
        code, result = self.invoke(argv=[str(self.executable), "--" + PRIVATE])
        self.assertEqual(1, code)
        self.assertIn("Usage:", result["error"])

    def test_subprocess_startup_failure_is_sanitized(self):
        self.executable.touch()
        with mock.patch.object(RUNNER.subprocess, "Popen", side_effect=OSError(PRIVATE)):
            code, result = self.invoke()
        self.assertEqual(1, code)
        self.assertIn("Could not start", result["error"])

    def test_non_protocol_stdout_is_sanitized(self):
        self.executable.touch()
        process = SimpleNamespace(stdin=io.StringIO(), stdout=io.StringIO(PRIVATE + "\n"), wait=mock.Mock(return_value=0))
        with mock.patch.object(RUNNER.subprocess, "Popen", return_value=process) as start:
            code, result = self.invoke()
        self.assertEqual(1, code)
        self.assertIn("protocol data", result["error"])
        self.assertIs(start.call_args.kwargs["stderr"], subprocess.DEVNULL)

    def assert_minimum_accounts(self, count):
        session = FakeSession(count)
        code, result = self.invoke(session)
        self.assertEqual(1, code)
        self.assertEqual(count, result["account_count"])
        self.assertIn("at least two", result["error"])
        self.assertEqual(["get_status", "list_accounts"], [call[0] for call in session.calls])
        self.assertTrue(session.closed)

    def test_zero_accounts_rejected(self):
        self.assert_minimum_accounts(0)

    def test_one_account_rejected(self):
        self.assert_minimum_accounts(1)

    def assert_dynamic_accounts(self, count):
        session = FakeSession(count)
        code, result = self.invoke(session)
        self.assertEqual(0, code)
        self.assertTrue(result["success"])
        self.assertEqual(count, result["account_count"])
        self.assertEqual(list(range(1, count + 1)), [account["ordinal"] for account in result["account_checks"]])
        self.assertEqual(count, result["mail_sample_count"])
        self.assertEqual(count, result["event_sample_count"])
        self.assertEqual("summary_detail_consistency_only", result["comparison"])
        self.assertEqual(0, result["failed_checks"])
        scopes = [(arguments["accountIds"], budget) for name, arguments, budget in session.calls
                  if name == "search_mail" and len(arguments["accountIds"]) > 1]
        self.assertEqual([([account["id"] for account in session.accounts], count)], scopes)

    def test_two_accounts_are_discovered_dynamically(self):
        self.assert_dynamic_accounts(2)

    def test_three_accounts_are_discovered_dynamically(self):
        self.assert_dynamic_accounts(3)

    def test_four_accounts_are_discovered_dynamically(self):
        self.assert_dynamic_accounts(4)

    def test_provider_failure_keeps_healthy_account_and_other_category(self):
        session = FakeSession()
        session.failures.add(("search_mail", session.accounts[0]["id"]))
        code, result = self.invoke(session)
        first, second = [account["checks"] for account in result["account_checks"]]
        self.assertEqual(1, code)
        self.assertFalse(result["success"])
        self.assertEqual("failed", first["mail_search"]["status"])
        self.assertEqual("passed", first["event_detail"]["status"])
        self.assertEqual("passed", second["mail_detail"]["status"])
        self.assertEqual("passed", second["event_detail"]["status"])
        self.assertEqual("failed", result["mixed_checks"]["mixed_mail"]["status"])
        self.assertEqual("failed", first["mixed_mail"]["status"])

    def test_unexpected_category_error_is_sanitized_and_other_checks_continue(self):
        session = FakeSession()
        def hook(name, arguments, result):
            if name == "search_mail" and arguments["accountIds"] == [session.accounts[0]["id"]]:
                raise ValueError(PRIVATE)
            return result
        session.hook = hook
        code, result = self.invoke(session)
        self.assertEqual(1, code)
        self.assertEqual("passed", result["account_checks"][1]["checks"]["mail_detail"]["status"])
        self.assertEqual("passed", result["account_checks"][0]["checks"]["event_detail"]["status"])

    def test_missing_consent_and_empty_events_are_explicit_skips(self):
        session = FakeSession()
        session.accounts[0]["mail_read_enabled"] = False
        session.no_events.add(session.accounts[0]["id"])
        session.accounts[1]["calendar_read_enabled"] = False
        code, result = self.invoke(session)
        first, second = [account["checks"] for account in result["account_checks"]]
        self.assertEqual(0, code)
        self.assertEqual("skipped", first["mail_search"]["status"])
        self.assertIn("consent", first["mail_search"]["reason"])
        self.assertEqual("passed", first["event_search"]["status"])
        self.assertIn("No events", first["event_detail"]["reason"])
        self.assertEqual("skipped", second["calendar_discovery"]["status"])
        self.assertEqual(0, result["failed_checks"])

    def test_no_granted_reads_cannot_report_success(self):
        session = FakeSession()
        for account in session.accounts:
            account["mail_read_enabled"] = account["calendar_read_enabled"] = False
        code, result = self.invoke(session)
        self.assertEqual(1, code)
        self.assertFalse(result["success"])
        self.assertEqual(0, result["passed_checks"])
        self.assertIn("No provider reads", result["error"])

    def test_empty_mail_and_calendars_skip_samples(self):
        session = FakeSession()
        session.no_mail.add(session.accounts[0]["id"])
        session.calendar_counts[session.accounts[0]["id"]] = 0
        code, result = self.invoke(session)
        checks = result["account_checks"][0]["checks"]
        self.assertEqual(0, code)
        self.assertEqual("passed", checks["mail_search"]["status"])
        self.assertEqual("skipped", checks["mail_detail"]["status"])
        self.assertIn("No readable calendars", checks["event_search"]["reason"])

    def test_same_count_wrong_account_scope_fails(self):
        session = FakeSession()
        def hook(name, arguments, result):
            if name == "search_mail" and arguments["accountIds"] == [session.accounts[0]["id"]]:
                result["searched_account_ids"] = [session.accounts[1]["id"]]
            return result
        session.hook = hook
        code, result = self.invoke(session)
        self.assertEqual(1, code)
        self.assertIn("unexpected account scope", result["account_checks"][0]["checks"]["mail_search"]["error"])

    def test_result_from_unselected_account_fails(self):
        session = FakeSession()
        def hook(name, arguments, result):
            if name == "search_mail" and arguments["accountIds"] == [session.accounts[0]["id"]]:
                result["items"][0]["account_id"] = session.accounts[1]["id"]
            return result
        session.hook = hook
        code, result = self.invoke(session)
        self.assertEqual(1, code)
        self.assertIn("unselected account", result["account_checks"][0]["checks"]["mail_search"]["error"])

    def test_calendar_batches_and_cursors_preserve_scope_and_budget(self):
        session = FakeSession()
        session.calendar_counts[session.accounts[0]["id"]] = 41
        code, result = self.invoke(session)
        self.assertEqual(0, code)
        searches = [(arguments, budget) for name, arguments, budget in session.calls if name == "search_events"]
        first_account = [(arguments, budget) for arguments, budget in searches
                         if session.accounts[0]["id"] in arguments["calendarReferences"][0]]
        self.assertEqual([20, 20, 20, 20, 1, 1], [len(arguments["calendarReferences"]) for arguments, _ in first_account])
        for arguments, budget in searches:
            self.assertLessEqual(len(arguments["calendarReferences"]), 20)
            self.assertEqual(len(arguments["calendarReferences"]), budget)
            start, end = datetime.fromisoformat(arguments["start"]), datetime.fromisoformat(arguments["end"])
            self.assertIsNotNone(start.utcoffset())
            self.assertGreater((end - start).total_seconds(), 0)
            self.assertLessEqual((end - start).total_seconds(), 31 * 86400)
        for index in range(0, len(first_account), 2):
            first, following = first_account[index][0], first_account[index + 1][0]
            self.assertEqual(first["calendarReferences"], following["calendarReferences"])
            self.assertIn("cursor", following)
        self.assertEqual(3, result["account_checks"][0]["checks"]["event_search"]["passed_batches"])

    def test_bad_event_cursor_calendar_cannot_be_hidden_by_later_batch(self):
        session = FakeSession()
        session.calendar_counts[session.accounts[0]["id"]] = 21
        def hook(name, arguments, result):
            if name == "search_events" and arguments.get("cursor") and len(arguments["calendarReferences"]) == 20:
                result["events"][0]["calendar_reference"] = "unselected-calendar"
            return result
        session.hook = hook
        code, result = self.invoke(session)
        self.assertEqual(1, code)
        self.assertEqual("failed", result["account_checks"][0]["checks"]["event_cursor"]["status"])
        self.assertEqual("passed", result["account_checks"][1]["checks"]["event_cursor"]["status"])

    def test_changed_mail_cursor_scope_fails(self):
        session = FakeSession()
        def hook(name, arguments, result):
            if name == "search_mail" and arguments.get("cursor"):
                result["searched_account_ids"] = ["unselected-account"]
            return result
        session.hook = hook
        code, result = self.invoke(session)
        self.assertEqual(1, code)
        self.assertEqual("failed", result["account_checks"][0]["checks"]["mail_cursor"]["status"])

    def test_same_tool_count_cannot_replace_read_tool_with_write_tool(self):
        session = FakeSession()
        session.tools[0]["name"] = "send_mail"
        code, result = self.invoke(session)
        self.assertEqual(1, code)
        self.assertIn("read-only allowlist", result["error"])
        self.assertEqual([], session.calls)

    def test_write_tool_call_is_refused_before_protocol_request(self):
        session = RUNNER.McpSession()
        with mock.patch.object(session, "request") as request:
            with self.assertRaisesRegex(RUNNER.CheckFailure, "read-only allowlist"):
                session.content("create_event", {})
        request.assert_not_called()


if __name__ == "__main__":
    if RUNNER.is_ci(os.environ):
        raise SystemExit("These synthetic manual-runner regressions are local-only; no CI integration is configured.")
    unittest.main(verbosity=2)
