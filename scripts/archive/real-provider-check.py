#!/usr/bin/env python3
"""Run opt-in, local read checks; report consistency, never provider-side comparison."""

import argparse
import json
import os
import queue
import subprocess
import threading
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path


READ_ONLY_TOOLS = frozenset({
    "get_status", "list_accounts", "search_mail", "search_unread_mail", "search_mail_by_date", "read_mail",
    "list_calendars", "search_events", "read_event",
})
CI_FLAGS = ("CI", "GITHUB_ACTIONS", "GITLAB_CI", "TF_BUILD", "CIRCLECI",
            "BUILDKITE", "TRAVIS", "APPVEYOR", "DRONE", "CODEBUILD_CI")
CI_MARKERS = ("JENKINS_URL", "JENKINS_HOME", "TEAMCITY_VERSION",
              "BITBUCKET_BUILD_NUMBER", "CODEBUILD_BUILD_ID", "BUILD_BUILDID")
MAX_CALENDAR_BATCH = 20
EVENT_WINDOW_DAYS = 30


class CheckFailure(RuntimeError):
    """A failure containing only a fixed local message, never provider data."""


class LocalArgumentParser(argparse.ArgumentParser):
    """Avoid echoing unexpected command arguments into diagnostic output."""

    def error(self, message):
        raise CheckFailure("Usage: real-provider-check.py <agentinbox-executable>")


def require(condition, message):
    if not condition:
        raise CheckFailure(message)


def is_ci(environment):
    """Recognize common CI runners without reading local app configuration."""
    disabled = {"", "0", "false", "no", "off"}
    return (
        any(environment.get(name, "").strip().lower() not in disabled for name in CI_FLAGS)
        or any(environment.get(name, "").strip() for name in CI_MARKERS)
    )


def search_terms(provider):
    if provider == "google":
        return ["newer_than:3650d", "in:anywhere"]
    if provider == "microsoft":
        return ["Microsoft", "account", "security", "welcome"]
    return ["mail"]


class McpSession:
    """Keep provider responses in memory and suppress all child diagnostics."""

    def __init__(self):
        self.process = None
        self.messages = queue.Queue(maxsize=128)
        self.next_identifier = 1
        self.transport_failed = False

    def start(self, executable):
        try:
            resolved = executable.resolve(strict=True)
            require(resolved.is_file(), "The executable path must point to a file.")
            self.process = subprocess.Popen(
                [str(resolved), "--stdio"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                stderr=subprocess.DEVNULL, text=True, encoding="utf-8",
                env={**os.environ, "AGENTINBOX_LOG_LEVEL": "fatal"},
            )
        except CheckFailure:
            raise
        except Exception:
            raise CheckFailure("Could not start the supplied AgentInbox executable.") from None
        threading.Thread(target=self._consume_stdout, daemon=True).start()

    def _consume_stdout(self):
        try:
            while True:
                # Bound individual frames; never retain diagnostics or dump frames.
                line = self.process.stdout.readline(4 * 1024 * 1024 + 1)
                if not line:
                    raise CheckFailure("MCP stdout closed unexpectedly.")
                require(len(line) <= 4 * 1024 * 1024, "MCP response exceeded the size limit.")
                value = json.loads(line)
                require(isinstance(value, dict), "MCP stdout contained an invalid response.")
                require(value.get("jsonrpc") == "2.0", "MCP protocol version was invalid.")
                self.messages.put(value)
        except CheckFailure as error:
            self.messages.put(error)
        except Exception:
            self.messages.put(CheckFailure("MCP stdout could not be decoded as protocol data."))

    def send(self, message):
        require(not self.transport_failed, "MCP transport is no longer available.")
        try:
            self.process.stdin.write(json.dumps({"jsonrpc": "2.0", **message}) + "\n")
            self.process.stdin.flush()
        except Exception:
            self.transport_failed = True
            raise CheckFailure("Could not write to MCP stdin.") from None

    def request(self, method, params=None, work_units=1):
        identifier = self.next_identifier
        self.next_identifier += 1
        message = {"id": identifier, "method": method}
        if params is not None:
            message["params"] = params
        self.send(message)
        # Allow each sequential provider operation its 30-second application budget.
        deadline = time.monotonic() + 15 + 35 * max(1, work_units)
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                self.transport_failed = True
                raise CheckFailure("MCP response timed out.")
            try:
                item = self.messages.get(timeout=remaining)
            except queue.Empty:
                self.transport_failed = True
                raise CheckFailure("MCP response timed out.") from None
            if isinstance(item, CheckFailure):
                self.transport_failed = True
                raise item
            if item.get("id") == identifier:
                require("error" not in item, "MCP returned a protocol error.")
                require(isinstance(item.get("result"), dict), "MCP result was malformed.")
                return item["result"]

    def content(self, name, arguments, work_units=1):
        require(name in READ_ONLY_TOOLS, "The requested tool is outside the read-only allowlist.")
        result = self.request("tools/call", {"name": name, "arguments": arguments}, work_units)
        require(result.get("isError", False) is False, "The read-only MCP tool failed.")
        try:
            value = result.get("structuredContent")
            if value is None:
                value = json.loads(result["content"][0]["text"])
            require(isinstance(value, dict), "MCP tool content was malformed.")
            return value
        except CheckFailure:
            raise
        except Exception:
            raise CheckFailure("MCP tool content could not be decoded.") from None

    def close(self):
        if self.process is None:
            return
        try:
            self.process.stdin.close()
        except Exception:
            pass
        try:
            self.process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=5)


