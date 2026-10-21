"""Record SMTP outcomes without a message queue, stored bodies or automatic retries."""

import contextlib
import re
import sqlite3
import time
from pathlib import Path

from .errors import MailError

LEDGER_VERSION = 4
FINAL_STATES = {"accepted_by_server", "rejected", "outcome_unknown"}


def request_identity(value: str) -> str:
    """Require a caller-owned ID that survives a lost tool response."""
    if not isinstance(value, str) or re.fullmatch(r"[a-f0-9]{32}", value) is None:
        raise MailError("invalid_request_id", "Use a unique request_id of 32 lowercase hexadecimal characters for each email.")
    return value


class SendLedger:
    """Claim each request once and retain only its submission metadata."""

    def __init__(self, root: Path):
        self.root = root
        self.path = root / "outgoing.sqlite3"

    @contextlib.contextmanager
    def database(self):
        """Open the current schema; reject older formats without converting them."""
        self.root.mkdir(mode=0o700, parents=True, exist_ok=True)
        database = sqlite3.connect(self.path, timeout=3)
        database.row_factory = sqlite3.Row
        try:
            version = database.execute("PRAGMA user_version").fetchone()[0]
            if version == 0:
                database.execute("BEGIN IMMEDIATE")
                # Recheck after the lock: another process may have initialized it.
                version = database.execute("PRAGMA user_version").fetchone()[0]
                if version == 0:
                    existing = database.execute("SELECT 1 FROM sqlite_master WHERE type='table' LIMIT 1").fetchone()
                    if existing:
                        raise MailError("invalid_profile", "The send ledger has an unsupported format. Use a new profile.")
                    database.execute(
                        "CREATE TABLE outgoing (id TEXT PRIMARY KEY, account_id TEXT NOT NULL, "
                        "created REAL NOT NULL, status TEXT NOT NULL, error TEXT)"
                    )
                    database.execute(f"PRAGMA user_version={LEDGER_VERSION}")
                    version = LEDGER_VERSION
                database.commit()
            if version != LEDGER_VERSION:
                raise MailError("invalid_profile", "The send ledger has an unsupported version. No conversion is available; use a new profile.")
            yield database
        finally:
            database.close()

    def begin(self, identity: str, account_id: str) -> None:
        """Claim the ID atomically before SMTP; even concurrent duplicates cannot send."""
        request_identity(identity)
        with self.database() as database:
            try:
                database.execute("INSERT INTO outgoing VALUES (?, ?, ?, 'submitting', NULL)",
                                 (identity, account_id, time.time()))
                database.commit()
            except sqlite3.IntegrityError as error:
                raise MailError("request_exists", "This request_id was already used. Check get_send_status; do not resend automatically.") from error

    def get(self, identity: str) -> dict:
        """Read metadata; submitting means no final result was recorded, not proof of activity."""
        request_identity(identity)
        if not self.path.exists():
            raise MailError("request_not_found", "This send request does not exist.")
        with self.database() as database:
            row = database.execute("SELECT * FROM outgoing WHERE id=?", (identity,)).fetchone()
        if row is None:
            raise MailError("request_not_found", "This send request does not exist.")
        if row["status"] not in FINAL_STATES | {"submitting"}:
            raise MailError("invalid_profile", "The send ledger contains an unsupported state.")
        return dict(row)

    def finish(self, identity: str, status: str, error: str | None = None) -> dict:
        """Save the terminal result without overwriting an earlier terminal outcome."""
        if status not in FINAL_STATES:
            raise MailError("invalid_send_status", "The SMTP operation returned an unsupported result.")
        with self.database() as database:
            database.execute("UPDATE outgoing SET status=?, error=? WHERE id=? AND status='submitting'",
                             (status, error, identity))
            database.commit()
        return self.get(identity)
