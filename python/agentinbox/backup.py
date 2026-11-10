# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Local authenticated encryption and file handling for account backups.
# Created with AI: OpenAI Codex, 2026-11-10.
# Human guidance: Umberto Giacobbi; ask the user for a backup passphrase locally.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Export and restore current account settings through a local encrypted archive."""

import ctypes
import ctypes.util
import hashlib
import json
import os
from pathlib import Path
import secrets
import sys
import tempfile

from .config import SCHEMA_VERSION
from .errors import MailError

MAGIC = b"AgentInbox backup\x00\x01"
SALT_BYTES = 16
NONCE_BYTES = 12
TAG_BYTES = 16
HEADER_BYTES = len(MAGIC) + SALT_BYTES + NONCE_BYTES
MAX_PAYLOAD_BYTES = 64 * 1024 * 1024
GET_TAG = 0x10
SET_TAG = 0x11


def _crypto():
    """Bind the OpenSSL AES-GCM API already supplied by the OS or Python runtime."""
    if sys.platform == "win32":
        roots = (Path(sys.executable).parent, Path(sys.base_prefix) / "DLLs")
        library = next((str(root / "libcrypto-3.dll") for root in roots
                        if (root / "libcrypto-3.dll").is_file()), None)
    else:
        library = ctypes.util.find_library("crypto")
    if not library:
        raise MailError("backup_crypto_unavailable", "OpenSSL encryption is unavailable in this runtime.")
    pointer = ctypes.c_void_p
    size = ctypes.c_int
    size_pointer = ctypes.POINTER(size)
    bindings = {
        "EVP_CIPHER_CTX_new": (pointer, []),
        "EVP_CIPHER_CTX_free": (None, [pointer]),
        "EVP_aes_256_gcm": (pointer, []),
        "EVP_CipherInit_ex": (size, [pointer, pointer, pointer, pointer, pointer, size]),
        "EVP_CipherUpdate": (size, [pointer, pointer, size_pointer, pointer, size]),
        "EVP_CipherFinal_ex": (size, [pointer, pointer, size_pointer]),
        "EVP_CIPHER_CTX_ctrl": (size, [pointer, size, size, pointer]),
    }
    try:
        crypto = ctypes.CDLL(library)
        for name, (result, arguments) in bindings.items():
            function = getattr(crypto, name)
            function.restype = result
            function.argtypes = arguments
    except (OSError, AttributeError) as error:
        raise MailError("backup_crypto_unavailable", "OpenSSL AES-GCM is unavailable in this runtime.") from error
    return crypto


def _cipher(payload, header, passphrase, *, tag=None):
    """Encrypt or authenticate and decrypt one bounded archive body in memory.

    Args:
        payload: Plaintext JSON for export, or ciphertext for import.
        header: Format signature, random salt and 96-bit nonce authenticated as associated data.
        passphrase: Local user-supplied backup passphrase.
        tag: Existing 128-bit GCM tag for import. None requests encryption and tag generation.
    """
    try:
        phrase = passphrase.encode("utf-8")
        if not 1 <= len(phrase) <= 1024:
            raise MailError("invalid_passphrase", "Enter a passphrase of up to 1,024 UTF-8 bytes.")
        salt = header[len(MAGIC):len(MAGIC) + SALT_BYTES]
        key = hashlib.scrypt(phrase, salt=salt, n=2**17, r=8, p=1, maxmem=160 * 1024 * 1024, dklen=32)
    except (AttributeError, ValueError, MemoryError) as error:
        raise MailError("backup_crypto_unavailable", "The runtime could not derive the backup encryption key.") from error

    crypto = _crypto()
    context = crypto.EVP_CIPHER_CTX_new()
    if not context:
        raise MailError("backup_crypto_unavailable", "The runtime could not initialize backup encryption.")
    try:
        encrypt = tag is None
        nonce = header[-NONCE_BYTES:]
        written = ctypes.c_int()
        output = ctypes.create_string_buffer(len(payload) + TAG_BYTES)
        initialized = crypto.EVP_CipherInit_ex(context, crypto.EVP_aes_256_gcm(), None, key, nonce, int(encrypt))
        if (initialized != 1
                or crypto.EVP_CipherUpdate(context, None, ctypes.byref(written), header, len(header)) != 1
                or crypto.EVP_CipherUpdate(context, output, ctypes.byref(written), payload, len(payload)) != 1):
            raise MailError("backup_crypto_failed", "The backup encryption operation could not finish.")
        if not encrypt and crypto.EVP_CIPHER_CTX_ctrl(context, SET_TAG, TAG_BYTES, tag) != 1:
            raise MailError("backup_crypto_failed", "The backup authentication tag could not be loaded.")
        remaining = ctypes.c_int()
        if crypto.EVP_CipherFinal_ex(context, ctypes.byref(output, written.value), ctypes.byref(remaining)) != 1:
            if not encrypt:
                raise MailError("backup_authentication_failed", "The passphrase is wrong or the backup was changed.")
            raise MailError("backup_crypto_failed", "The backup encryption operation could not finish.")
        result = ctypes.string_at(output, written.value + remaining.value)
        if not encrypt:
            return result
        generated_tag = ctypes.create_string_buffer(TAG_BYTES)
        if crypto.EVP_CIPHER_CTX_ctrl(context, GET_TAG, TAG_BYTES, generated_tag) != 1:
            raise MailError("backup_crypto_failed", "The backup authentication tag could not be created.")
        return result + generated_tag.raw
    finally:
        crypto.EVP_CIPHER_CTX_free(context)


