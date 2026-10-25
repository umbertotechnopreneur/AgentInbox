"""Operating system protected storage with no plaintext fallback."""

import ctypes
import ctypes.util
import os
import re
import sys
import threading
from ctypes import wintypes
from pathlib import Path

from .errors import MailError


class Vault:
    """Protect credentials and temporary sign-in data using the current user's OS facilities."""

    def __init__(self, root: Path):
        self.root = root / "protected"

    def _key(self, key: str) -> str:
        if sys.platform not in {"win32", "darwin", "linux"}:
            raise MailError("unsupported_platform", "AgentInbox supports Windows, macOS and Linux only.")
        if not re.fullmatch(r"(?:incoming|smtp|oauth|signin)-[a-f0-9]{32}", key):
            raise MailError("invalid_reference", "Invalid protected-storage reference.")
        return key

    def put(self, key: str, value: str) -> None:
        """Protect a value before making it available to an account or request."""
        key = self._key(key)
        if "\0" in value:
            raise MailError("invalid_credential", "Protected values cannot contain a NUL character.")
        if sys.platform == "win32" and not key.startswith("signin-"):
            self._wincred(key, value.encode("utf-8"))
        elif sys.platform == "win32":
            self.root.mkdir(parents=True, exist_ok=True)
            encrypted = self._windows(value.encode("utf-8"), protect=True)
            path = self.root / (key + ".bin")
            temporary = path.with_suffix(".tmp")
            try:
                with temporary.open("xb") as stream:
                    stream.write(encrypted)
                    stream.flush()
                    os.fsync(stream.fileno())
                os.replace(temporary, path)
            finally:
                temporary.unlink(missing_ok=True)
        elif sys.platform == "darwin":
            self._macos(key, value.encode("utf-8"))
        else:
            self._linux(key, value)

    def get(self, key: str) -> str:
        """Read one protected value without exposing backend error details."""
        key = self._key(key)
        if sys.platform == "win32" and not key.startswith("signin-"):
            return self._wincred(key).decode("utf-8")
        if sys.platform == "win32":
            try:
                return self._windows((self.root / (key + ".bin")).read_bytes(), protect=False).decode("utf-8")
            except (OSError, UnicodeError) as error:
                raise MailError("credential_unavailable", "Protected data is unavailable. Re-enter the account credentials.") from error
        if sys.platform == "darwin":
            return self._macos(key).decode("utf-8")
        return self._linux(key)

    def delete(self, key: str) -> None:
        """Remove a protected value, failing if the secure store cannot be reached."""
        key = self._key(key)
        if sys.platform == "win32" and not key.startswith("signin-"):
            self._wincred(key, delete=True)
        elif sys.platform == "win32":
            (self.root / (key + ".bin")).unlink(missing_ok=True)
        elif sys.platform == "darwin":
            self._macos(key, delete=True)
        else:
            self._linux(key, delete=True)

    @staticmethod
    def _wincred(key: str, value: bytes | None = None, *, delete: bool = False) -> bytes:
        """Store credentials in the current user's Windows Credential Manager."""
        class Credential(ctypes.Structure):
            _fields_ = [("Flags", wintypes.DWORD), ("Type", wintypes.DWORD),
                        ("TargetName", wintypes.LPWSTR), ("Comment", wintypes.LPWSTR),
                        ("LastWritten", wintypes.FILETIME), ("CredentialBlobSize", wintypes.DWORD),
                        ("CredentialBlob", ctypes.POINTER(ctypes.c_ubyte)), ("Persist", wintypes.DWORD),
                        ("AttributeCount", wintypes.DWORD), ("Attributes", ctypes.c_void_p),
                        ("TargetAlias", wintypes.LPWSTR), ("UserName", wintypes.LPWSTR)]

        try:
            api = ctypes.WinDLL("advapi32", use_last_error=True)
            api.CredWriteW.argtypes = [ctypes.POINTER(Credential), wintypes.DWORD]
            api.CredWriteW.restype = wintypes.BOOL
            api.CredReadW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.POINTER(ctypes.POINTER(Credential))]
            api.CredReadW.restype = wintypes.BOOL
            api.CredDeleteW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD]
            api.CredDeleteW.restype = wintypes.BOOL
            api.CredFree.argtypes = [ctypes.c_void_p]
            api.CredFree.restype = None
        except (OSError, AttributeError) as error:
            raise MailError("secure_storage_unavailable", "Windows Credential Manager is unavailable.") from error
        target = "AgentInbox/" + key
        if delete:
            if not api.CredDeleteW(target, 1, 0) and ctypes.get_last_error() != 1168:
                raise MailError("secure_storage_unavailable", "Windows Credential Manager could not remove this credential.")
            return b""
        if value is not None:
            # CRED_MAX_CREDENTIAL_BLOB_SIZE is 5 * 512 bytes; never truncate a secret.
            if not 0 < len(value) <= 2560:
                raise MailError("credential_too_large", "Windows Credential Manager requires a nonempty credential of at most 2560 UTF-8 bytes.")
            buffer = (ctypes.c_ubyte * len(value)).from_buffer_copy(value)
            credential = Credential(Type=1, TargetName=target, CredentialBlobSize=len(value),
                                    CredentialBlob=buffer, Persist=2, UserName="AgentInbox")
            if not api.CredWriteW(ctypes.byref(credential), 0):
                raise MailError("secure_storage_unavailable", "Windows Credential Manager could not protect this credential.")
            return b""
        found = ctypes.POINTER(Credential)()
        if not api.CredReadW(target, 1, 0, ctypes.byref(found)):
            raise MailError("credential_unavailable", "The Windows credential is unavailable. Re-enter it or sign in again.")
        try:
            if not 0 < found.contents.CredentialBlobSize <= 2560:
                raise MailError("credential_unavailable", "The Windows credential has an invalid size.")
            return ctypes.string_at(found.contents.CredentialBlob, found.contents.CredentialBlobSize)
        finally:
            api.CredFree(found)

    @staticmethod
    def _windows(value: bytes, *, protect: bool) -> bytes:
        class Blob(ctypes.Structure):
            _fields_ = [("size", wintypes.DWORD), ("data", ctypes.POINTER(ctypes.c_ubyte))]

        crypt = ctypes.WinDLL("crypt32", use_last_error=True)
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.LocalFree.argtypes = [ctypes.c_void_p]
        kernel.LocalFree.restype = ctypes.c_void_p
        buffer = (ctypes.c_ubyte * len(value)).from_buffer_copy(value)
        source, destination = Blob(len(value), buffer), Blob()
        function = crypt.CryptProtectData if protect else crypt.CryptUnprotectData
        function.argtypes = [ctypes.POINTER(Blob), ctypes.c_void_p, ctypes.c_void_p,
                             ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(Blob)]
        function.restype = wintypes.BOOL
        if not function(ctypes.byref(source), None, None, None, None, 1, ctypes.byref(destination)):
            raise MailError("secure_storage_unavailable", "Windows user-protected storage is unavailable.")
        try:
            return ctypes.string_at(destination.data, destination.size)
        finally:
            kernel.LocalFree(destination.data)

    @staticmethod
    def _linux(key: str, value: str | None = None, *, delete: bool = False) -> str:
        # Native libsecret avoids secret-tool's small stdin buffer for protected MIME.
        try:
            libraries = [ctypes.util.find_library(name) for name in ("secret-1", "glib-2.0", "gio-2.0", "gobject-2.0")]
            if not all(libraries):
                raise OSError()
            secret, glib, gio, objects = [ctypes.CDLL(name) for name in libraries]
        except OSError as error:
            raise MailError("secure_storage_unavailable", "Linux requires libsecret and an unlocked Secret Service keyring in the user session running MCP.") from error
        pointer = ctypes.c_void_p
        glib.g_hash_table_new.argtypes = [pointer, pointer]
        glib.g_hash_table_new.restype = pointer
        glib.g_hash_table_insert.argtypes = [pointer, ctypes.c_char_p, ctypes.c_char_p]
        glib.g_hash_table_insert.restype = ctypes.c_int
        glib.g_hash_table_unref.argtypes = [pointer]
        glib.g_hash_table_unref.restype = None
        glib.g_error_free.argtypes = [pointer]
        glib.g_error_free.restype = None
        gio.g_cancellable_new.argtypes = []
        gio.g_cancellable_new.restype = pointer
        gio.g_cancellable_cancel.argtypes = [pointer]
        gio.g_cancellable_cancel.restype = None
        objects.g_object_unref.argtypes = [pointer]
        objects.g_object_unref.restype = None
        secret.secret_password_storev_sync.argtypes = [pointer, pointer, ctypes.c_char_p, ctypes.c_char_p, ctypes.c_char_p, pointer, ctypes.POINTER(pointer)]
        secret.secret_password_storev_sync.restype = ctypes.c_int
        secret.secret_password_lookupv_sync.argtypes = [pointer, pointer, pointer, ctypes.POINTER(pointer)]
        secret.secret_password_lookupv_sync.restype = pointer
        secret.secret_password_clearv_sync.argtypes = [pointer, pointer, pointer, ctypes.POINTER(pointer)]
        secret.secret_password_clearv_sync.restype = ctypes.c_int
        secret.secret_password_free.argtypes = [pointer]
        secret.secret_password_free.restype = None
        attributes = glib.g_hash_table_new(ctypes.cast(glib.g_str_hash, pointer), ctypes.cast(glib.g_str_equal, pointer))
        pairs = [(b"application", b"AgentInbox"), (b"entry", key.encode("ascii"))]
        for name, entry in pairs:
            glib.g_hash_table_insert(attributes, name, entry)
        cancellation = gio.g_cancellable_new()
        timer = threading.Timer(30, gio.g_cancellable_cancel, [cancellation])
        timer.daemon = True
        error, found = pointer(), None
        timer.start()
        try:
            if delete:
                secret.secret_password_clearv_sync(None, attributes, cancellation, ctypes.byref(error))
                result = ""
            elif value is not None:
                stored = secret.secret_password_storev_sync(None, attributes, None, b"AgentInbox", value.encode("utf-8"), cancellation, ctypes.byref(error))
                if not stored and not error:
                    raise MailError("secure_storage_unavailable", "Secret Service did not protect this value.")
                result = ""
            else:
                found = secret.secret_password_lookupv_sync(None, attributes, cancellation, ctypes.byref(error))
                if not found and not error:
                    raise MailError("credential_unavailable", "The keyring entry is missing. Re-enter the account credentials.")
                result = ctypes.string_at(found).decode("utf-8") if found else ""
            if error:
                raise MailError("secure_storage_unavailable", "Secret Service is unavailable, locked or did not finish within thirty seconds.")
            return result
        finally:
            timer.cancel()
            timer.join()
            if found:
                secret.secret_password_free(found)
            if error:
                glib.g_error_free(error)
            objects.g_object_unref(cancellation)
            glib.g_hash_table_unref(attributes)

    @staticmethod
    def _macos(key: str, value: bytes | None = None, *, delete: bool = False) -> bytes:
        try:
            security = ctypes.CDLL("/System/Library/Frameworks/Security.framework/Security")
            core = ctypes.CDLL("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")
        except OSError as error:
            raise MailError("secure_storage_unavailable", "The macOS Keychain frameworks are unavailable.") from error
        pointer, length = ctypes.c_void_p(), ctypes.c_uint32()
        item = ctypes.c_void_p()
        service, account = b"AgentInbox", key.encode("ascii")
        find = security.SecKeychainFindGenericPassword
        find.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_char_p, ctypes.c_uint32,
                         ctypes.c_char_p, ctypes.POINTER(ctypes.c_uint32), ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p)]
        find.restype = ctypes.c_int32
        security.SecKeychainItemFreeContent.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
        security.SecKeychainItemFreeContent.restype = ctypes.c_int32
        security.SecKeychainItemDelete.argtypes = [ctypes.c_void_p]
        security.SecKeychainItemDelete.restype = ctypes.c_int32
        core.CFRelease.argtypes = [ctypes.c_void_p]
        core.CFRelease.restype = None
        status = find(None, len(service), service, len(account), account, ctypes.byref(length), ctypes.byref(pointer), ctypes.byref(item))
        try:
            if status not in (0, -25300):
                raise MailError("secure_storage_unavailable", "The macOS login keychain is unavailable or locked.")
            if delete:
                if status == 0 and security.SecKeychainItemDelete(item):
                    raise MailError("secure_storage_unavailable", "The keychain entry could not be removed.")
                return b""
            if value is None:
                if status:
                    raise MailError("credential_unavailable", "The keychain entry is missing. Re-enter the account credentials.")
                return ctypes.string_at(pointer, length.value)
            if status == 0:
                modify = security.SecKeychainItemModifyAttributesAndData
                modify.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_void_p]
                modify.restype = ctypes.c_int32
                result = modify(item, None, len(value), ctypes.c_char_p(value))
            else:
                add = security.SecKeychainAddGenericPassword
                add.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_char_p, ctypes.c_uint32,
                                ctypes.c_char_p, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_void_p]
                add.restype = ctypes.c_int32
                result = add(None, len(service), service, len(account), account, len(value), ctypes.c_char_p(value), None)
            if result:
                raise MailError("secure_storage_unavailable", "The keychain could not protect this value.")
            return b""
        finally:
            if pointer:
                security.SecKeychainItemFreeContent(None, pointer)
            if item:
                core.CFRelease(item)