def validate_page(result, expected_ids, item_key, calendar_scope=None):
    """Check exact coverage and item membership before consuming returned data."""
    expected = set(expected_ids)
    searched = result["searched_account_ids"]
    require(isinstance(searched, list) and len(searched) == len(set(searched))
            and set(searched) == expected, "The response used an unexpected account scope.")
    failures = result["failed_accounts"]
    require(isinstance(failures, list), "Account failure coverage was malformed.")
    failed_ids = [failure["account_id"] for failure in failures]
    require(len(failed_ids) == len(set(failed_ids)) and set(failed_ids) <= expected,
            "The response reported failures outside the selected account scope.")
    require(result["coverage_complete"] is (not failed_ids),
            "The coverage flag did not match the reported account failures.")
    items = result[item_key]
    require(isinstance(items, list), "The result collection was malformed.")
    for item in items:
        require(item["account_id"] in expected, "A result belongs to an unselected account.")
        require(isinstance(item["reference"], str) and bool(item["reference"]),
                "A result reference was missing.")
        if calendar_scope is not None:
            require(calendar_scope.get(item["calendar_reference"]) == item["account_id"],
                    "An event belongs to an unselected calendar or account.")
    return failed_ids


def require_complete_page(result, expected_ids, item_key, calendar_scope=None):
    failures = validate_page(result, expected_ids, item_key, calendar_scope)
    require(not failures, "The provider reported incomplete account coverage.")
    return result


def skipped(reason):
    return {"status": "skipped", "reason": reason}


def run_step(checks, name, operation):
    """Keep independent failures while allowing other accounts/categories to run."""
    try:
        value = operation()
        checks[name] = {"status": "passed"}
        return value
    except CheckFailure as error:
        checks[name] = {"status": "failed", "error": str(error)}
    except Exception:
        checks[name] = {"status": "failed", "error": "An unexpected response prevented this check."}
    return None


def check_mail(session, account, checks):
    names = ("mail_search", "mail_detail", "mail_cursor")
    if not account["mail_read_enabled"]:
        checks.update({name: skipped("Mail read consent is not granted.") for name in names})
        return
    checks.update({name: skipped("Mail search did not complete.") for name in names})
    account_ids = [account["id"]]

    def search():
        for query in search_terms(account["provider"]):
            result = require_complete_page(session.content(
                "search_mail", {"query": query, "accountIds": account_ids, "limit": 3}), account_ids, "items")
            if result["items"]:
                return query, result
        return query, result

    found = run_step(checks, "mail_search", search)
    if found is None:
        return
    query, result = found
    if result["items"]:
        item = result["items"][0]

        def read():
            detail = session.content("read_mail", {"reference": item["reference"], "maxCharacters": 1000})
            require(all(detail[key] == item[key] for key in
                        ("reference", "account_id", "subject", "sender", "received_at"))
                    and isinstance(detail["text"], str) and len(detail["text"]) <= 1000,
                    "The mail summary and detail were inconsistent.")
            return True

        run_step(checks, "mail_detail", read)
    else:
        checks["mail_detail"] = skipped("No mail samples matched the bounded search candidates.")
    if result.get("next_cursor"):
        run_step(checks, "mail_cursor", lambda: require_complete_page(session.content("search_mail", {
            "query": query, "accountIds": account_ids, "limit": 3, "cursor": result["next_cursor"],
        }), account_ids, "items"))
    else:
        checks["mail_cursor"] = skipped("The search did not return a continuation cursor.")


