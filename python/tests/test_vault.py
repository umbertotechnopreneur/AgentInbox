"""Platform storage routing with native protection APIs replaced by fakes."""

from unittest import mock

from agentinbox import vault
from agentinbox.errors import MailError

from tests._support import MailApplicationCase


class PlatformVaultRoutingTests(MailApplicationCase):
    """Describe native backend selection and failures without accessing credentials."""

    def test_macos_and_linux_use_their_native_backends(self):
        protected = vault.Vault(self.root)
        for platform, backend, value in (
            ("darwin", "_macos", b"synthetic-secret"),
            ("linux", "_linux", "synthetic-secret"),
        ):
            for prefix in ("incoming", "smtp", "oauth", "signin"):
                with self.subTest(platform=platform, prefix=prefix):
                    key = prefix + "-" + "a" * 32
                    with mock.patch.object(vault.sys, "platform", platform), \
                         mock.patch.object(protected, backend, return_value=value) as native:
                        protected.put(key, "synthetic-secret")
                        self.assertEqual("synthetic-secret", protected.get(key))
                        protected.delete(key)
                    self.assertEqual([
                        mock.call(key, value), mock.call(key), mock.call(key, delete=True)
                    ], native.call_args_list)
        self.assertFalse(protected.root.exists())

    def test_unsupported_platform_never_uses_linux_storage(self):
        protected = vault.Vault(self.root)
        key = "incoming-" + "a" * 32
        with mock.patch.object(vault.sys, "platform", "freebsd14"), \
             mock.patch.object(protected, "_linux") as native:
            for operation, arguments in (
                (protected.put, (key, "synthetic-secret")),
                (protected.get, (key,)),
                (protected.delete, (key,)),
            ):
                with self.subTest(operation=operation.__name__):
                    with self.assertRaises(MailError) as raised:
                        operation(*arguments)
                    self.assertEqual("unsupported_platform", raised.exception.code)
        native.assert_not_called()

    def test_nul_secret_is_rejected_before_native_storage(self):
        protected = vault.Vault(self.root)
        for platform, backend in (("win32", "_wincred"), ("darwin", "_macos"), ("linux", "_linux")):
            with self.subTest(platform=platform), \
                 mock.patch.object(vault.sys, "platform", platform), \
                 mock.patch.object(protected, backend) as native:
                with self.assertRaises(MailError) as raised:
                    protected.put("incoming-" + "a" * 32, "synthetic\0secret")
                self.assertEqual("invalid_credential", raised.exception.code)
                native.assert_not_called()

    def test_missing_macos_framework_has_a_bounded_error(self):
        protected = vault.Vault(self.root)
        with mock.patch.object(vault.sys, "platform", "darwin"), \
             mock.patch.object(vault.ctypes, "CDLL", side_effect=OSError("synthetic private loader detail")):
            with self.assertRaises(MailError) as raised:
                protected.get("incoming-" + "a" * 32)
        self.assertEqual("secure_storage_unavailable", raised.exception.code)
        self.assertNotIn("synthetic private loader detail", str(raised.exception))
        self.assertFalse(protected.root.exists())

    def test_missing_linux_libraries_do_not_create_protected_files(self):
        protected = vault.Vault(self.root)
        with mock.patch.object(vault.sys, "platform", "linux"), \
             mock.patch.object(vault.ctypes.util, "find_library", return_value=None), \
             mock.patch.object(vault.ctypes, "CDLL") as loader:
            with self.assertRaises(MailError) as raised:
                protected.put("incoming-" + "a" * 32, "synthetic-secret")
        self.assertEqual("secure_storage_unavailable", raised.exception.code)
        loader.assert_not_called()
        self.assertFalse(protected.root.exists())


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
