"""Source profile and plugin regression cases; no native credentials or providers."""

import contextlib
import importlib.util
import io
import json
import os
import unittest
from pathlib import Path
from unittest import mock

from agentinbox import config
from agentinbox.errors import MailError
from tests._support import MailApplicationCase


class PlatformProfileTests(MailApplicationCase):
    """Keep profile selection explicit and independent of the host environment."""

    def test_linux_empty_and_unset_xdg_use_the_user_data_directory(self):
        for environment in ({}, {"XDG_DATA_HOME": ""}):
            with self.subTest(environment=environment), \
                 mock.patch.dict(os.environ, environment, clear=True), \
                 mock.patch.object(config.sys, "platform", "linux"), \
                 mock.patch.object(config.Path, "home", return_value=self.root):
                self.assertEqual((self.root / ".local" / "share" / "agentinbox").resolve(), config.data_directory())

    def test_linux_explicit_profile_takes_precedence_over_xdg(self):
        profile = self.root / "chosen-profile"
        with mock.patch.dict(os.environ, {"AGENTINBOX_DATA_DIR": str(profile), "XDG_DATA_HOME": "relative"}, clear=True), \
             mock.patch.object(config.sys, "platform", "linux"):
            self.assertEqual(profile.resolve(), config.data_directory())

    def test_linux_relative_xdg_is_rejected(self):
        with mock.patch.dict(os.environ, {"XDG_DATA_HOME": "relative"}, clear=True), \
             mock.patch.object(config.sys, "platform", "linux"):
            with self.assertRaises(MailError) as raised:
                config.data_directory()
        self.assertEqual("invalid_directory", raised.exception.code)

    def test_unsupported_platform_rejects_even_an_explicit_profile(self):
        with mock.patch.dict(os.environ, {"AGENTINBOX_DATA_DIR": str(self.root)}, clear=True), \
             mock.patch.object(config.sys, "platform", "freebsd14"):
            with self.assertRaises(MailError) as raised:
                config.data_directory()
        self.assertEqual("unsupported_platform", raised.exception.code)


class SourcePluginPathTests(MailApplicationCase):
    """Export synthetic source without executing the selected interpreter."""

    def setUp(self):
        """Create a disposable source tree and load the exporter without running it."""
        super().setUp()
        script = Path(__file__).resolve().parents[2] / "scripts" / "export-plugin.py"
        spec = importlib.util.spec_from_file_location("agentinbox_export_under_test", script)
        self.exporter = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.exporter)
        self.repository = self.root / "source"
        templates = self.repository / "integrations" / "codex"
        plugin = templates / "plugins" / "agentinbox"
        plugin.mkdir(parents=True)
        (plugin / "plugin.json").write_text("{}", encoding="utf-8")
        (plugin / "mcp.json").write_text(json.dumps({"mcpServers": {"agentinbox": {}}}), encoding="utf-8")
        catalog = templates / ".agents" / "plugins"
        catalog.mkdir(parents=True)
        (catalog / "marketplace.json").write_text("{}", encoding="utf-8")
        source = self.repository / "python"
        (source / "agentinbox").mkdir(parents=True)
        (source / "run.py").write_text("# Synthetic source, never executed.\n", encoding="utf-8")
        (self.repository / "LICENSE").write_text("Synthetic license fixture.\n", encoding="utf-8")
        self.interpreter = self.root / "synthetic-python"
        self.interpreter.write_text("Never execute this fixture.\n", encoding="utf-8")
        self.interpreter.chmod(0o700)

    def _export(self, interpreter, profile):
        arguments = ["export-plugin.py", "--python", str(interpreter), "--data-dir", str(profile)]
        with mock.patch.object(self.exporter, "__file__", str(self.repository / "scripts" / "export-plugin.py")), \
             mock.patch.object(self.exporter.sys, "argv", arguments), \
             contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.exporter.main()

    @unittest.skipUnless(os.name == "posix", "Requires native POSIX symlinks and execute permissions.")
    def test_export_preserves_the_virtual_environment_interpreter_path(self):
        interpreter = self.root / "venv" / "bin" / "python"
        interpreter.parent.mkdir(parents=True)
        interpreter.symlink_to(self.interpreter)
        self._export(interpreter, self.root / "profile")
        manifest = self.repository / "artifacts" / "codex-source" / "plugins" / "agentinbox" / "mcp.json"
        command = json.loads(manifest.read_text(encoding="utf-8"))["mcpServers"]["agentinbox"]["command"]
        self.assertEqual(str(interpreter), command)
        self.assertNotEqual(str(interpreter.resolve()), command)

    @unittest.skipUnless(os.name == "posix", "Requires native POSIX execute permissions.")
    def test_nonexecutable_interpreter_is_rejected_before_export(self):
        self.interpreter.chmod(0o600)
        with self.assertRaises(SystemExit) as raised:
            self._export(self.interpreter, self.root / "profile")
        self.assertEqual(2, raised.exception.code)
        self.assertFalse((self.repository / "artifacts").exists())

    def test_profile_file_is_rejected_before_export(self):
        profile = self.root / "profile-file"
        profile.write_text("Synthetic profile fixture.\n", encoding="utf-8")
        with self.assertRaises(SystemExit) as raised:
            self._export(self.interpreter, profile)
        self.assertEqual(2, raised.exception.code)
        self.assertFalse((self.repository / "artifacts").exists())
