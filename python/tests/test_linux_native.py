# VBWR B
# File: Opt-in Linux CLI/MCP integration using a private D-Bus and disposable keyring.
# Modified with AI: OpenAI Codex, 2026-11-02, under Umberto Giacobbi's direction.
# Copyright (c) 2026 Umberto Giacobbi
# SPDX-License-Identifier: MIT
# VBWR E

"""Native Linux checks; opt in with AGENTINBOX_LINUX_NATIVE=1, never use a live keyring."""

import json
import os
import select
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path


@unittest.skipUnless(sys.platform == "linux" and os.environ.get("AGENTINBOX_LINUX_NATIVE") == "1",
                     "Requires explicit opt-in to private Linux D-Bus/keyring integration checks.")
class LinuxNativeTests(unittest.TestCase):
    """Run real source entry points without touching the desktop bus or provider mail."""

    @classmethod
    def setUpClass(cls):
        """Start owned session/keyring processes with all persistent output in artifacts."""
        for command in ("dbus-daemon", "gnome-keyring-daemon", "gdbus"):
            if not shutil.which(command):
                raise RuntimeError(f"Native integration requires {command}.")
        cls.repository = Path(__file__).resolve().parents[2]
        artifacts = cls.repository / "artifacts"
        root = Path(os.environ["AGENTINBOX_TEST_ROOT"])
        if not root.is_absolute() or root.resolve() != root or not root.is_relative_to(artifacts) or root == artifacts:
            raise RuntimeError("Native test root must be an exact directory inside artifacts.")
        root.mkdir(parents=True, exist_ok=True)
        cls.temporary = tempfile.TemporaryDirectory(prefix="linux-native-", dir=root)
        cls.addClassCleanup(cls.temporary.cleanup)
        cls.root = Path(cls.temporary.name)
        cls.environment = dict(os.environ, XDG_DATA_HOME=str(cls.root / "data"),
                               XDG_CONFIG_HOME=str(cls.root / "config"),
                               AGENTINBOX_DATA_DIR=str(cls.root / "profile"),
                               PYTHONNOUSERSITE="1", PYTHONDONTWRITEBYTECODE="1")
        # Unix sockets need a short POSIX path; tmpfs avoids writes on the live USB.
        cls.runtime = tempfile.TemporaryDirectory(prefix="agentinbox-native-", dir="/tmp")
        cls.addClassCleanup(cls.runtime.cleanup)
        cls.environment["XDG_RUNTIME_DIR"] = cls.runtime.name
        for key in ("DBUS_SESSION_BUS_ADDRESS", "GNOME_KEYRING_CONTROL", "DISPLAY", "WAYLAND_DISPLAY"):
            cls.environment.pop(key, None)
        cls.daemon_log = (cls.root / "daemons.log").open("wb")
        cls.addClassCleanup(cls.daemon_log.close)
        cls.bus = subprocess.Popen(["dbus-daemon", "--session", "--nofork", "--print-address=1"],
                                   env=cls.environment, stdout=subprocess.PIPE, stderr=cls.daemon_log)
        cls.addClassCleanup(cls._stop, cls.bus)
        if not select.select([cls.bus.stdout], [], [], 5)[0]:
            raise RuntimeError("Private D-Bus did not start within five seconds.")
        cls.environment["DBUS_SESSION_BUS_ADDRESS"] = cls.bus.stdout.readline(4096).decode().strip()
        cls.keyring = subprocess.Popen(["gnome-keyring-daemon", "--foreground", "--unlock",
                                        "--components=secrets", "--control-directory", cls.runtime.name],
                                       env=cls.environment, stdin=subprocess.PIPE,
                                       stdout=cls.daemon_log, stderr=cls.daemon_log)
        cls.addClassCleanup(cls._stop, cls.keyring)
        cls.keyring.stdin.write(b"synthetic-disposable-keyring-password")
        cls.keyring.stdin.close()
        for attempt in range(50):
            ready = subprocess.run(["gdbus", "call", "--session", "--dest", "org.freedesktop.DBus",
                                    "--object-path", "/org/freedesktop/DBus", "--method",
                                    "org.freedesktop.DBus.NameHasOwner", "org.freedesktop.secrets"],
                                   env=cls.environment, capture_output=True, timeout=3)
            if ready.returncode == 0 and b"true" in ready.stdout:
                break
            if cls.keyring.poll() is not None:
                raise RuntimeError("Private keyring exited during startup.")
            time.sleep(0.1)
        else:
            raise RuntimeError("Private Secret Service did not become available.")

    @staticmethod
    def _stop(process):
        """Terminate and reap one owned child and close its pipe descriptors.

        Args:
            process: Popen child created by this fixture, never a desktop process.
        """
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        for stream in (process.stdout, process.stdin):
            if stream is not None and not stream.closed:
                stream.close()

    def _run(self, arguments, *, content=None, environment=None):
        """Run the actual source launcher with a deadline and private environment.

        Args:
            arguments: Separate CLI arguments, never a shell command.
            content: Optional stdin text, used only for JSON protocol requests.
            environment: Replacement private child environment, or None for the fixture's.
        """
        return subprocess.run([sys.executable, "-B", str(self.repository / "python" / "run.py"), *arguments],
                              env=environment or self.environment, input=content, text=True,
                              capture_output=True, timeout=40, cwd="/tmp")

    def test_native_storage_round_trip_and_failure_without_session_bus(self):
        result = self._run(["doctor", "--check-storage", "--json"])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        report = json.loads(result.stdout)["result"]
        self.assertTrue(report["ready"])
        self.assertEqual("ok", report["checks"][-1]["status"])
        self.assertEqual("", result.stderr)
        isolated = dict(self.environment, DBUS_SESSION_BUS_ADDRESS="unix:path=/nonexistent-agentinbox-test-bus")
        result = self._run(["doctor", "--check-storage", "--json"], environment=isolated)
        self.assertEqual(1, result.returncode)
        self.assertFalse(json.loads(result.stdout)["result"]["ready"])
        self.assertFalse((Path(isolated["AGENTINBOX_DATA_DIR"]) / "protected").exists())
        # A failed probe can queue cleanup; retry only after restoring our private bus.
        self.assertEqual(0, self._run(["profile", "cleanup", "--json"]).returncode)

    def test_terminal_setup_persists_across_processes_and_mcp_uses_same_profile(self):
        import pty
        import termios

        primary, secondary = pty.openpty()
        self.addCleanup(os.close, primary)
        attributes = termios.tcgetattr(secondary)
        attributes[3] |= termios.ECHO
        termios.tcsetattr(secondary, termios.TCSANOW, attributes)
        process = subprocess.Popen([sys.executable, "-B", str(self.repository / "python" / "run.py"),
                                    "accounts", "add", "--json"], env=self.environment,
                                   stdin=secondary, stdout=subprocess.PIPE, stderr=secondary,
                                   start_new_session=True, cwd="/tmp")
        os.close(secondary)
        self.addCleanup(self._stop, process)
        questions = [b"Provider", b"Existing email", b"Connection name", b"Read mail using",
                     b"Selected folders", b"Allow your AI", b"Save this connection", b"Incoming password"]
        answers = [b"custom", b"reader@example.test", b"Native Linux", b"imap",
                   b"imap.example.test", b"tls", b"993", b"reader@example.test",
                   b"INBOX,Archive", b"no", b"yes", b"synthetic-terminal-secret"]
        # Custom server prompts occur between protocol and folder selection.
        questions[4:4] = [b"IMAP server", b"Connection security", b"Port", b"Server username"]
        transcript = bytearray()
        for question, answer in zip(questions, answers, strict=True):
            deadline = time.monotonic() + 10
            pending = bytearray()
            while question not in pending:
                if time.monotonic() > deadline or not select.select([primary], [], [], 1)[0]:
                    if process.poll() is not None or time.monotonic() > deadline:
                        self.fail(f"Terminal did not reach {question!r}: {bytes(transcript)!r}")
                    continue
                chunk = os.read(primary, 4096)
                pending.extend(chunk)
                transcript.extend(chunk)
            if question == b"Incoming password":
                self.assertFalse(termios.tcgetattr(primary)[3] & termios.ECHO,
                                 "The CLI must disable terminal echo before requesting a password.")
            os.write(primary, answer + b"\n")
        output, _ = process.communicate(timeout=40)
        while select.select([primary], [], [], 0)[0]:
            try:
                chunk = os.read(primary, 4096)
            except OSError:
                break
            if not chunk:
                break
            transcript.extend(chunk)
        self.assertEqual(0, process.returncode, output)
        saved = json.loads(output)["result"]
        identity = saved["id"]
        self.assertFalse(saved["send_enabled"])
        self.assertNotIn(b"synthetic-terminal-secret", transcript)
        self.assertNotIn(b"synthetic-terminal-secret", output)
        shown = self._run(["accounts", "show", identity, "--json"])
        self.assertEqual(1, json.loads(shown.stdout)["result"]["revision"])
        draft = {key: value for key, value in json.loads(shown.stdout)["result"].items()
                 if key not in {"revision", "microsoft_connected"}}
        draft.update(id="", label="Updated Linux connection")
        template = self.root / "setup.json"
        template.write_text(json.dumps({"accounts": {"synthetic": draft}}), encoding="utf-8")
        update = ["accounts", "update", identity, "--revision", "1", "--file", str(template),
                  "--entry", "synthetic", "--json"]
        updated = self._run(update)
        self.assertEqual(0, updated.returncode, updated.stdout + updated.stderr)
        self.assertEqual(2, json.loads(updated.stdout)["result"]["revision"])
        stale = self._run(update)
        self.assertEqual(1, stale.returncode)
        self.assertEqual("configuration_changed", json.loads(stale.stdout)["error"]["code"])

        # Read only this fixture's synthetic credential in a fresh Python process.
        # Neither the command line nor captured output contains its value.
        settings = json.loads((Path(self.environment["AGENTINBOX_DATA_DIR"]) / "accounts.json").read_text())
        reference = settings["accounts"][0]["incoming_secret"]
        program = ("import json,sys; from pathlib import Path; "
                   "sys.path.insert(0, sys.argv[1]); from agentinbox.vault import Vault; "
                   "value=json.load(sys.stdin); "
                   "assert Vault(Path(value['root'])).get(value['reference']) == value['expected']")
        persisted = subprocess.run([sys.executable, "-B", "-c", program, str(self.repository / "python")],
                                   env=self.environment, text=True, capture_output=True, timeout=40,
                                   input=json.dumps({"root": self.environment["AGENTINBOX_DATA_DIR"],
                                                     "reference": reference, "expected": "synthetic-terminal-secret"}))
        self.assertEqual(0, persisted.returncode, persisted.stderr)
        self.assertEqual("", persisted.stdout)

        configuration = json.loads(self._run(["mcp-config"]).stdout)["mcpServers"]["agentinbox"]
        requests = [
            {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "synthetic-linux-check", "version": "1"}}},
            {"jsonrpc": "2.0", "method": "notifications/initialized"},
            {"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
            {"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "list_accounts", "arguments": {}}},
        ]
        result = subprocess.run([configuration["command"], *configuration["args"]],
                                env=dict(self.environment, **configuration["env"]),
                                input="".join(json.dumps(item) + "\n" for item in requests),
                                text=True, capture_output=True, cwd="/tmp", timeout=10)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("", result.stderr)
        replies = [json.loads(line) for line in result.stdout.splitlines()]
        self.assertEqual([1, 2, 3], [item["id"] for item in replies])
        self.assertEqual(5, len(replies[1]["result"]["tools"]))
        accounts = json.loads(replies[2]["result"]["content"][0]["text"])["accounts"]
        self.assertEqual(identity, accounts[0]["id"])
        self.assertEqual(["INBOX", "Archive"], accounts[0]["folders"])
        self.assertNotIn("synthetic-terminal-secret", result.stdout)
        self.assertEqual(0, self._run(["accounts", "remove", identity, "--json"]).returncode)
        self.assertEqual([], json.loads(self._run(["accounts", "list", "--json"]).stdout)["result"])

    def test_piped_guided_input_is_rejected_and_json_stays_parseable(self):
        result = self._run(["accounts", "add", "--json"], content="gmail\n")
        self.assertEqual(1, result.returncode)
        self.assertEqual("terminal_required", json.loads(result.stdout)["error"]["code"])
        self.assertEqual("", result.stderr)