def check_calendars(session, account, checks, window):
    names = ("calendar_discovery", "event_search", "event_detail", "event_cursor")
    if not account["calendar_read_enabled"]:
        checks.update({name: skipped("Calendar read consent is not granted.") for name in names})
        return
    checks.update({name: skipped("Calendar discovery did not complete.") for name in names})
    account_ids = [account["id"]]
    result = run_step(checks, "calendar_discovery", lambda: require_complete_page(
        session.content("list_calendars", {"accountIds": account_ids}), account_ids, "calendars"))
    if result is None:
        return
    calendars = result["calendars"]
    checks["calendar_discovery"]["calendar_count"] = len(calendars)
    if not calendars:
        for name in names[1:]:
            checks[name] = skipped("No readable calendars were returned.")
        return
    references = [calendar["reference"] for calendar in calendars]
    require(len(references) == len(set(references)), "Calendar references were not unique.")
    checks.pop("event_search")
    checks["event_detail"] = skipped("No events were found in the 30-day window.")
    checks["event_cursor"] = skipped("The event searches did not return a continuation cursor.")
    sample_checked = False
    successful_batches = 0
    failed_batches = 0
    for offset in range(0, len(calendars), MAX_CALENDAR_BATCH):
        batch = calendars[offset:offset + MAX_CALENDAR_BATCH]
        calendar_scope = {item["reference"]: item["account_id"] for item in batch}
        arguments = {**window, "calendarReferences": list(calendar_scope), "limit": 3}
        batch_checks = {}
        events = run_step(batch_checks, "search", lambda: require_complete_page(
            session.content("search_events", arguments, len(batch)), account_ids, "events", calendar_scope))
        if events is None:
            failed_batches += 1
            checks["event_search"] = batch_checks["search"]
            continue
        successful_batches += 1
        if events["events"] and not sample_checked:
            sample_checked = True
            event = events["events"][0]

            def read():
                detail = session.content("read_event", {
                    "reference": event["reference"], "maxDescriptionCharacters": 1000,
                })
                require(all(detail[key] == event[key] for key in (
                    "reference", "calendar_reference", "account_id", "title", "start", "end",
                    "all_day", "cancelled", "location"))
                    and isinstance(detail["description"], str) and len(detail["description"]) <= 1000,
                    "The event summary and detail were inconsistent.")
                return True

            run_step(checks, "event_detail", read)
        if events.get("next_cursor"):
            cursor_checks = {}
            run_step(cursor_checks, "event_cursor", lambda: require_complete_page(
                session.content("search_events", {**arguments, "cursor": events["next_cursor"]}, len(batch)),
                account_ids, "events", calendar_scope))
            # Never overwrite an earlier failure with a later successful batch.
            if checks["event_cursor"]["status"] != "failed":
                checks["event_cursor"] = cursor_checks["event_cursor"]
    if not failed_batches:
        checks["event_search"] = {"status": "passed"}
    checks["event_search"].update({"passed_batches": successful_batches, "failed_batches": failed_batches})
    if failed_batches and not sample_checked:
        checks["event_detail"] = skipped("No event sample was available because some searches failed.")


def check_mixed(session, accounts, account_checks, checks, name, consent, tool, collection, extra=None):
    account_ids = [account["id"] for account in accounts if account[consent]]
    if len(account_ids) < 2:
        checks[name] = skipped("This mixed-account check needs two accounts with the required read consent.")
        return

    def search():
        page = session.content(tool, {"accountIds": account_ids, **(extra or {})}, len(account_ids))
        failed_ids = validate_page(page, account_ids, collection)
        ordinals = {account["id"]: ordinal for ordinal, account in enumerate(accounts)}
        for failed_id in failed_ids:
            account_checks[ordinals[failed_id]]["checks"][name] = {
                "status": "failed", "error": "This account failed during the mixed-account check.",
            }
        require(not failed_ids, "Mixed-account coverage was incomplete.")
        return True

    run_step(checks, name, search)


