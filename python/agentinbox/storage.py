"""Atomic local settings and a metadata-only outgoing request ledger."""

import contextlib
import json
import os
import sqlite3
import time
import uuid
from pathlib import Path

from .config import SCHEMA_VERSION, account_input
from .errors import MailError
from .vault import Vault


MAX_SETTINGS_BYTES = 256_000
MAX_CLEANUP_REFERENCES = 2_000


class Store:
    """Own settings writes across the desktop, CLI and MCP processes."""

    def __init__(self, root: Path):
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

    def accounts(self) -> list[dict]:
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
            if not isinstance(accounts, list) or len(accounts) > 50:
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

    def account(self, identity: str) -> dict:
        """Resolve a current account and fail when it has been removed."""
        for account in self.accounts():
            if account["id"] == identity:
                return account
        raise MailError("account_not_found", "The account no longer exists.")

    def _atomic_json(self, path: Path, document: dict, maximum: int) -> None:
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
        self._atomic_json(self.settings, {"schema_version": SCHEMA_VERSION, "accounts": accounts}, MAX_SETTINGS_BYTES)

    @staticmethod
    def _cleanup_result(count: int, error: str | None = None) -> dict:
        return {"cleanup_pending": bool(count or error), "cleanup_pending_count": count, "cleanup_error": error}

    @staticmethod
    def _cleanup_error(error: Exception) -> str:
        return error.code if isinstance(error, MailError) else "cleanup_failed"

    def _cleanup_references(self) -> list[str]:
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
                if reference.split("-", 1)[0] not in {"incoming", "smtp", "oauth", "outgoing", "signin"}:
                    raise ValueError()
            if len(set(references)) != len(references):
                raise ValueError()
            return references
        except (ValueError, TypeError, KeyError, OSError, MailError) as error:
            raise MailError("invalid_cleanup_queue", "The protected-data cleanup queue is invalid or unavailable. Preserve it for recovery.") from error

    def _queue_cleanup_locked(self, references) -> None:
        pending = self._cleanup_references()
        changed = False
        for reference in references:
            self.vault._key(reference)
            if reference not in pending:
                pending.append(reference)
                changed = True
        if len(pending) > MAX_CLEANUP_REFERENCES:
            raise MailError("cleanup_queue_full", "Retry protected-data cleanup before changing more accounts or outgoing requests.")
        if changed:
            self._atomic_json(self.cleanup_path, {"schema_version": 1, "references": pending}, MAX_SETTINGS_BYTES)

    def _live_references(self) -> set[str]:
        live = {account[field] for account in self.accounts()
                for field in ("incoming_secret", "smtp_secret", "oauth_secret") if account[field]}
        outbox = Outbox(self)
        if outbox.path.exists():
            database = sqlite3.connect(outbox.path.as_uri() + "?mode=ro", uri=True, timeout=3)
            try:
                if database.execute("PRAGMA user_version").fetchone()[0] != 3:
                    raise MailError("invalid_profile", "The outgoing ledger uses an unsupported schema.")
                live.update("outgoing-" + row[0] for row in database.execute(
                    "SELECT id FROM outgoing WHERE status IN ('pending_approval', 'submitting')"))
            finally:
                database.close()
        return live

    def _expire_outgoing_locked(self) -> None:
        outbox = Outbox(self)
        if outbox.path.exists():
            with outbox.database() as database:
                database.execute("UPDATE outgoing SET status='expired' WHERE status='pending_approval' AND expires<=?", (time.time(),))
                database.commit()

    def _retry_cleanup_locked(self) -> dict:
        pending = []
        queued = []
        try:
            queued = self._cleanup_references()
            self._expire_outgoing_locked()
            live = self._live_references()
            retained = [reference for reference in queued if reference in live and reference.startswith("outgoing-")]
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
                self._atomic_json(self.cleanup_path, {"schema_version": 1, "references": retained + pending}, MAX_SETTINGS_BYTES)
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
        """Check whether protected state has been durably marked for removal."""
        self.vault._key(reference)
        return reference in self._cleanup_references()

    def retire_protected(self, references) -> dict:
        """Durably consume protected state before reporting cancellation or completion."""
        with self.locked():
            self._queue_cleanup_locked(references)
            return self._retry_cleanup_locked()

    def cleanup_protected(self, references) -> dict:
        """Queue consumed protected values and report cleanup separately from committed work."""
        references = list(references)
        try:
            return self.retire_protected(references)
        except Exception as error:
            status = self.cleanup_status()
            return self._cleanup_result(max(len(references), status["cleanup_pending_count"]), self._cleanup_error(error))

    def save(self, value: dict, passwords: dict, expected_revision: int | None) -> dict:
        """Save explicit account settings and newly protected credentials atomically."""
        account = account_input(value)
        if expected_revision is not None and (type(expected_revision) is not int or expected_revision < 1):
            raise MailError("invalid_configuration", "An existing account revision must be a positive integer.")
        if not isinstance(passwords, dict) or set(passwords) != {"incoming", "smtp"} or any(not isinstance(p, str) or len(p) > 4096 or "\0" in p or "\r" in p or "\n" in p for p in passwords.values()):
            raise MailError("invalid_credentials", "Enter valid incoming and SMTP passwords in the local setup app.")
        oauth = account["authentication"]["method"] == "microsoft_oauth"
        if oauth and any(passwords.values()):
            raise MailError("invalid_credentials", "Microsoft sign-in does not accept account passwords.")
        with self.locked():
            accounts = self.accounts()
            previous = next((item for item in accounts if item["id"] == account["id"]), None)
            if (account["id"] and previous is None) or (previous and previous["revision"] != expected_revision):
                raise MailError("configuration_changed", "The account changed in another window. Reload it before saving.")
            if not previous and expected_revision is not None:
                raise MailError("configuration_changed", "A new account cannot have an existing revision.")
            if not previous and len(accounts) >= 50:
                raise MailError("account_limit", "This profile supports at most 50 configured accounts.")
            account["id"] = previous["id"] if previous else uuid.uuid4().hex
            account["revision"] = previous["revision"] + 1 if previous else 1
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
                        raise MailError("password_required", f"Enter the {protocol.upper()} password for the new server or username.")
                    if previous and previous[field] and previous[field] != account[field]:
                        obsolete.append(previous[field])
                account["oauth_secret"] = None
                if oauth and previous and all(previous[key] == account[key] for key in ("authentication", "incoming", "smtp", "email")) and (not account["send_enabled"] or previous["send_enabled"]):
                    account["oauth_secret"] = previous["oauth_secret"]
                if previous and previous["oauth_secret"] and previous["oauth_secret"] != account["oauth_secret"]:
                    obsolete.append(previous["oauth_secret"])
                self._queue_cleanup_locked(obsolete)
                self._write([item for item in accounts if item["id"] != account["id"]] + [account])
            except Exception:
                cleanup = self._retry_cleanup_locked()
                if cleanup["cleanup_pending"]:
                    raise MailError("save_failed_cleanup_pending", "Account changes were not committed. Protected-data cleanup remains pending; retry cleanup locally.") from None
                raise
            cleanup = self._retry_cleanup_locked()
        return {**self.public(account), **cleanup}

    def connect_oauth(self, identity: str, expected_revision: int, refresh_token: str, *, consumed_references=()) -> dict:
        """Attach a completed Microsoft sign-in only to its unchanged account."""
        with self.locked():
            accounts = self.accounts()
            account = next((item for item in accounts if item["id"] == identity), None)
            if account is None or account["revision"] != expected_revision or account["authentication"]["method"] != "microsoft_oauth":
                raise MailError("configuration_changed", "Account settings changed. Start Microsoft sign-in again.")
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
                    raise MailError("signin_failed_cleanup_pending", "Microsoft sign-in was not saved. Protected-data cleanup remains pending; retry cleanup locally.") from None
                raise
            cleanup = self._retry_cleanup_locked()
            return {**self.public(account), **cleanup}

    def remove(self, identity: str) -> dict:
        """Revoke local access before removing its protected credentials."""
        with self.locked():
            account = self.account(identity)
            self._queue_cleanup_locked([account[field] for field in ("incoming_secret", "smtp_secret", "oauth_secret") if account[field]])
            self._write([item for item in self.accounts() if item["id"] != identity])
            cleanup = self._retry_cleanup_locked()
            return {"removed": True, **cleanup}

    @staticmethod
    def public(account: dict) -> dict:
        """Return settings without credential references or values."""
        return {**{key: value for key, value in account.items() if not key.endswith("_secret")},
                "microsoft_connected": bool(account.get("oauth_secret"))}


