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


class Store:
    """Own settings writes across the desktop, CLI and MCP processes."""

    def __init__(self, root: Path):
        self.root = root
        self.settings = root / "accounts.json"
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
            if self.settings.stat().st_size > 256_000:
                raise ValueError()
            document = json.loads(self.settings.read_text(encoding="utf-8"))
            if set(document) != {"schema_version", "accounts"} or document["schema_version"] != SCHEMA_VERSION:
                raise ValueError()
            accounts = document["accounts"]
            if not isinstance(accounts, list) or len(accounts) > 50:
                raise ValueError()
            identities = set()
            for saved in accounts:
                base = {key: value for key, value in saved.items() if key not in {"revision", "imap_secret", "smtp_secret"}}
                validated = account_input(base)
                if not validated["id"] or validated != base or saved["id"] in identities:
                    raise ValueError()
                if type(saved["revision"]) is not int or saved["revision"] < 1:
                    raise ValueError()
                self.vault._key(saved["imap_secret"])
                if saved["smtp"] is not None:
                    self.vault._key(saved["smtp_secret"])
                elif saved["smtp_secret"] is not None:
                    raise ValueError()
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

    def _write(self, accounts: list[dict]) -> None:
        temporary = self.root / ("settings-" + uuid.uuid4().hex + ".tmp")
        try:
            descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
                json.dump({"schema_version": SCHEMA_VERSION, "accounts": accounts}, stream, ensure_ascii=False, indent=2)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, self.settings)
        finally:
            temporary.unlink(missing_ok=True)

    def save(self, value: dict, passwords: dict, expected_revision: int | None) -> dict:
        """Save explicit account settings and newly protected credentials atomically."""
        account = account_input(value)
        if set(passwords) != {"imap", "smtp"} or any(not isinstance(p, str) or len(p) > 4096 or "\0" in p or "\r" in p or "\n" in p for p in passwords.values()):
            raise MailError("invalid_credentials", "Enter valid IMAP and SMTP passwords in the local setup app.")
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
            created = []
            obsolete = []
            try:
                for protocol in ("imap", "smtp"):
                    field = protocol + "_secret"
                    if account[protocol] is None:
                        account[field] = None
                    elif passwords[protocol]:
                        reference = protocol + "-" + uuid.uuid4().hex
                        self.vault.put(reference, passwords[protocol])
                        created.append(reference)
                        account[field] = reference
                    elif previous and previous[protocol] == account[protocol]:
                        account[field] = previous[field]
                    else:
                        raise MailError("password_required", f"Enter the {protocol.upper()} password for the new server or username.")
                    if previous and previous[field] and previous[field] != account[field]:
                        obsolete.append(previous[field])
                self._write([item for item in accounts if item["id"] != account["id"]] + [account])
            except Exception:
                for reference in created:
                    self.vault.delete(reference)
                raise
            for reference in obsolete:
                self.vault.delete(reference)
        return self.public(account)

    def remove(self, identity: str) -> None:
        """Revoke local access before removing its protected credentials."""
        with self.locked():
            account = self.account(identity)
            self._write([item for item in self.accounts() if item["id"] != identity])
            self.vault.delete(account["imap_secret"])
            if account["smtp_secret"]:
                self.vault.delete(account["smtp_secret"])

    @staticmethod
    def public(account: dict) -> dict:
        """Return settings without credential references or values."""
        return {key: value for key, value in account.items() if not key.endswith("_secret")}


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
                connection.execute("PRAGMA user_version=2")
                connection.commit()
            elif version != 2:
                raise MailError("invalid_profile", "The outgoing ledger uses an unsupported schema.")
            yield connection
        finally:
            connection.close()

    def add(self, identity: str, account: dict, digest: str, content: dict) -> dict:
        """Create one protected request with a ten-minute review deadline."""
        self.store.vault.put("outgoing-" + identity, json.dumps(content, ensure_ascii=False))
        now = time.time()
        try:
            with self.database() as database:
                database.execute("BEGIN IMMEDIATE")
                count = database.execute("SELECT COUNT(*) FROM outgoing WHERE status='pending_approval' AND expires>?", (now,)).fetchone()[0]
                if count >= 20:
                    raise MailError("pending_limit", "Review or cancel existing requests before preparing more. The limit is twenty pending messages.")
                database.execute("INSERT INTO outgoing VALUES (?, ?, ?, ?, ?, ?, 'pending_approval', NULL)",
                                 (identity, account["id"], account["revision"], digest, now, now + 600))
                database.commit()
        except Exception:
            self.store.vault.delete("outgoing-" + identity)
            raise
        return self.get(identity)

    def get(self, identity: str) -> dict:
        """Read one state, expiring pending requests before returning them."""
        self.store.vault._key("outgoing-" + identity)
        if not self.path.exists():
            raise MailError("request_not_found", "The outgoing request does not exist.")
        with self.database() as database:
            database.execute("UPDATE outgoing SET status='expired' WHERE id=? AND status='pending_approval' AND expires<=?", (identity, time.time()))
            database.commit()
            row = database.execute("SELECT * FROM outgoing WHERE id=?", (identity,)).fetchone()
        if row is None:
            raise MailError("request_not_found", "The outgoing request does not exist.")
        result = dict(row)
        if result["status"] == "submitting":
            with self.submission_lease(identity) as available:
                if available:
                    with self.database() as database:
                        database.execute("UPDATE outgoing SET status='outcome_unknown', error='submission_interrupted' WHERE id=? AND status='submitting'", (identity,))
                        database.commit()
                        result = dict(database.execute("SELECT * FROM outgoing WHERE id=?", (identity,)).fetchone())
        if result["status"] in {"cancelled", "expired", "accepted_by_server", "rejected", "outcome_unknown"}:
            self.store.vault.delete("outgoing-" + identity)
        return result

    def pending(self) -> list[dict]:
        """List the newest hundred states without exposing message content."""
        if not self.path.exists():
            return []
        with self.database() as database:
            identities = [row[0] for row in database.execute("SELECT id FROM outgoing ORDER BY created DESC LIMIT 100")]
        return [self.get(identity) for identity in identities]

    def claim(self, identity: str) -> None:
        """Consume a pending request once, immediately before submission."""
        with self.database() as database:
            cursor = database.execute("UPDATE outgoing SET status='submitting' WHERE id=? AND status='pending_approval' AND expires>?", (identity, time.time()))
            database.commit()
            if cursor.rowcount != 1:
                raise MailError("approval_unavailable", "This message expired or has already been handled. Prepare a new message.")

    def finish(self, identity: str, status: str, error: str | None = None) -> dict:
        """Record the observed outcome and remove protected pending content."""
        with self.database() as database:
            database.execute("UPDATE outgoing SET status=?, error=? WHERE id=? AND status='submitting'", (status, error, identity))
            database.commit()
        return self.get(identity)

    def cancel(self, identity: str) -> dict:
        """Cancel only an unclaimed request; an in-flight submission cannot be recalled."""
        with self.database() as database:
            cursor = database.execute("UPDATE outgoing SET status='cancelled' WHERE id=? AND status='pending_approval'", (identity,))
            database.commit()
            if cursor.rowcount != 1:
                raise MailError("approval_unavailable", "This request is already being handled. Refresh its status; an in-flight submission cannot be cancelled.")
        return self.get(identity)
