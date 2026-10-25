"""Synthetic Microsoft authorization scenarios; no real tokens or HTTP requests."""

import contextlib
import copy
import os
import tempfile
import unittest
import urllib.parse
from pathlib import Path
from unittest import mock

from agentinbox.errors import MailError
from agentinbox.microsoft import MicrosoftAuth, RESOURCE
from agentinbox.storage import Store

from tests._support import MemoryVault


class SyntheticStore:
    """Model account revision commits while keeping every credential in memory."""

    def __init__(self, root):
        self.root = root
        self.vault = MemoryVault()
        self.in_transaction = False
        self.saved = {
            "id": "a" * 32,
            "revision": 1,
            "label": "Synthetic Microsoft account",
            "email": "reader@example.test",
            "incoming": {
                "protocol": "imap", "host": "outlook.office365.com",
                "port": 993, "tls": "tls", "username": "reader@example.test",
            },
            "smtp": None,
            "read_enabled": True,
            "send_enabled": False,
            "folders": ["INBOX"],
            "authentication": {
                "method": "microsoft_oauth",
                "client_id": "11111111-2222-3333-4444-555555555555",
                "tenant": "common",
            },
            "incoming_secret": None,
            "smtp_secret": None,
            "oauth_secret": None,
        }

    @contextlib.contextmanager
    def locked(self):
        if self.in_transaction:
            raise AssertionError("Nested settings transaction.")
        self.in_transaction = True
        try:
            yield
        finally:
            self.in_transaction = False

    def account(self, identity):
        if identity != self.saved["id"]:
            raise MailError("account_not_found", "Synthetic account was removed.")
        return copy.deepcopy(self.saved)

    def cleanup_queued(self, reference):
        return False

    def retire_protected(self, references):
        with self.locked():
            for reference in references:
                self.vault.delete(reference)
        return {"cleanup_pending": False, "cleanup_pending_count": 0, "cleanup_error": None}

    def connect_oauth(self, account_id, expected_revision, refresh_token, *, consumed_references=()):
        with self.locked():
            current = self.account(account_id)
            if current["revision"] != expected_revision:
                raise MailError("configuration_changed", "Synthetic settings changed.")
            reference = "oauth-" + "c" * 32
            self.vault.put(reference, refresh_token)
            self.saved["oauth_secret"] = reference
            self.saved["revision"] += 1
            for consumed in consumed_references:
                self.vault.delete(consumed)
            return {**{key: value for key, value in self.saved.items() if not key.endswith("_secret")},
                    "cleanup_pending": False, "cleanup_pending_count": 0, "cleanup_error": None}