class Outbox:
    """Persist outgoing states, with message content kept in protected storage."""

    def __init__(self, store: Store):
        self.store = store
        self.path = store.root / "outgoing.sqlite3"

    @contextlib.contextmanager
    def submission_lease(self, identity: str):
        """Hold an OS lock across submission, released automatically if its process dies."""
        self.store.vault._key("outgoing-" + identity)
        directory = self.store.root / "submission-locks"
        directory.mkdir(mode=0o700, parents=True, exist_ok=True)
        with (directory / (identity + ".lock")).open("a+b") as stream:
            stream.seek(0, os.SEEK_END)
            if stream.tell() == 0:
                stream.write(b"\0")
                stream.flush()
            stream.seek(0)
            acquired = False
            try:
                if os.name == "nt":
                    import msvcrt
                    msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                acquired = True
            except OSError:
                pass
            try:
                yield acquired
            finally:
                if acquired:
                    stream.seek(0)
                    if os.name == "nt":
                        msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
                    else:
                        fcntl.flock(stream.fileno(), fcntl.LOCK_UN)

    @contextlib.contextmanager
    def database(self):
        """Open the current ledger and reject unknown schemas."""
        self.store.root.mkdir(mode=0o700, parents=True, exist_ok=True)
        connection = sqlite3.connect(self.path, timeout=3)
        connection.row_factory = sqlite3.Row
        try:
            version = connection.execute("PRAGMA user_version").fetchone()[0]
            if version == 0:
                if connection.execute("SELECT 1 FROM sqlite_master WHERE type='table' LIMIT 1").fetchone():
                    raise MailError("invalid_profile", "The outgoing ledger is not an empty current-format database. Use a new profile.")
                connection.execute("CREATE TABLE IF NOT EXISTS outgoing (id TEXT PRIMARY KEY, account_id TEXT NOT NULL, revision INTEGER NOT NULL, digest TEXT NOT NULL, created REAL NOT NULL, expires REAL NOT NULL, status TEXT NOT NULL, error TEXT)")
                connection.execute("PRAGMA user_version=3")
                connection.commit()
            elif version != 3:
                raise MailError("invalid_profile", "The outgoing ledger uses an unsupported schema.")
            yield connection
        finally:
            connection.close()

    def add(self, identity: str, account: dict, digest: str, content: dict) -> dict:
        """Create one protected request with a ten-minute review deadline."""
        now = time.time()
        with self.store.locked():
            self.store._queue_cleanup_locked(["outgoing-" + identity])
            try:
                self.store.vault.put("outgoing-" + identity, json.dumps(content, ensure_ascii=False))
                with self.database() as database:
                    database.execute("BEGIN IMMEDIATE")
                    count = database.execute("SELECT COUNT(*) FROM outgoing WHERE status='pending_approval' AND expires>?", (now,)).fetchone()[0]
                    if count >= 20:
                        raise MailError("pending_limit", "Review or cancel existing requests before preparing more. The limit is twenty pending messages.")
                    database.execute("INSERT INTO outgoing VALUES (?, ?, ?, ?, ?, ?, 'pending_approval', NULL)",
                                     (identity, account["id"], account["revision"], digest, now, now + 600))
                    database.commit()
            except Exception:
                cleanup = self.store._retry_cleanup_locked()
                if cleanup["cleanup_pending"]:
                    raise MailError("prepare_failed_cleanup_pending", "The outgoing request was not saved. Protected-data cleanup remains pending; retry cleanup locally.") from None
                raise
        return self.get(identity)

    def get(self, identity: str, *, cleanup: bool = True) -> dict:
        """Read one state, expiring pending requests before returning them."""
        self.store.vault._key("outgoing-" + identity)
        if not self.path.exists():
            raise MailError("request_not_found", "The outgoing request does not exist.")
        with self.database() as database:
            row = database.execute("SELECT * FROM outgoing WHERE id=?", (identity,)).fetchone()
        if row is None:
            raise MailError("request_not_found", "The outgoing request does not exist.")
        result = dict(row)
        if result["status"] == "pending_approval" and result["expires"] <= time.time():
            with self.database() as database:
                database.execute("UPDATE outgoing SET status='expired' WHERE id=? AND status='pending_approval' AND expires<=?", (identity, time.time()))
                database.commit()
                result = dict(database.execute("SELECT * FROM outgoing WHERE id=?", (identity,)).fetchone())
        if result["status"] == "submitting":
            with self.submission_lease(identity) as available:
                if available:
                    with self.database() as database:
                        database.execute("UPDATE outgoing SET status='outcome_unknown', error='submission_interrupted' WHERE id=? AND status='submitting'", (identity,))
                        database.commit()
                        result = dict(database.execute("SELECT * FROM outgoing WHERE id=?", (identity,)).fetchone())
        if cleanup and result["status"] in {"cancelled", "expired", "accepted_by_server", "rejected", "outcome_unknown"}:
            result.update(self.store.cleanup_protected(["outgoing-" + identity]))
        else:
            result.update(self.store.cleanup_status())
        return result

    def pending(self) -> list[dict]:
        """List the newest hundred states and attempt one bounded cleanup batch."""
        if not self.path.exists():
            return []
        with self.database() as database:
            identities = [row[0] for row in database.execute("SELECT id FROM outgoing ORDER BY created DESC LIMIT 100")]
        records = [self.get(identity, cleanup=False) for identity in identities]
        cleanup = self.store.retry_cleanup()
        return [{**record, **cleanup} for record in records]

    def claim(self, identity: str) -> None:
        """Consume a pending request once, immediately before submission."""
        with self.database() as database:
            cursor = database.execute("UPDATE outgoing SET status='submitting' WHERE id=? AND status='pending_approval' AND expires>?", (identity, time.time()))
            database.commit()
            if cursor.rowcount != 1:
                raise MailError("approval_unavailable", "This message expired or has already been handled. Prepare a new message.")

    def finish(self, identity: str, status: str, error: str | None = None) -> dict:
        """Record the observed outcome and remove protected pending content."""
        # add() journals this reference before writing MIME; never let cleanup prevent
        # recording a server outcome, even when the settings lock is temporarily busy.
        with self.database() as database:
            database.execute("UPDATE outgoing SET status=?, error=? WHERE id=? AND status='submitting'", (status, error, identity))
            database.commit()
            row = database.execute("SELECT * FROM outgoing WHERE id=?", (identity,)).fetchone()
        if row is None:
            raise MailError("request_not_found", "The outgoing request does not exist.")
        result = dict(row)
        result.update(self.store.cleanup_protected(["outgoing-" + identity]))
        return result

    def cancel(self, identity: str) -> dict:
        """Cancel only an unclaimed request; an in-flight submission cannot be recalled."""
        with self.database() as database:
            cursor = database.execute("UPDATE outgoing SET status='cancelled' WHERE id=? AND status='pending_approval'", (identity,))
            database.commit()
            if cursor.rowcount != 1:
                raise MailError("approval_unavailable", "This request is already being handled. Refresh its status; an in-flight submission cannot be cancelled.")
        return self.get(identity)
