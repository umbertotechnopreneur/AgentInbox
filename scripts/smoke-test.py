#!/usr/bin/env python3
"""Exercise a built DLL or native executable through the real CLI and MCP stdio boundary."""

import argparse
import json
import os
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time


def check(condition, message):
    if not condition:
        raise RuntimeError(message)


def check_only_diagnostic_state(registry):
    """Allow documented rolling logs while rejecting any other first-run state."""
    registry_path = Path(registry)
    if not registry_path.exists():
        return

    log_directory = registry_path / "logs"
    unexpected = []
    for path in registry_path.rglob("*"):
        if path == log_directory:
            continue
        if (path.is_file() and path.parent == log_directory and path.name.startswith("mailmeup-")
                and path.suffix == ".log"):
            continue
        unexpected.append(str(path.relative_to(registry_path)))

    check(not unexpected, f"Read-only first run created non-diagnostic local state: {', '.join(unexpected)}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("executable", type=Path)
    args = parser.parse_args()
    # Keep an MSIX execution alias intact: resolving its reparse point would bypass the launch route being tested.
    executable = Path(os.path.abspath(args.executable))
    command = ["dotnet", str(executable)] if executable.suffix == ".dll" else [str(executable)]

    with tempfile.TemporaryDirectory(prefix="mailmeup-smoke-") as directory:
        registry = str(Path(directory) / "registry")
        environment = {**os.environ, "MAILMEUP_DATA_DIR": registry, "MAILMEUP_LOG_LEVEL": "warning",
                       "DOTNET_CLI_UI_LANGUAGE": "en"}
        for cli_args in (["--help"], ["--version"], ["status"], ["accounts", "list"], ["setup", "status"]):
            result = subprocess.run(command + cli_args, env=environment, capture_output=True, text=True,
                                    encoding="utf-8", timeout=30, check=True)
            check(bool(result.stdout.strip()), f"Empty output for {cli_args}")
            check(not result.stderr, f"Unexpected default diagnostics for {cli_args}")
            check("\x1b" not in result.stdout, f"ANSI escapes in redirected output for {cli_args}")
            if cli_args == ["status"]:
                status = json.loads(result.stdout)
                check(status["stage"] == "read_only_mvp", "Incorrect application stage")
                check(status["can_connect_accounts"] is True, "Account connection was not advertised")
                check(status["read_only"] is True, "Read-only scope was not reported")
                check(all(provider["authentication_available"] for provider in status["providers"]), "Missing authentication capability")
                check(all(provider["mail_read_available"] for provider in status["providers"]), "Missing mail capability")
                check(all(provider["calendar_read_available"] for provider in status["providers"]), "Missing calendar capability")
            if cli_args == ["accounts", "list"]:
                check(json.loads(result.stdout) == {"accounts": []}, "Unexpected account data")
            if cli_args == ["setup", "status"]:
                providers = json.loads(result.stdout)["providers"]
                check({provider["provider_id"] for provider in providers} == {"google", "microsoft"}, "Unexpected setup providers")
                check(all(not provider["configured"] for provider in providers), "Fresh setup should be empty")
        invalid = subprocess.run(command + ["unknown"], env=environment, capture_output=True, text=True,
                                 encoding="utf-8", timeout=30)
        check(invalid.returncode == 2 and not invalid.stdout, "Unknown commands must fail on stderr")

        for cli_args in (["--stdio", "--json"], ["status", "--log-level"], ["status", "--log-level", "0"],
                         ["accounts", "connect", "google", "--mail-only", "--calendar-only"],
                         ["setup", "status", "--calendar-only"], ["status", "--json", "--json"],
                         ["accounts", "remove", "--unknown"]):
            rejected = subprocess.run(command + cli_args, env=environment, capture_output=True, text=True,
                                      encoding="utf-8", timeout=30)
            check(rejected.returncode == 2 and not rejected.stdout and rejected.stderr,
                  f"Invalid options must fail before executing: {cli_args}")

        explicit_json = subprocess.run(command + ["--no-color", "accounts", "list", "--json", "--log-level", "debug"],
                                       env=environment, capture_output=True, text=True, encoding="utf-8",
                                       timeout=30, check=True)
        check(json.loads(explicit_json.stdout) == {"accounts": []}, "Explicit JSON shape changed")
        check("list_accounts" in explicit_json.stderr and "DBG" in explicit_json.stderr,
              "Serilog did not emit application diagnostics to stderr")
        check("\x1b" not in explicit_json.stdout + explicit_json.stderr, "Plain output contains ANSI escapes")

        level_override = subprocess.run(command + ["status", "--json", "--log-level", "warning"],
                                        env={**environment, "MAILMEUP_LOG_LEVEL": "invalid"},
                                        capture_output=True, text=True, encoding="utf-8", timeout=30, check=True)
        check(json.loads(level_override.stdout)["read_only"] and not level_override.stderr,
              "Command-line log level did not override the environment")
        private_path = str(Path(directory) / "private-input-sentinel@example.test.json")
        failed_setup = subprocess.run(command + ["setup", "google", private_path, "--log-level", "verbose"],
                                      env=environment, capture_output=True, text=True, encoding="utf-8", timeout=30)
        check(failed_setup.returncode == 1 and not failed_setup.stdout and failed_setup.stderr,
              "Missing setup input did not fail cleanly")
        check("private-input-sentinel" not in failed_setup.stderr and private_path not in failed_setup.stderr,
              "Diagnostics disclosed private command arguments or exception messages")

        process = subprocess.Popen(command + ["--stdio", "--log-level", "verbose"], env=environment, stdin=subprocess.PIPE,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")
        messages = queue.Queue()
        errors = []

        def consume_stdout():
            for line in process.stdout:
                try:
                    messages.put(json.loads(line))
                except json.JSONDecodeError:
                    messages.put(RuntimeError("Non-JSON output on MCP stdout"))
            messages.put(RuntimeError("MCP stdout closed unexpectedly"))

        def consume_stderr():
            for line in process.stderr:
                errors.append(line)

        threading.Thread(target=consume_stdout, daemon=True).start()
        stderr_thread = threading.Thread(target=consume_stderr, daemon=True)
        stderr_thread.start()

        def send(message):
            process.stdin.write(json.dumps({"jsonrpc": "2.0", **message}) + "\n")
            process.stdin.flush()

        def receive(identifier):
            deadline = time.monotonic() + 30
            while True:
                item = messages.get(timeout=max(0.1, deadline - time.monotonic()))
                if isinstance(item, Exception):
                    raise item
                if item.get("id") == identifier:
                    return item
                check(time.monotonic() < deadline, "MCP response timeout")

        def content(response):
            check("error" not in response, "MCP returned a protocol error")
            result = response["result"]
            check(not result.get("isError", False), "MCP tool returned an error")
            return result.get("structuredContent") or json.loads(result["content"][0]["text"])

        try:
            send({"id": 1, "method": "initialize", "params": {"protocolVersion": "2025-11-25", "capabilities": {},
                  "clientInfo": {"name": "mailmeup-smoke", "version": "1.0.0"}}})
            check("result" in receive(1), "MCP initialization failed")
            send({"method": "notifications/initialized"})
            send({"id": 2, "method": "tools/list"})
            tools = receive(2)["result"]["tools"]
            expected_tools = {"get_status", "list_accounts", "search_mail", "search_unread_mail",
                              "search_mail_by_date", "read_mail", "list_calendars", "search_events", "read_event"}
            check({tool["name"] for tool in tools} == expected_tools, "Unexpected tool surface")
            check(all(tool["annotations"]["readOnlyHint"] for tool in tools), "Missing read-only hints")
            for tool in tools:
                if tool["name"] in {"search_mail", "search_unread_mail", "search_mail_by_date"}:
                    schema = tool["inputSchema"]
                    check("inboxOnly" in schema["properties"], f"Missing Inbox filter in {tool['name']}")
                    check("inboxOnly" not in schema.get("required", []), "Inbox scope must have a default")
            send({"id": 3, "method": "tools/call", "params": {"name": "get_status", "arguments": {}}})
            check(content(receive(3))["can_connect_accounts"] is True, "Incorrect MCP readiness")
            send({"id": 4, "method": "tools/call", "params": {"name": "list_accounts", "arguments": {}}})
            check(content(receive(4)) == {"accounts": []}, "Unexpected MCP account data")
            send({"id": 5, "method": "tools/call", "params": {"name": "search_mail", "arguments": {"query": "private-query-sentinel@example.test"}}})
            mail = content(receive(5))
            check(mail["items"] == [] and mail["coverage_complete"] is True, "Unexpected empty mail search")
            check(mail["inbox_only"] is False, "General mail search must retain its broad default")
            send({"id": 6, "method": "tools/call", "params": {"name": "search_unread_mail", "arguments": {}}})
            unread = content(receive(6))
            check(unread["items"] == [] and unread["coverage_complete"] is True, "Unexpected empty unread mail search")
            check(unread["inbox_only"] is True, "Unread mail search must default to Inbox")
            send({"id": 7, "method": "tools/call", "params": {"name": "search_mail_by_date", "arguments": {
                "start": "2026-09-05T00:00:00+07:00", "end": "2026-09-06T00:00:00+07:00"}}})
            dated = content(receive(7))
            check(dated["items"] == [] and dated["coverage_complete"] is True, "Unexpected empty date mail search")
            check(dated["inbox_only"] is False, "Date mail search must retain its broad default")
            for identifier, name, arguments, expected_scope in (
                    (12, "search_unread_mail", {"inboxOnly": False}, False),
                    (13, "search_mail_by_date", {"start": "2026-09-05T00:00:00+07:00",
                                                  "end": "2026-09-06T00:00:00+07:00", "inboxOnly": True}, True),
                    (14, "search_mail", {"query": "synthetic", "inboxOnly": True}, True)):
                send({"id": identifier, "method": "tools/call", "params": {"name": name, "arguments": arguments}})
                scoped = content(receive(identifier))
                check(scoped["inbox_only"] is expected_scope, f"Explicit Inbox scope not applied by {name}")
                check(scoped["items"] == [] and scoped["coverage_complete"] is True,
                      f"Unexpected empty scoped mail result for {name}")
            send({"id": 8, "method": "tools/call", "params": {"name": "list_calendars", "arguments": {}}})
            calendars = content(receive(8))
            check(calendars["calendars"] == [] and calendars["coverage_complete"] is True, "Unexpected empty calendar list")
            send({"id": 9, "method": "tools/call", "params": {"name": "search_events", "arguments": {
                "start": "2026-09-05T00:00:00+07:00", "end": "2026-09-06T00:00:00+07:00"}}})
            events = content(receive(9))
            check(events["events"] == [] and events["coverage_complete"] is True, "Unexpected empty event search")
            for identifier, name, arguments in ((10, "read_mail", {"reference": "m_private-reference-sentinel"}),
                                                (11, "read_event", {"reference": "evt_invalid"})):
                send({"id": identifier, "method": "tools/call", "params": {"name": name, "arguments": arguments}})
                rejected = receive(identifier)
                check("error" in rejected or rejected.get("result", {}).get("isError", False),
                      f"Invalid reference was accepted by {name}")
                failure = rejected.get("result", {})
                failure_payload = failure.get("structuredContent") or json.loads(failure["content"][0]["text"])
                notification = failure_payload["user_notification"]
                check(notification["required"] is True and "tell the user" in notification["instruction"].lower(),
                      f"Missing caller notification for {name}")
                check("MailMeUp plugin" in notification["message"], f"Missing plain-English plugin failure for {name}")
        finally:
            process.stdin.close()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
            stderr_thread.join(timeout=5)
        check_only_diagnostic_state(registry)
        diagnostics = "".join(errors)
        check("Starting read-only MCP bridge" in diagnostics and "search_mail" in diagnostics,
              "MCP application diagnostics were not written to stderr")
        check("private-query-sentinel" not in diagnostics and "m_private-reference-sentinel" not in diagnostics,
              "Verbose MCP diagnostics disclosed request arguments")
        check("\x1b" not in diagnostics, "MCP diagnostics contain ANSI escapes")
    print("PASS: CLI options, JSON, private stderr logs, nine MCP tools, Inbox scope schema/defaults/overrides, empty reads, invalid references, and diagnostic-only first run.")


if __name__ == "__main__":
    main()
