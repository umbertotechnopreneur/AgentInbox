"""Windows storage routing with both native protection APIs replaced by fakes."""

from unittest import mock

from agentinbox import vault
from agentinbox.errors import MailError

from tests._support import MailApplicationCase


class WindowsVaultRoutingTests(MailApplicationCase):
    """Check storage selection without calling WinCred, DPAPI or a real keychain."""

    def test_credentials_use_wincred_without_creating_protected_files(self):
        protected = vault.Vault(self.root)
        for prefix in ("incoming", "smtp", "oauth"):
            with self.subTest(prefix=prefix):
                key = prefix + "-" + "a" * 32
                with mock.patch.object(vault.sys, "platform", "win32"), \
                     mock.patch.object(protected, "_wincred", return_value=b"synthetic-secret") as wincred, \
                     mock.patch.object(protected, "_windows") as dpapi:
                    protected.put(key, "synthetic-secret")
                    self.assertEqual("synthetic-secret", protected.get(key))
                    protected.delete(key)
                self.assertEqual([
                    mock.call(key, b"synthetic-secret"), mock.call(key), mock.call(key, delete=True)
                ], wincred.call_args_list)
                dpapi.assert_not_called()
        self.assertFalse(protected.root.exists())

    def test_pending_content_uses_dpapi_files_without_wincred(self):
        protected = vault.Vault(self.root)

        def fake_dpapi(value, *, protect):
            return b"synthetic-protected:" + value if protect else value.removeprefix(b"synthetic-protected:")

        for prefix in ("signin",):
            with self.subTest(prefix=prefix):
                key = prefix + "-" + "b" * 32
                path = protected.root / (key + ".bin")
                with mock.patch.object(vault.sys, "platform", "win32"), \
                     mock.patch.object(protected, "_wincred") as wincred, \
                     mock.patch.object(protected, "_windows", side_effect=fake_dpapi) as dpapi:
                    protected.put(key, "synthetic-content")
                    self.assertEqual(b"synthetic-protected:synthetic-content", path.read_bytes())
                    self.assertEqual("synthetic-content", protected.get(key))
                    protected.delete(key)
                self.assertFalse(path.exists())
                self.assertEqual(2, dpapi.call_count)
                wincred.assert_not_called()

    def test_unavailable_wincred_does_not_fall_back_to_files(self):
        protected = vault.Vault(self.root)
        key = "incoming-" + "c" * 32
        failure = MailError("secure_storage_unavailable", "Synthetic unavailable WinCred.")
        with mock.patch.object(vault.sys, "platform", "win32"), \
             mock.patch.object(protected, "_wincred", side_effect=failure), \
             mock.patch.object(protected, "_windows") as dpapi:
            with self.assertRaises(MailError) as raised:
                protected.put(key, "synthetic-secret")
        self.assertEqual("secure_storage_unavailable", raised.exception.code)
        dpapi.assert_not_called()
        self.assertFalse(protected.root.exists())
