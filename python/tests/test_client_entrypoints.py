"""Synthetic local-session, strict-input and profile-location regression cases."""

import contextlib
import copy
import io
import json
import os
from types import SimpleNamespace
from unittest import mock

from agentinbox import cli, config, mcp
from agentinbox.errors import MailError
from tests._support import MailApplicationCase


class ClientEntrypointTests(MailApplicationCase):
    """Exercise client-visible failures without native APIs or live mail."""

    def test_optional_outgoing_arrays_reject_falsey_values_without_sending(self):
        for field, value in (("cc", False), ("bcc", 0), ("attachments", {})):
            with self.subTest(field=field), self.assertRaises(MailError):
                self.send(**{field: value})
        self.assertFalse(self.app.sends.path.exists())
        self.smtp.assert_not_called()

    def test_mcp_schema_rejects_bool_integer_and_optional_array_substitutes(self):
        definitions = {item["name"]: item["inputSchema"] for item in mcp.TOOLS}
        cases = [
            ("search_mail", {"account_id": self.account["id"], "limit": True}),
            ("search_mail", {"account_id": self.account["id"], "unread": "false"}),
            ("send_email", {"request_id": "a" * 32, "account_id": self.account["id"], "to": ["reader@example.test"],
                               "subject": "Synthetic", "body": "Synthetic", "cc": False}),
        ]
        for operation, arguments in cases:
            with self.subTest(operation=operation, arguments=arguments), self.assertRaises(MailError) as failure:
                mcp.validate_arguments(arguments, definitions[operation])
            self.assertEqual("invalid_arguments", failure.exception.code)

    def test_manage_session_retains_message_reference_between_search_and_read(self):
        responses = []
        start = "2026-10-01T00:00:00+00:00"
        stop = "2026-10-19T00:00:00+00:00"
        requests = [
            {"operation": "search_mail", "arguments": {"account_id": self.account["id"], "start": start, "end": stop}},
            None,
        ]

        class Input:
            def readline(self, maximum):
                if not requests:
                    return b""
                request = requests.pop(0)
                if request is None:
                    reference = responses[0]["result"]["messages"][0]["reference"]
                    request = {"operation": "read_mail", "arguments": {"reference": reference}}
                return (json.dumps(request) + "\n").encode()

        self.imap.side_effect = lambda *args, **kwargs: contextlib.nullcontext(object())
        page = {"messages": [{"uid": "12", "uid_validity": "4", "folder": "INBOX", "subject": "Synthetic"}],
                "more": False, "next_offset": 1, "candidate_limit_reached": False,
                "uid_validity": "4", "candidates": ["12"]}
        with mock.patch("agentinbox.mail.search", return_value=copy.deepcopy(page)), \
             mock.patch("agentinbox.mail.read", return_value={"uid": "12", "body": "Synthetic message"}) as read, \
             mock.patch.object(cli.sys, "stdin", SimpleNamespace(buffer=Input())), \
             mock.patch.object(cli, "write", side_effect=responses.append):
            result = cli.manage(self.app, True)
        self.assertEqual(0, result)
        self.assertEqual(2, len(responses))
        self.assertTrue(all(response["ok"] for response in responses))
        self.assertEqual("Synthetic message", responses[1]["result"]["body"])
        self.assertEqual(("INBOX", "12", "4"), read.call_args.args[1:])

    def test_management_error_is_reported_and_later_session_request_still_runs(self):
        requests = [
            {"operation": "old_provider_login", "arguments": {}},
            {"operation": "profile_info", "arguments": {}},
        ]
        stream = io.BytesIO(b"".join((json.dumps(request) + "\n").encode() for request in requests))
        responses = []
        with mock.patch.object(cli.sys, "stdin", SimpleNamespace(buffer=stream)), \
             mock.patch.object(cli, "write", side_effect=responses.append):
            result = cli.manage(self.app, True)
        self.assertEqual(1, result)
        self.assertFalse(responses[0]["ok"])
        self.assertTrue(responses[1]["ok"])
        self.assertEqual(str(self.root), responses[1]["result"]["data_directory"])
        self.assertNotIn("password", json.dumps(responses[1]["result"]))

    def test_packaged_default_and_explicit_profile_are_distinct(self):
        family = "Synthetic.AgentInbox_123456789abcd"
        with mock.patch.object(config.sys, "platform", "win32"), \
             mock.patch.object(config, "windows_package_family", return_value=family), \
             mock.patch.dict(os.environ, {"LOCALAPPDATA": str(self.root)}, clear=True):
            expected = self.root / "Packages" / family / "LocalState" / "AgentInbox"
            self.assertEqual(expected, config.data_directory())
            with mock.patch.dict(os.environ, {"AGENTINBOX_DATA_DIR": str(self.root / "explicit")}):
                self.assertEqual(self.root / "explicit", config.data_directory())

    def test_unpacked_windows_profile_uses_local_appdata(self):
        with mock.patch.object(config.sys, "platform", "win32"), \
             mock.patch.object(config, "windows_package_family", return_value=None), \
             mock.patch.dict(os.environ, {"LOCALAPPDATA": str(self.root)}, clear=True):
            self.assertEqual(self.root / "AgentInbox", config.data_directory())