class MicrosoftAuthorizationTests(unittest.TestCase):
    """Check scopes, protected state, pacing, redaction and concurrent changes."""

    def setUp(self):
        repository = Path(__file__).resolve().parents[2]
        artifacts = repository / "artifacts"
        container = Path(os.environ.get("AGENTINBOX_TEST_ROOT", str(artifacts / "tests")))
        if (
            not container.is_absolute()
            or artifacts.resolve() != artifacts
            or container.resolve() != container
            or container == artifacts
            or not container.is_relative_to(artifacts)
        ):
            raise RuntimeError("Synthetic authorization output must stay under artifacts.")
        created = not container.exists()
        container.mkdir(parents=True, exist_ok=True)
        temporary = tempfile.TemporaryDirectory(prefix="microsoft-", dir=container)
        root = Path(temporary.name).resolve()

        def cleanup():
            if Path(temporary.name).resolve() != root or root.parent != container:
                raise RuntimeError("Refusing cleanup outside the synthetic authorization profile.")
            temporary.cleanup()
            if created and container.exists() and not any(container.iterdir()):
                container.rmdir()

        self.addCleanup(cleanup)
        self.store = SyntheticStore(root)
        self.auth = MicrosoftAuth(self.store)
        network = mock.patch("socket.create_connection", side_effect=AssertionError("Live HTTP is forbidden."))
        network.start()
        self.addCleanup(network.stop)
        clock = mock.patch("agentinbox.microsoft.time.time", return_value=1000)
        self.clock = clock.start()
        self.addCleanup(clock.stop)

    @staticmethod
    def device_response(**changes):
        response = {
            "device_code": "synthetic-device-code",
            "user_code": "ABCD-EFGH",
            "verification_uri": "https://microsoft.com/devicelogin",
            "expires_in": 900,
            "interval": 5,
        }
        response.update(changes)
        return response

    @staticmethod
    def token_response(**changes):
        response = {
            "token_type": "Bearer",
            "scope": RESOURCE + "IMAP.AccessAsUser.All",
            "access_token": "synthetic-access-token",
            "refresh_token": "synthetic-refresh-token",
            "expires_in": 3600,
        }
        response.update(changes)
        return response

    def test_start_requests_only_selected_pop_permission_and_offline_access(self):
        self.store.saved["incoming"]["protocol"] = "pop3"
        self.store.saved["incoming"]["port"] = 995

        def post(account, operation, fields, *, diagnostic=None):
            self.assertFalse(self.store.in_transaction)
            self.assertEqual("devicecode", operation)
            self.assertEqual(
                {RESOURCE + "POP.AccessAsUser.All", "offline_access"}, set(fields["scope"].split())
            )
            self.assertNotIn("client_secret", fields)
            return self.device_response()

        with mock.patch.object(self.auth, "_post", side_effect=post):
            started = self.auth.start(self.store.saved["id"])

        self.assertNotIn("device_code", started)
        self.assertEqual("ABCD-EFGH", started["user_code"])
        self.assertEqual(900, started["expires_in"])
        self.assertIn("signin-" + started["flow_id"], self.store.vault.values)
        self.assertIsNone(self.store.saved["oauth_secret"])

    def test_poll_honors_persisted_interval_and_slow_down(self):
        with mock.patch.object(self.auth, "_post", return_value=self.device_response()) as post:
            started = self.auth.start(self.store.saved["id"])
            self.clock.return_value = 1002
            self.assertEqual({"status": "pending", "interval": 3}, self.auth.poll(started["flow_id"]))
            self.assertEqual(1, post.call_count)
            post.return_value = {"error": "slow_down", "error_description": "Synthetic server detail."}
            self.clock.return_value = 1005
            self.assertEqual({"status": "pending", "interval": 10}, self.auth.poll(started["flow_id"]))
            self.clock.return_value = 1006
            self.assertEqual({"status": "pending", "interval": 9}, self.auth.poll(started["flow_id"]))
            self.assertEqual(2, post.call_count)

    def test_connected_flow_persists_only_refresh_and_reuses_access_in_memory(self):
        def post(account, operation, fields, *, diagnostic=None):
            self.assertFalse(self.store.in_transaction)
            return self.device_response() if operation == "devicecode" else self.token_response()

        with mock.patch.object(self.auth, "_post", side_effect=post) as request:
            started = self.auth.start(self.store.saved["id"])
            self.clock.return_value = 1005
            connected = self.auth.poll(started["flow_id"])
            access = self.auth.access_token(self.store.account(self.store.saved["id"]))

        self.assertEqual("connected", connected["status"])
        self.assertEqual(2, connected["account"]["revision"])
        self.assertEqual("synthetic-access-token", access)
        self.assertEqual(2, request.call_count)
        self.assertNotIn("signin-" + started["flow_id"], self.store.vault.values)
        self.assertEqual("synthetic-refresh-token", self.store.vault.get(self.store.saved["oauth_secret"]))
        self.assertFalse(any("synthetic-access-token" in value for value in self.store.vault.values.values()))

    def test_refresh_rotation_cannot_overwrite_a_newer_account_connection(self):
        original = "oauth-" + "b" * 32
        replacement = "oauth-" + "d" * 32
        self.store.saved["oauth_secret"] = original
        self.store.vault.put(original, "synthetic-original-refresh")

        def post(account, operation, fields, *, diagnostic=None):
            self.assertFalse(self.store.in_transaction)
            self.store.saved["revision"] += 1
            self.store.saved["oauth_secret"] = replacement
            self.store.vault.put(replacement, "synthetic-new-connection")
            return self.token_response()

        with mock.patch.object(self.auth, "_post", side_effect=post):
            with self.assertRaises(MailError) as failure:
                self.auth.access_token(self.store.account(self.store.saved["id"]))

        self.assertEqual("configuration_changed", failure.exception.code)
        self.assertEqual("synthetic-new-connection", self.store.vault.get(replacement))
        self.assertEqual("synthetic-original-refresh", self.store.vault.get(original))

    def test_valid_refresh_rotates_same_secret_without_changing_account_revision(self):
        reference = "oauth-" + "b" * 32
        self.store.saved["oauth_secret"] = reference
        self.store.vault.put(reference, "synthetic-original-refresh")
        with mock.patch.object(self.auth, "_post", return_value=self.token_response()) as post:
            token = self.auth.access_token(self.store.account(self.store.saved["id"]))

        self.assertEqual("synthetic-access-token", token)
        self.assertEqual("synthetic-refresh-token", self.store.vault.get(reference))
        self.assertEqual(1, self.store.saved["revision"])
        self.assertEqual("refresh_token", post.call_args.args[2]["grant_type"])
        self.assertFalse(any(token in value for value in self.store.vault.values.values()))

    def test_invalid_secure_browser_addresses_are_rejected_before_saving_flow(self):
        for uri in (
            "http://microsoft.com/devicelogin",
            "https:///missing-host",
            "https://microsoft.com@attacker.example.test/devicelogin",
            "https://@microsoft.com/devicelogin",
            "https://:@microsoft.com/devicelogin",
            "https://microsoft.com:invalid/devicelogin",
            "https://microsoft.com:0/devicelogin",
        ):
            with self.subTest(uri=uri):
                with mock.patch.object(self.auth, "_post", return_value=self.device_response(verification_uri=uri)):
                    with self.assertRaises(MailError) as failure:
                        self.auth.start(self.store.saved["id"])
                self.assertEqual("microsoft_response", failure.exception.code)
        self.assertEqual({}, self.store.vault.values)

    def test_returned_https_address_is_preserved_without_codes_in_diagnostics(self):
        uri = "https://signin.example.test/ABCD-EFGH?code=synthetic-device-code#ABCD-EFGH"
        details = []
        with mock.patch.object(self.auth, "_post", return_value=self.device_response(verification_uri=uri)):
            started = self.auth.start(self.store.saved["id"], diagnostic=details.append)
        self.assertEqual(uri, started["verification_uri"])
        trace = "\n".join(details)
        self.assertIn("https://signin.example.test", trace)
        self.assertNotIn("ABCD-EFGH", trace)
        self.assertNotIn("synthetic-device-code", trace)
        self.assertNotIn(self.store.saved["email"], trace)

    def test_declined_flow_discards_device_code_and_redacts_server_description(self):
        with mock.patch.object(self.auth, "_post", return_value=self.device_response()) as post:
            started = self.auth.start(self.store.saved["id"])
            self.clock.return_value = 1005
            post.return_value = {
                "error": "authorization_declined",
                "error_description": "Sign-in declined for reader@example.test with synthetic-device-code",
            }
            with self.assertRaises(MailError) as failure:
                self.auth.poll(started["flow_id"])

        self.assertEqual("authorization_declined", failure.exception.code)
        detail = failure.exception.server_response
        self.assertIn("Sign-in declined", detail)
        self.assertNotIn("synthetic-device-code", detail)
        self.assertNotIn("reader@example.test", detail)
        self.assertNotIn("server_response", failure.exception.result())
        self.assertNotIn("signin-" + started["flow_id"], self.store.vault.values)
        self.assertIsNone(self.store.saved["oauth_secret"])

    def test_prior_grants_do_not_reject_requested_mail_permission(self):
        details = []
        with mock.patch.object(self.auth, "_post", return_value=self.device_response()) as post:
            started = self.auth.start(self.store.saved["id"])
            self.clock.return_value = 1005
            post.return_value = self.token_response(scope=RESOURCE + "IMAP.AccessAsUser.All " + RESOURCE + "User.Read")
            result = self.auth.poll(started["flow_id"], diagnostic=details.append)
        self.assertEqual("connected", result["status"])
        self.assertIsNotNone(self.store.saved["oauth_secret"])
        self.assertIn("User.Read", "\n".join(details))
        self.assertFalse(self.store.saved["send_enabled"])

    def test_missing_mail_permission_reports_requested_and_received_without_secrets(self):
        details = []
        with mock.patch.object(self.auth, "_post", return_value=self.device_response()) as post:
            started = self.auth.start(self.store.saved["id"])
            self.clock.return_value = 1005
            post.return_value = self.token_response(scope=RESOURCE + "User.Read")
            with self.assertRaises(MailError) as failure:
                self.auth.poll(started["flow_id"], diagnostic=details.append)
        self.assertIn("did not allow reading mail", str(failure.exception))
        detail = failure.exception.server_response
        self.assertIn("IMAP.AccessAsUser.All", detail)
        self.assertIn("User.Read", detail)
        self.assertNotIn("server_response", failure.exception.result())
        trace = "\n".join(details) + detail
        for secret in ("synthetic-device-code", "synthetic-access-token", "synthetic-refresh-token", self.store.saved["email"]):
            self.assertNotIn(secret, trace)
        self.assertIsNone(self.store.saved["oauth_secret"])
        self.assertNotIn("signin-" + started["flow_id"], self.store.vault.values)

    def test_start_error_keeps_bounded_server_reason_only_for_local_setup(self):
        response = {
            "error": "invalid_client",
            "error_description": "SYNTHETIC_REGISTRATION_FAILURE reader@example.test private-code\n\x00 " + "x" * 2000,
            "user_code": "private-code",
        }
        with mock.patch.object(self.auth, "_post", return_value=response):
            with self.assertRaises(MailError) as failure:
                self.auth.start(self.store.saved["id"])
        error = failure.exception
        self.assertEqual("microsoft_configuration", error.code)
        local = error.result(include_server_response=True)
        self.assertIn("SYNTHETIC_REGISTRATION_FAILURE", local["server_response"])
        self.assertNotIn("reader@example.test", local["server_response"])
        self.assertNotIn("private-code", local["server_response"])
        self.assertNotIn("\x00", local["server_response"])
        self.assertNotIn("\n", local["server_response"])
        self.assertLessEqual(len(local["server_response"]), 1040)
        self.assertNotIn("server_response", error.result())
        self.assertNotIn("SYNTHETIC_REGISTRATION_FAILURE", str(error))
        self.assertEqual({}, self.store.vault.values)

    def test_refresh_error_redacts_request_token_and_returned_secrets(self):
        refresh = "synthetic-refresh/+value"
        reference = "oauth-" + "c" * 32
        self.store.saved["oauth_secret"] = reference
        self.store.vault.put(reference, refresh)
        encoded = urllib.parse.quote_plus(refresh)
        response = {
            "error": "invalid_grant", "access_token": "private-access",
            "error_description": f"SYNTHETIC_EXPIRED_GRANT {refresh} {encoded} private-access",
        }
        with mock.patch.object(self.auth, "_post", return_value=response):
            with self.assertRaises(MailError) as failure:
                self.auth.access_token(self.store.saved)
        detail = failure.exception.server_response
        self.assertIn("SYNTHETIC_EXPIRED_GRANT", detail)
        for secret in (refresh, encoded, "private-access"):
            self.assertNotIn(secret, detail)
        self.assertEqual(refresh, self.store.vault.get(reference))

    def test_poll_error_keeps_server_reason_when_cleanup_is_pending(self):
        with mock.patch.object(self.auth, "_post", return_value=self.device_response()) as post:
            started = self.auth.start(self.store.saved["id"])
            self.clock.return_value = 1005
            post.return_value = {"error": "access_denied", "error_description": "SYNTHETIC_ACCOUNT_BLOCKED"}
            with mock.patch.object(self.store, "retire_protected", return_value={"cleanup_pending": True}):
                with self.assertRaises(MailError) as failure:
                    self.auth.poll(started["flow_id"])
        self.assertEqual("authorization_declined", failure.exception.code)
        self.assertIn("SYNTHETIC_ACCOUNT_BLOCKED", failure.exception.server_response)
        self.assertIn("Retry cleanup", str(failure.exception))

    def test_malformed_token_error_discards_flow_with_sanitized_failure(self):
        with mock.patch.object(self.auth, "_post", return_value=self.device_response()) as post:
            started = self.auth.start(self.store.saved["id"])
            self.clock.return_value = 1005
            post.return_value = {"error": {"private": "synthetic-secret-value"}}
            with self.assertRaises(MailError) as failure:
                self.auth.poll(started["flow_id"])

        self.assertEqual("microsoft_response", failure.exception.code)
        self.assertNotIn("synthetic-secret-value", str(failure.exception))
        self.assertNotIn("signin-" + started["flow_id"], self.store.vault.values)

    def real_store(self):
        """Use the real settings transaction with an isolated in-memory vault."""
        vault = MemoryVault()
        with mock.patch("agentinbox.storage.Vault", return_value=vault):
            store = Store(self.store.root / "real-store")
        value = {key: copy.deepcopy(value) for key, value in self.store.saved.items()
                 if key != "revision" and not key.endswith("_secret")}
        value["id"] = ""
        saved = store.save(value, {"incoming": "", "smtp": ""}, None)
        return store, saved

    def test_real_store_connection_and_refresh_preserve_public_and_secret_boundaries(self):
        store, saved = self.real_store()
        auth = MicrosoftAuth(store)
        with mock.patch.object(auth, "_post", return_value=self.device_response()) as post:
            started = auth.start(saved["id"])
            self.clock.return_value = 1005
            post.return_value = self.token_response()
            connected = auth.poll(started["flow_id"])["account"]

        self.assertTrue(connected["microsoft_connected"])
        self.assertEqual(saved["revision"] + 1, connected["revision"])
        self.assertFalse(any(key.endswith("_secret") for key in connected))
        current = store.account(saved["id"])
        reference = current["oauth_secret"]
        self.assertIsNone(current["incoming_secret"])
        self.assertIsNone(current["smtp_secret"])
        self.assertEqual("synthetic-refresh-token", store.vault.get(reference))
        self.assertNotIn("synthetic-refresh-token", store.settings.read_text(encoding="utf-8"))

        fresh_process = MicrosoftAuth(store)
        with mock.patch.object(fresh_process, "_post", return_value=self.token_response(refresh_token="rotated-synthetic-refresh")):
            fresh_process.access_token(current)
        refreshed = store.account(saved["id"])
        self.assertEqual(current["revision"], refreshed["revision"])
        self.assertEqual(reference, refreshed["oauth_secret"])
        self.assertEqual("rotated-synthetic-refresh", store.vault.get(reference))
        self.assertFalse(any("synthetic-access-token" in value for value in store.vault.values.values()))

    def test_saving_provider_choice_preserves_microsoft_connection(self):
        store, saved = self.real_store()
        connected = store.connect_oauth(saved["id"], saved["revision"], "synthetic-refresh-token")
        before = store.account(saved["id"])
        value = {key: copy.deepcopy(value) for key, value in before.items()
                 if key != "revision" and not key.endswith("_secret")}
        value["preset"] = "outlook"
        updated = store.save(value, {"incoming": "", "smtp": ""}, connected["revision"])
        after = store.account(saved["id"])
        self.assertEqual("outlook", updated["preset"])
        self.assertTrue(updated["microsoft_connected"])
        self.assertEqual(before["oauth_secret"], after["oauth_secret"])
        self.assertEqual("synthetic-refresh-token", store.vault.get(after["oauth_secret"]))

    def test_real_store_enabling_smtp_invalidates_connection_and_blocks_stale_access(self):
        store, saved = self.real_store()
        connected = store.connect_oauth(saved["id"], saved["revision"], "synthetic-refresh-token")
        before = store.account(saved["id"])
        value = {key: copy.deepcopy(value) for key, value in before.items()
                 if key != "revision" and not key.endswith("_secret")}
        value["smtp"] = {"host": "smtp.office365.com", "port": 587, "tls": "starttls", "username": "reader@example.test"}
        value["send_enabled"] = True
        updated = store.save(value, {"incoming": "", "smtp": ""}, connected["revision"])

        self.assertFalse(updated["microsoft_connected"])
        self.assertNotIn(before["oauth_secret"], store.vault.values)
        auth = MicrosoftAuth(store)
        with mock.patch.object(auth, "_post", side_effect=AssertionError("Invalidated credentials must never reach HTTP.")):
            with self.assertRaises(MailError) as stale:
                auth.access_token(before)
            self.assertEqual("configuration_changed", stale.exception.code)
            with self.assertRaises(MailError) as disconnected:
                auth.access_token(store.account(saved["id"]))
            self.assertEqual("microsoft_signin_required", disconnected.exception.code)

    def test_connected_flow_reports_pending_cleanup_without_replaying_consumed_code(self):
        store, saved = self.real_store()
        auth = MicrosoftAuth(store)
        delete = store.vault.delete

        def fail_flow_cleanup(reference):
            if reference.startswith("signin-"):
                raise MailError("secure_storage_unavailable", "Synthetic sign-in blob is locked.")
            delete(reference)

        with mock.patch.object(auth, "_post", return_value=self.device_response()) as post:
            started = auth.start(saved["id"])
            self.clock.return_value = 1005
            post.return_value = self.token_response()
            with mock.patch.object(store.vault, "delete", side_effect=fail_flow_cleanup):
                connected = auth.poll(started["flow_id"])
                with self.assertRaises(MailError) as reused:
                    auth.poll(started["flow_id"])

        self.assertEqual("connected", connected["status"])
        self.assertTrue(connected["account"]["microsoft_connected"])
        self.assertTrue(connected["cleanup_pending"])
        self.assertEqual("secure_storage_unavailable", connected["cleanup_error"])
        self.assertEqual("signin_expired", reused.exception.code)
        self.assertEqual(2, post.call_count)
        self.assertIn("signin-" + started["flow_id"], store.vault.values)
        self.assertFalse(store.retry_cleanup()["cleanup_pending"])
        self.assertNotIn("signin-" + started["flow_id"], store.vault.values)

    def test_connected_flow_does_not_reacquire_settings_lock_after_commit(self):
        store, saved = self.real_store()
        auth = MicrosoftAuth(store)
        connect = store.connect_oauth
        lock = store.locked
        committed = False

        def connect_then_compete(*args, **kwargs):
            nonlocal committed
            result = connect(*args, **kwargs)
            committed = True
            return result

        @contextlib.contextmanager
        def busy_after_commit():
            if committed:
                raise MailError("profile_busy", "Synthetic concurrent settings writer.")
            with lock():
                yield

        with mock.patch.object(auth, "_post", return_value=self.device_response()) as post:
            started = auth.start(saved["id"])
            self.clock.return_value = 1005
            post.return_value = self.token_response()
            with mock.patch.object(store, "locked", side_effect=busy_after_commit):
                with mock.patch.object(store, "connect_oauth", side_effect=connect_then_compete):
                    result = auth.poll(started["flow_id"])

        self.assertEqual("connected", result["status"])
        self.assertTrue(result["account"]["microsoft_connected"])
        self.assertFalse(result["cleanup_pending"])

    def test_cancelled_flow_remains_consumed_when_protected_blob_deletion_fails(self):
        store, saved = self.real_store()
        auth = MicrosoftAuth(store)
        with mock.patch.object(auth, "_post", return_value=self.device_response()) as post:
            started = auth.start(saved["id"])
            with mock.patch.object(store.vault, "delete", side_effect=MailError("secure_storage_unavailable", "Synthetic locked blob.")):
                cancelled = auth.cancel(started["flow_id"])
            self.clock.return_value = 1005
            with self.assertRaises(MailError) as reused:
                auth.poll(started["flow_id"])

        self.assertEqual("cancelled", cancelled["status"])
        self.assertTrue(cancelled["cleanup_pending"])
        self.assertEqual("signin_expired", reused.exception.code)
        self.assertEqual(1, post.call_count)