def run_checks(session, summary):
    session.request("initialize", {
        "protocolVersion": "2025-11-25", "capabilities": {},
        "clientInfo": {"name": "agentinbox-real-provider-check", "version": "2.0.0"},
    })
    session.send({"method": "notifications/initialized"})
    tools = session.request("tools/list")["tools"]
    require(len(tools) == len(READ_ONLY_TOOLS)
            and {tool["name"] for tool in tools} == READ_ONLY_TOOLS,
            "The MCP tool surface does not match the read-only allowlist.")
    require(all(tool.get("annotations", {}).get("readOnlyHint") is True
                and tool.get("annotations", {}).get("destructiveHint") is False for tool in tools),
            "Read-only, non-destructive tool annotations are required.")
    summary["read_only_tool_count"] = len(tools)
    require(session.content("get_status", {})["read_only"] is True,
            "The executable did not report read-only mode.")
    accounts = session.content("list_accounts", {})["accounts"]
    require(isinstance(accounts, list), "The account list was malformed.")
    summary["account_count"] = len(accounts)
    require(len(accounts) >= 2, "Real multi-account checks require at least two connected accounts.")
    account_ids = [account["id"] for account in accounts]
    require(all(isinstance(account_id, str) and account_id for account_id in account_ids)
            and len(account_ids) == len(set(account_ids)), "The account list contains invalid or duplicate IDs.")
    require(all(account["provider"] in {"google", "microsoft"}
                and isinstance(account["mail_read_enabled"], bool)
                and isinstance(account["calendar_read_enabled"], bool) for account in accounts),
            "The account provider or consent flags were unexpected.")
    end = datetime.now(timezone.utc).replace(hour=0, minute=0, second=0, microsecond=0)
    start = end - timedelta(days=EVENT_WINDOW_DAYS)
    require(0 < (end - start).total_seconds() <= 31 * 24 * 60 * 60,
            "The event window must be positive and no longer than 31 days.")
    window = {"start": start.isoformat(), "end": end.isoformat()}
    summary["account_checks"] = []
    for ordinal, account in enumerate(accounts, start=1):
        check = {"ordinal": ordinal, "provider": account["provider"], "checks": {}}
        summary["account_checks"].append(check)
        # These outer boundaries also handle malformed data without losing later accounts.
        run_step(check["checks"], "mail_category", lambda: check_mail(session, account, check["checks"]))
        run_step(check["checks"], "calendar_category", lambda: check_calendars(session, account, check["checks"], window))
        for name in ("mail_category", "calendar_category"):
            if check["checks"][name]["status"] == "passed":
                del check["checks"][name]

    mixed = summary["mixed_checks"] = {}
    check_mixed(session, accounts, summary["account_checks"], mixed, "mixed_mail", "mail_read_enabled",
                "search_mail", "items", {"query": "Microsoft", "limit": 8})
    check_mixed(session, accounts, summary["account_checks"], mixed, "mixed_calendars", "calendar_read_enabled",
                "list_calendars", "calendars")
    account_results = [result for account in summary["account_checks"] for result in account["checks"].values()]
    results = account_results + list(mixed.values())
    summary["passed_checks"] = sum(result["status"] == "passed" for result in results)
    summary["failed_checks"] = sum(result["status"] == "failed" for result in results)
    summary["skipped_checks"] = sum(result["status"] == "skipped" for result in results)
    summary["mail_account_count"] = sum(account["mail_read_enabled"] for account in accounts)
    summary["calendar_account_count"] = sum(account["calendar_read_enabled"] for account in accounts)
    summary["mail_sample_count"] = sum(account["checks"].get("mail_detail", {}).get("status") == "passed"
                                       for account in summary["account_checks"])
    summary["event_sample_count"] = sum(account["checks"].get("event_detail", {}).get("status") == "passed"
                                        for account in summary["account_checks"])
    summary["calendar_count"] = sum(account["checks"].get("calendar_discovery", {}).get("calendar_count", 0)
                                    for account in summary["account_checks"])
    summary["success"] = summary["failed_checks"] == 0 and summary["passed_checks"] > 0
    if not summary["passed_checks"] and not summary["failed_checks"]:
        summary["error"] = "No provider reads could be checked; grant read consent to connected accounts."


def main():
    summary = {"success": False, "manual_only": True, "comparison": "summary_detail_consistency_only"}
    session = McpSession()
    try:
        # This guard precedes path resolution, child startup and credential access.
        require(not is_ci(os.environ), "Real-provider checks are local-only and cannot run in CI.")
        parser = LocalArgumentParser(description=__doc__)
        parser.add_argument("executable", type=Path, help="Path to the local agentinbox or agentinbox.exe")
        args = parser.parse_args()
        session.start(args.executable)
        run_checks(session, summary)
    except CheckFailure as error:
        summary["success"] = False
        summary["error"] = str(error)
    except KeyboardInterrupt:
        summary["success"] = False
        summary["error"] = "The local check was cancelled."
    except Exception:
        summary["success"] = False
        summary["error"] = "An unexpected local or protocol failure prevented the checks."
    finally:
        try:
            session.close()
        except Exception:
            summary["success"] = False
            summary["shutdown_error"] = "The MCP process did not shut down cleanly."
    print(json.dumps(summary, separators=(",", ":")))
    return 0 if summary["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