def export_backup(store, path, passphrase):
    """Publish an encrypted backup without replacing any existing file.

    Args:
        store: Selected profile whose settings and saved endpoint passwords are exported.
        path: New backup filename in an existing directory outside the profile.
        passphrase: User's confirmed passphrase from hidden terminal input.
    """
    if len(passphrase) < 12:
        raise MailError("weak_passphrase", "Choose a backup passphrase with at least 12 characters.")
    path = path.parent.resolve() / path.name
    if path.is_relative_to(store.root):
        raise MailError("invalid_backup_path", "Choose a backup file outside the AgentInbox profile directory.")
    if path.exists() or path.is_symlink():
        raise MailError("backup_exists", "A file already exists at this destination. Choose a new backup filename.")
    entries = store.backup_snapshot()
    document = {"account_schema": SCHEMA_VERSION, "accounts": entries}
    payload = json.dumps(document, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    if len(payload) > MAX_PAYLOAD_BYTES:
        raise MailError("backup_too_large", "The configuration exceeds the supported backup size.")
    header = MAGIC + secrets.token_bytes(SALT_BYTES) + secrets.token_bytes(NONCE_BYTES)
    ciphertext = _cipher(payload, header, passphrase)
    result = {"accounts": len(entries), "temporary_cleanup_pending": False}
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".agentinbox-backup-", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(header)
            stream.write(ciphertext)
            stream.flush()
            os.fsync(stream.fileno())
        # Publish the complete encrypted file atomically without replacing an existing name.
        os.link(temporary, path)
    except FileExistsError as error:
        raise MailError("backup_exists", "A file already exists at this destination. Choose a new backup filename.") from error
    except OSError as error:
        raise MailError("backup_write_failed", "Use a writable directory that supports atomic hard links for backup files.") from error
    finally:
        if temporary is not None:
            try:
                temporary.unlink(missing_ok=True)
            except OSError:
                result["temporary_cleanup_pending"] = True
    return result


def import_backup(store, path, passphrase):
    """Authenticate and restore one current-format backup into an empty profile.

    Args:
        store: Empty destination profile using the current user's native credential store.
        path: Regular local file containing an encrypted AgentInbox backup.
        passphrase: Original backup passphrase from hidden terminal input.
    """
    try:
        if not path.is_file():
            raise OSError()
        with path.open("rb") as stream:
            encrypted = stream.read(HEADER_BYTES + MAX_PAYLOAD_BYTES + TAG_BYTES + 1)
    except OSError as error:
        raise MailError("backup_read_failed", "Choose a readable AgentInbox backup file.") from error
    if (not HEADER_BYTES + TAG_BYTES < len(encrypted) <= HEADER_BYTES + MAX_PAYLOAD_BYTES + TAG_BYTES
            or not encrypted.startswith(MAGIC)):
        raise MailError("invalid_backup", "The file is not a supported AgentInbox encrypted backup.")
    payload = _cipher(encrypted[HEADER_BYTES:-TAG_BYTES], encrypted[:HEADER_BYTES], passphrase,
                      tag=encrypted[-TAG_BYTES:])
    try:
        document = json.loads(payload)
    except (ValueError, UnicodeError, RecursionError) as error:
        raise MailError("invalid_backup", "The decrypted backup has an invalid configuration format.") from error
    if (not isinstance(document, dict) or set(document) != {"account_schema", "accounts"}
            or type(document["account_schema"]) is not int or document["account_schema"] != SCHEMA_VERSION
            or not isinstance(document["accounts"], list)):
        raise MailError("invalid_backup", "The backup does not use the current AgentInbox account schema.")
    return store.restore_backup(document["accounts"])
