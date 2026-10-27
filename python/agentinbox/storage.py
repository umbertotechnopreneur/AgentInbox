# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Atomic account settings and durable protected-credential cleanup.
# Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
# Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
# Evidence: docs/VALIDATION.md (source review; execution checks pending).
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Atomic account settings and durable protected-credential cleanup."""

import contextlib
import json
import os
import time
import uuid
from pathlib import Path

from .config import SCHEMA_VERSION, account_input
from .errors import MailError
from .vault import Vault
from .models import SavedAccount


MAX_SETTINGS_BYTES = 256_000
MAX_CLEANUP_REFERENCES = 2_000


class Store:
    """Own settings writes across the desktop, CLI and MCP processes."""

    def __init__(self, root: Path):
        """Bind settings, credential storage and cleanup paths to one profile.

        Args:
            root: Absolute directory of the selected local profile; no alternate profile
                is selected here.
        """
        self.root = root
        self.settings = root / "accounts.json"
        self.cleanup_path = root / "cleanup.json"
        self.vault = Vault(root)

    @contextlib.contextmanager
    def locked(self):
        """Serialize local mutations; never hold this lock during network access."""
        self.root.mkdir(mode=0o700, parents=True, exist_ok=True)
        with (self.root / ".settings.lock").open("a+b") as stream:
            stream.seek(0, os.SEEK_END)
            if stream.tell() == 0:
                stream.write(b"\0")
                stream.flush()
            stream.seek(0)
            if os.name == "nt":
                import msvcrt
                try:
                    msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
                except OSError as error:
                    raise MailError("profile_busy", "Another process is changing this profile. Try again.") from error
            else:
                import fcntl
                try:
                    fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                except OSError as error:
                    raise MailError("profile_busy", "Another process is changing this profile. Try again.") from error
            try:
                yield
            finally:
                stream.seek(0)
                if os.name == "nt":
                    msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
                else:
                    fcntl.flock(stream.fileno(), fcntl.LOCK_UN)

    def accounts(self) -> list[SavedAccount]:
        """Read and validate the complete current schema without migration."""
        if not self.settings.exists():
            return []
        try:
            if self.settings.stat().st_size > MAX_SETTINGS_BYTES:
                raise ValueError()
            document = json.loads(self.settings.read_text(encoding="utf-8"))
            if set(document) != {"schema_version", "accounts"} or document["schema_version"] != SCHEMA_VERSION:
                raise ValueError()
            accounts = document["accounts"]
            if not isinstance(accounts, list):
                raise ValueError()
            identities = set()
            for saved in accounts:
                secret_fields = {"incoming_secret", "smtp_secret", "oauth_secret"}
                if not isinstance(saved, dict) or not secret_fields <= saved.keys():
                    raise ValueError()
                base = {key: value for key, value in saved.items() if key not in {"revision", *secret_fields}}
                validated = account_input(base)
                if not validated["id"] or validated != base or saved["id"] in identities:
                    raise ValueError()
                if type(saved["revision"]) is not int or saved["revision"] < 1:
                    raise ValueError()
                if saved["authentication"]["method"] == "password":
                    self.vault._key(saved["incoming_secret"])
                    if saved["smtp"] is not None:
                        self.vault._key(saved["smtp_secret"])
                    elif saved["smtp_secret"] is not None:
                        raise ValueError()
                    if saved["oauth_secret"] is not None:
                        raise ValueError()
                elif saved["incoming_secret"] is not None or saved["smtp_secret"] is not None:
                    raise ValueError()
                elif saved["oauth_secret"] is not None:
                    self.vault._key(saved["oauth_secret"])
                identities.add(saved["id"])
            return accounts
        except (ValueError, TypeError, KeyError, OSError, MailError) as error:
            raise MailError("invalid_profile", "The saved profile is invalid or incompatible. Correct the current settings or use a new profile; no migration is performed.") from error

    def account(self, identity: str) -> SavedAccount:
        """Resolve a current account and fail when it has been removed.

        Args:
            identity: Saved account ID to resolve from the current settings snapshot.
        """
        for account in self.accounts():
            if account["id"] == identity:
                return account
        raise MailError("account_not_found", "The account no longer exists.")

    def _atomic_json(self, path: Path, document: dict, maximum: int) -> None:
        """Flush bounded JSON to a private temporary file before atomically replacing metadata.

        Args:
            path: Local file path read or atomically replaced by this operation.
            document: Non-secret JSON metadata to serialize and atomically persist.
            maximum: Upper size or duration bound enforced by this operation.
        """
        payload = json.dumps(document, ensure_ascii=False, indent=2).encode("utf-8")
        if len(payload) > maximum:
            raise MailError("profile_too_large", "The saved metadata would exceed its size limit. Reduce the account settings before saving.")
        temporary = self.root / ("settings-" + uuid.uuid4().hex + ".tmp")
        try:
            descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, "wb") as stream:
                stream.write(payload)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, path)
        except BaseException:
            temporary.unlink(missing_ok=True)
            raise

    def _write(self, accounts: list[dict]) -> None:
        """Write one JSON protocol record and flush stdout.

        Args:
            accounts: Complete replacement list of validated saved-account metadata.
        """
        self._atomic_json(self.settings, {"schema_version": SCHEMA_VERSION, "accounts": accounts}, MAX_SETTINGS_BYTES)

    @staticmethod
    def _cleanup_result(count: int, error: str | None = None) -> dict:
        """Describe pending credential cleanup separately from the committed operation.

        Args:
            count: Number of protected references still waiting for cleanup.
            error: Optional safe cleanup error code; it does not alter the primary operation result.
        """
        return {"cleanup_pending": bool(count or error), "cleanup_pending_count": count, "cleanup_error": error}

    @staticmethod
    def _cleanup_error(error: Exception) -> str:
        """Reduce a cleanup exception to a safe application error code.

        Args:
            error: Failure to reduce to bounded diagnostics or a safe persisted error
                code.
        """
        return error.code if isinstance(error, MailError) else "cleanup_failed"

    def _cleanup_references(self) -> list[str]:
        """Read and validate the durable queue of protected-storage references."""
        if not self.cleanup_path.exists():
            return []
        try:
            if self.cleanup_path.stat().st_size > MAX_SETTINGS_BYTES:
                raise ValueError()
            document = json.loads(self.cleanup_path.read_text(encoding="utf-8"))
            if not isinstance(document, dict) or set(document) != {"schema_version", "references"} or type(document["schema_version"]) is not int or document["schema_version"] != 1:
                raise ValueError()
            references = document["references"]
            if not isinstance(references, list) or len(references) > MAX_CLEANUP_REFERENCES:
                raise ValueError()
            for reference in references:
                self.vault._key(reference)
                if reference.split("-", 1)[0] not in {"incoming", "smtp", "oauth", "signin"}:
                    raise ValueError()
            if len(set(references)) != len(references):
                raise ValueError()
            return references
        except (ValueError, TypeError, KeyError, OSError, MailError) as error:
            raise MailError("invalid_cleanup_queue", "The protected-data cleanup queue is invalid or unavailable. Preserve it for recovery.") from error

    def _queue_cleanup_locked(self, references) -> None:
        """Durably enqueue unique protected references while the settings lock is held.

        Args:
            references: Protected-storage keys to retire; live account credentials
                remain protected from cleanup.
        """
        pending = self._cleanup_references()
        changed = False
        for reference in references:
            self.vault._key(reference)
            if reference not in pending:
                pending.append(reference)
                changed = True
        if len(pending) > MAX_CLEANUP_REFERENCES:
            raise MailError("cleanup_queue_full", "Retry protected-data cleanup before changing more accounts.")
        if changed:
            self._atomic_json(self.cleanup_path, {"schema_version": 1, "references": pending}, MAX_SETTINGS_BYTES)

    def _live_references(self) -> set[str]:
        """Collect protected credentials still referenced by saved accounts."""
        return {account[field] for account in self.accounts()
                for field in ("incoming_secret", "smtp_secret", "oauth_secret") if account[field]}

    def _retry_cleanup_locked(self) -> dict:
        """Delete a bounded batch of retired credentials while preserving a durable retry queue."""
        pending = []
        queued = []
        try:
            queued = self._cleanup_references()
            live = self._live_references()
            obsolete = [reference for reference in queued if reference not in live]
            error_code = None
            deadline = time.monotonic() + 5
            for index, reference in enumerate(obsolete):
                if index >= 32 or time.monotonic() >= deadline:
                    pending.extend(obsolete[index:])
                    error_code = "cleanup_incomplete"
                    break
                try:
                    self.vault.delete(reference)
                except Exception as error:
                    # A locked/unavailable backend must not cause thousands of waits.
                    pending.extend(obsolete[index:])
                    error_code = self._cleanup_error(error)
                    break
            try:
                self._atomic_json(self.cleanup_path, {"schema_version": 1, "references": pending}, MAX_SETTINGS_BYTES)
            except Exception as error:
                # The previous queue remains durable, including already removed entries.
                return self._cleanup_result(len(queued), self._cleanup_error(error))
            return self._cleanup_result(len(pending), error_code)
        except Exception as error:
            return self._cleanup_result(len(queued), self._cleanup_error(error))

    def cleanup_status(self) -> dict:
        """Read cleanup metadata without deleting protected values or changing files."""
        try:
            queued = self._cleanup_references()
            if not queued:
                return self._cleanup_result(0)
            live = self._live_references()
            return self._cleanup_result(sum(reference not in live for reference in queued))
        except Exception as error:
            return self._cleanup_result(0, self._cleanup_error(error))

    def retry_cleanup(self) -> dict:
        """Retry up to 32 obsolete values; stop on failure or after the current call exceeds five seconds."""
        try:
            with self.locked():
                return self._retry_cleanup_locked()
        except Exception as error:
            status = self.cleanup_status()
            return self._cleanup_result(status["cleanup_pending_count"], self._cleanup_error(error))

    def cleanup_queued(self, reference: str) -> bool:
        """Check whether protected state has been durably marked for removal.

        Args:
            reference: Protected-storage key whose durable retirement status is queried.
        """
        self.vault._key(reference)
        return reference in self._cleanup_references()

    def retire_protected(self, references) -> dict:
        """Durably consume protected state before reporting cancellation or completion.

        Args:
            references: Protected-storage keys to retire; live account credentials
                remain protected from cleanup.
        """
        with self.locked():
            self._queue_cleanup_locked(references)
            return self._retry_cleanup_locked()

    def cleanup_protected(self, references) -> dict:
        """Queue consumed protected values and report cleanup separately from committed work.

        Args:
            references: Protected-storage keys to retire; live account credentials
                remain protected from cleanup.
        """
        references = list(references)
        try:
            return self.retire_protected(references)
        except Exception as error:
            status = self.cleanup_status()
            return self._cleanup_result(max(len(references), status["cleanup_pending_count"]), self._cleanup_error(error))

    def save(self, value: dict, passwords: dict, expected_revision: int | None) -> dict:
        """Save explicit account settings and newly protected credentials atomically.

        Args:
            value: Replacement account payload without saved revisions or credential references.
            passwords: Incoming and SMTP password fields; empty strings request
                permitted saved-credential reuse.
            expected_revision: Saved account revision being edited or checked; None
                denotes a new account.
        """
        account = account_input(value)
        if expected_revision is not None and (type(expected_revision) is not int or expected_revision < 1):
            raise MailError("invalid_configuration", "An existing account revision must be a positive integer.")
        if not isinstance(passwords, dict) or set(passwords) != {"incoming", "smtp"} or any(not isinstance(p, str) or len(p) > 4096 or "\0" in p or "\r" in p or "\n" in p for p in passwords.values()):
            raise MailError("invalid_credentials", "Check the passwords for reading and sending in AgentInbox.")
        oauth = account["authentication"]["method"] == "microsoft_oauth"
        if oauth and any(passwords.values()):
            raise MailError("invalid_credentials", "Microsoft sign-in does not need a password in this app.")

        # Serialize the revision check and commit across all local adapters.
        with self.locked():
            accounts = self.accounts()
            previous = next((item for item in accounts if item["id"] == account["id"]), None)
            if (account["id"] and previous is None) or (previous and previous["revision"] != expected_revision):
                raise MailError("configuration_changed", "The account changed in another window. Reload it before saving.")
            if not previous and expected_revision is not None:
                raise MailError("configuration_changed", "A new account cannot have an existing revision.")
            account["id"] = previous["id"] if previous else uuid.uuid4().hex
            account["revision"] = previous["revision"] + 1 if previous else 1

            # Queue new references before writing secrets so failed saves remain recoverable.
            obsolete = []
            try:
                for protocol in ("incoming", "smtp"):
                    field = protocol + "_secret"
                    if oauth or account[protocol] is None:
                        account[field] = None
                    elif passwords[protocol]:
                        reference = protocol + "-" + uuid.uuid4().hex
                        self._queue_cleanup_locked([reference])
                        self.vault.put(reference, passwords[protocol])
                        account[field] = reference
                    elif previous and previous["authentication"] == account["authentication"] and previous[protocol] == account[protocol] and previous[field]:
                        account[field] = previous[field]
                    else:
                        purpose = "reading" if protocol == "incoming" else "sending"
                        section = "Read mail" if protocol == "incoming" else "Send mail"
                        raise MailError("password_required", f"Enter the password for {purpose} under {section}. A new server or email address needs its password entered again.")
                    if previous and previous[field] and previous[field] != account[field]:
                        obsolete.append(previous[field])
                account["oauth_secret"] = None
                if oauth and previous and all(previous[key] == account[key] for key in ("authentication", "incoming", "smtp", "email")) and (not account["send_enabled"] or previous["send_enabled"]):
                    account["oauth_secret"] = previous["oauth_secret"]
                if previous and previous["oauth_secret"] and previous["oauth_secret"] != account["oauth_secret"]:
                    obsolete.append(previous["oauth_secret"])

                # The atomic settings replacement is the commit point. Cleanup follows it.
                self._queue_cleanup_locked(obsolete)
                self._write([item for item in accounts if item["id"] != account["id"]] + [account])
            except Exception:
                cleanup = self._retry_cleanup_locked()
                if cleanup["cleanup_pending"]:
                    raise MailError("save_failed_cleanup_pending", "Account changes were not committed. Protected-data cleanup remains pending; retry cleanup locally.") from None
                raise

            # A cleanup failure must not turn a committed save into a retryable save error.
            cleanup = self._retry_cleanup_locked()
        return {**self.public(account), **cleanup}

    def connect_oauth(self, identity: str, expected_revision: int, refresh_token: str, *, consumed_references=()) -> dict:
        """Attach a completed Microsoft sign-in only to its unchanged account.

        Args:
            identity: Saved Microsoft account ID to attach the refresh credential to.
            expected_revision: Saved account revision being edited or checked; None
                denotes a new account.
            refresh_token: Microsoft refresh credential to protect before attaching it
                to the unchanged account.
            consumed_references: Temporary device-flow keys to retire with the committed
                authorization.
        """
        with self.locked():
            accounts = self.accounts()
            account = next((item for item in accounts if item["id"] == identity), None)
            if account is None or account["revision"] != expected_revision or account["authentication"]["method"] != "microsoft_oauth":
                raise MailError("configuration_changed", "Account settings changed. Sign in with Microsoft again.")
            old_reference = account["oauth_secret"]
            reference = "oauth-" + uuid.uuid4().hex
            self._queue_cleanup_locked([reference, *([old_reference] if old_reference else []), *consumed_references])
            try:
                self.vault.put(reference, refresh_token)
                account["oauth_secret"] = reference
                account["revision"] += 1
                self._write(accounts)
            except Exception:
                cleanup = self._retry_cleanup_locked()
                if cleanup["cleanup_pending"]:
                    raise MailError("signin_failed_cleanup_pending", "Microsoft sign-in was not saved. Temporary sign-in data still needs to be removed. Use Retry cleanup.") from None
                raise
            cleanup = self._retry_cleanup_locked()
            return {**self.public(account), **cleanup}

    def remove(self, identity: str) -> dict:
        """Revoke local access before removing its protected credentials.

        Args:
            identity: Saved account ID whose local access is being revoked.
        """
        with self.locked():
            account = self.account(identity)
            self._queue_cleanup_locked([account[field] for field in ("incoming_secret", "smtp_secret", "oauth_secret") if account[field]])
            self._write([item for item in self.accounts() if item["id"] != identity])
            cleanup = self._retry_cleanup_locked()
            return {"removed": True, **cleanup}

    @staticmethod
    def public(account: dict) -> dict:
        """Return settings without credential references or values.

        Args:
            account: Account settings or saved snapshot used by this operation;
                credentials remain in protected storage.
        """
        return {**{key: value for key, value in account.items() if not key.endswith("_secret")},
                "microsoft_connected": bool(account.get("oauth_secret"))}
