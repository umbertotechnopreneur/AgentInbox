"""Shared application behavior for the MCP and local setup adapters."""

import base64
import datetime as dt
import email.policy
import email.utils
import hashlib
import json
import mimetypes
import os
import secrets
import subprocess
import time
import uuid
from email.message import EmailMessage
from pathlib import Path

from . import mail, pop
from .config import MAX_ATTACHMENT_BYTES, MAX_BODY_CHARS, MAX_RECIPIENTS, PRESETS, address, flag, text
from .errors import MailError
from .storage import Outbox, Store
from .microsoft import MicrosoftAuth


class MailApplication:
    """Apply account policy consistently, including when references are reused."""

    def __init__(self, root: Path):
        self.store = Store(root)
        self.outbox = Outbox(self.store)
        self.microsoft = MicrosoftAuth(self.store)
        self.references = {}
        self.continuations = {}

    def _credentials(self, account: dict, protocol: str) -> dict:
        if account["authentication"]["method"] == "microsoft_oauth":
            return {"password": "", "access_token": self.microsoft.access_token(account)}
        return {"password": self.store.vault.get(account[protocol + "_secret"])}

    @staticmethod
    def _incoming(account: dict):
        return pop if account["incoming"]["protocol"] == "pop3" else mail

    def _incoming_connection(self, account: dict):
        module = self._incoming(account)
        connect = pop.pop_connection if module is pop else mail.imap_connection
        return connect(account["incoming"], **self._credentials(account, "incoming"))

    def _read_account(self, identity: str, folder: str) -> dict:
        account = self.store.account(identity)
        if not account["read_enabled"] or folder not in account["folders"]:
            raise MailError("access_disabled", "Reading this account or folder is not enabled in local setup.")
        return account

    def _send_account(self, identity: str, revision: int | None = None) -> dict:
        account = self.store.account(identity)
        if not account["send_enabled"] or account["smtp"] is None:
            raise MailError("sending_disabled", "Enable SMTP for this account in local setup before preparing mail.")
        if revision is not None and account["revision"] != revision:
            raise MailError("configuration_changed", "The account settings changed. Prepare a new message and review it again.")
        return account

    def list_accounts(self) -> dict:
        """List only accounts enabled for assistant reading or approved sending."""
        return {"accounts": [{"id": item["id"], "label": item["label"], "email": item["email"],
                              "read_enabled": item["read_enabled"], "send_enabled": item["send_enabled"],
                              "incoming_protocol": item["incoming"]["protocol"],
                              "folders": item["folders"] if item["read_enabled"] else []}
                             for item in self.store.accounts() if item["read_enabled"] or item["send_enabled"]]}

    @staticmethod
    def _date(value: str) -> dt.datetime:
        try:
            result = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
            if result.tzinfo is None:
                raise ValueError()
            return result.astimezone(dt.timezone.utc)
        except (ValueError, AttributeError) as error:
            raise MailError("invalid_date", "Use an ISO 8601 date and time with an explicit timezone.") from error

    def search_mail(self, account_id: str, folder: str = "INBOX", start: str | None = None,
                    end: str | None = None, query: str = "", unread: bool = False,
                    limit: int = 10, continuation: str | None = None) -> dict:
        """Return a bounded page from one explicitly shared account and folder."""
        self._expire_references()
        if continuation:
            saved = self.continuations.get(continuation)
            if saved is None or saved["account_id"] != account_id:
                raise MailError("reference_expired", "The continuation expired or belongs to another account. Search again.")
            if folder != "INBOX" or start is not None or end is not None or query or unread or limit != 10:
                raise MailError("invalid_continuation", "Use only account_id and continuation to resume a search.")
            account = self._read_account(account_id, saved["folder"])
            if account["revision"] != saved["revision"]:
                raise MailError("configuration_changed", "Account settings changed. Start a new search.")
            parameters = saved["parameters"]
            candidates, validity, offset = saved["candidates"], saved["validity"], saved["offset"]
            truncated = saved["truncated"]
            undated = saved["undated"]
        else:
            folder = text(folder, "Folder")
            account = self._read_account(account_id, folder)
            query = text(query, "Search text", 128, allow_empty=True)
            flag(unread, "Unread filter")
            if type(limit) is not int or not 1 <= limit <= 20:
                raise MailError("invalid_limit", "Page size must be between 1 and 20.")
            stop = self._date(end) if end else dt.datetime.now(dt.timezone.utc)
            beginning = self._date(start) if start else stop - dt.timedelta(days=14)
            if not dt.timedelta(0) < stop - beginning <= dt.timedelta(days=31):
                raise MailError("invalid_date_range", "Choose a positive date range of at most 31 days.")
            parameters = {"folder": folder, "start": beginning, "end": stop, "query": query, "unread": unread, "limit": limit}
            candidates, validity, offset, truncated = None, None, 0, False
            undated = 0
        module = self._incoming(account)
        with self._incoming_connection(account) as connection:
            page = module.search(connection, **parameters, candidates=candidates, expected_validity=validity, offset=offset)
        # Do not release results after sharing was changed while the network request ran.
        current = self._read_account(account_id, parameters["folder"])
        if current["revision"] != account["revision"]:
            raise MailError("configuration_changed", "The account changed while reading. Start a new search.")
        results = []
        for item in page["messages"]:
            reference = secrets.token_urlsafe(24)
            self.references[reference] = {"account_id": account_id, "folder": item["folder"],
                                          "uid": item["uid"], "validity": item["uid_validity"],
                                          "revision": account["revision"], "expires": time.time() + 600}
            results.append({"reference": reference, "account_id": account_id,
                            **{key: value for key, value in item.items() if key not in {"uid", "uid_validity"}}})
        next_token = None
        truncated = truncated or page["candidate_limit_reached"]
        undated += page.get("undated_messages_skipped", 0)
        if page["more"]:
            next_token = secrets.token_urlsafe(24)
            self.continuations[next_token] = {"account_id": account_id, "folder": parameters["folder"],
                                             "revision": account["revision"], "parameters": parameters,
                                             "candidates": page["candidates"], "validity": page["uid_validity"],
                                             "offset": page["next_offset"], "truncated": truncated, "undated": undated, "expires": time.time() + 600}
        if continuation:
            self.continuations.pop(continuation, None)
        return {"messages": results, "continuation": next_token,
                "start": parameters["start"].isoformat(), "end": parameters["end"].isoformat(),
                "folder": parameters["folder"], "account_id": account_id,
                "coverage_complete": not page["more"] and not truncated and not undated,
                "date_basis": page.get("date_basis", "server_received_time"),
                "search_scope": page.get("search_scope", "server_text"),
                "undated_messages_skipped": undated, "date_filter_incomplete": bool(undated),
                "candidate_limit_reached": truncated,
                "notice": "Narrow the date range to search beyond the newest 300 candidates." if truncated else None}

    def _expire_references(self):
        now = time.time()
        for cache in (self.references, self.continuations):
            for key in list(cache):
                if cache[key]["expires"] <= now:
                    del cache[key]
            while len(cache) > 1000:
                del cache[next(iter(cache))]

    def read_mail(self, reference: str) -> dict:
        """Read a search result while applying current sharing choices."""
        self._expire_references()
        saved = self.references.get(reference)
        if saved is None:
            raise MailError("reference_expired", "The message reference expired. Search again in this connection.")
        account = self._read_account(saved["account_id"], saved["folder"])
        if account["revision"] != saved["revision"]:
            raise MailError("configuration_changed", "The account changed since this reference was created. Search again.")
        with self._incoming_connection(account) as connection:
            result = self._incoming(account).read(connection, saved["folder"], saved["uid"], saved["validity"])
        if self._read_account(account["id"], saved["folder"])["revision"] != account["revision"]:
            raise MailError("configuration_changed", "The account changed while reading. Search again.")
        result.pop("uid", None)
        return {"account_id": account["id"], **result}

    def prepare_email(self, account_id: str, to: list[str], subject: str, body: str,
                      cc: list[str] | None = None, bcc: list[str] | None = None,
                      attachments: list[str] | None = None) -> dict:
        """Freeze a message locally; preparation never submits it to SMTP."""
        account = self._send_account(account_id)
        if sum(item["status"] == "pending_approval" for item in self.outbox.pending()) >= 20:
            raise MailError("pending_limit", "Review or cancel existing outgoing requests before preparing more. The limit is twenty pending messages.")
        groups = []
        for group in (to, cc or [], bcc or []):
            if not isinstance(group, list) or len(group) > MAX_RECIPIENTS:
                raise MailError("invalid_recipients", "Supply a list of at most 25 explicit recipient addresses.")
            groups.append([address(value) for value in group])
        recipients = list(dict.fromkeys(item for group in groups for item in group))
        if not recipients or len(recipients) > MAX_RECIPIENTS:
            raise MailError("invalid_recipients", "Choose between one and 25 recipients.")
        subject = text(subject, "Subject", 300)
        if not isinstance(body, str) or not body.strip() or len(body) > MAX_BODY_CHARS or "\0" in body:
            raise MailError("invalid_body", "Enter a nonempty message of at most 100,000 characters.")
        paths = attachments or []
        if not isinstance(paths, list) or len(paths) > 10:
            raise MailError("attachment_limit", "Attach at most ten files with a combined size of 5 MiB.")
        message = EmailMessage(policy=email.policy.SMTP)
        message["From"] = account["email"]
        for name, values in zip(("To", "Cc"), groups[:2]):
            if values:
                message[name] = ", ".join(values)
        message["Subject"] = subject
        message["Date"] = email.utils.format_datetime(dt.datetime.now(dt.timezone.utc))
        message["Message-ID"] = email.utils.make_msgid(domain=account["email"].split("@", 1)[1])
        message.set_content(body, cte="quoted-printable")
        included, total = [], 0
        for supplied in paths:
            if not isinstance(supplied, str):
                raise MailError("invalid_attachment", "Attachment paths must be strings.")
            path = Path(supplied).expanduser()
            if not path.is_absolute() or not path.is_file():
                raise MailError("invalid_attachment", "Choose an existing file using its absolute path.")
            with path.open("rb") as stream:
                content = stream.read(MAX_ATTACHMENT_BYTES + 1 - total)
            total += len(content)
            if total > MAX_ATTACHMENT_BYTES:
                raise MailError("attachment_limit", "Attachments exceed the combined 5 MiB limit.")
            name = text(path.name, "Attachment filename", 255)
            content_type = mimetypes.guess_type(name)[0] or "application/octet-stream"
            major, minor = content_type.split("/", 1)
            message.add_attachment(content, maintype=major, subtype=minor, filename=name)
            included.append({"name": name, "size": len(content), "sha256": hashlib.sha256(content).hexdigest()})
        frozen = {"from": account["email"], "to": groups[0], "cc": groups[1], "bcc": groups[2],
                  "subject": subject, "body": body, "attachments": included, "recipients": recipients,
                  "mime": base64.b64encode(message.as_bytes()).decode("ascii")}
        serialized = json.dumps(frozen, sort_keys=True, ensure_ascii=False).encode("utf-8")
        request_id = uuid.uuid4().hex
        self.outbox.add(request_id, account, hashlib.sha256(serialized).hexdigest(), frozen)
        self._open_review(request_id)
        return {"request_id": request_id, "status": "pending_approval", "expires_in_seconds": 600,
                "message": "Open AgentInbox Outgoing to review and approve this exact message. Nothing has been sent."}

    def _open_review(self, request_id: str):
        executable = os.environ.get("AGENTINBOX_REVIEW_APP")
        if executable:
            path = Path(executable)
            if path.is_absolute() and path.is_file() and path.name.lower() == "agentinbox.desktop.exe":
                try:
                    subprocess.Popen([str(path), "--review", request_id], stdin=subprocess.DEVNULL,
                                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                except OSError:
                    # The durable request remains visible for manual review in the app.
                    pass

    def review_snapshot(self, request_id: str) -> tuple[dict, dict]:
        """Load the exact pending snapshot for a private local review session."""
        record = self.outbox.get(request_id)
        if record["status"] != "pending_approval":
            raise MailError("approval_unavailable", "This request is no longer awaiting approval.")
        self._send_account(record["account_id"], record["revision"])
        frozen = json.loads(self.store.vault.get("outgoing-" + request_id))
        actual = hashlib.sha256(json.dumps(frozen, sort_keys=True, ensure_ascii=False).encode("utf-8")).hexdigest()
        if not secrets.compare_digest(actual, record["digest"]):
            raise MailError("message_changed", "The protected message does not match its request. Prepare a new message.")
        return record, frozen

    def send_reviewed(self, record: dict, frozen: dict, approved: bool) -> dict:
        """Complete a private review interaction; this method is never an MCP tool."""
        flag(approved, "Approval")
        identity = record["id"]
        if not approved:
            return self.outbox.cancel(identity)
        account = self._send_account(record["account_id"], record["revision"])
        credentials = self._credentials(account, "smtp")
        # Freeze review content in memory; verify the current record before consuming it.
        current, current_message = self.review_snapshot(identity)
        if current["digest"] != record["digest"] or current_message != frozen:
            raise MailError("message_changed", "The message changed. Review a new request.")
        with self.outbox.submission_lease(identity) as acquired:
            if not acquired:
                raise MailError("approval_unavailable", "Another review is already submitting this message.")
            self.outbox.claim(identity)
            try:
                outcome = mail.submit(account["smtp"], credentials["password"], frozen["from"], frozen["recipients"],
                                      base64.b64decode(frozen["mime"], validate=True),
                                      lambda: self._send_account(account["id"], account["revision"]),
                                      **{key: value for key, value in credentials.items() if key != "password"})
            except MailError as error:
                return self.outbox.finish(identity, "rejected", error.code)
            except Exception:
                return self.outbox.finish(identity, "outcome_unknown", "submission_interrupted")
            return self.outbox.finish(identity, outcome)

    def get_send_status(self, request_id: str) -> dict:
        """Report server acceptance separately from end-to-end delivery."""
        record = self.outbox.get(request_id)
        self._send_account(record["account_id"])
        return {"request_id": record["id"], "status": record["status"], "error": record["error"],
                "delivery_confirmed": False}

    def manage(self, operation: str, arguments: dict):
        """Dispatch local setup operations through a closed allowlist."""
        if not isinstance(arguments, dict):
            raise MailError("invalid_arguments", "Arguments must be an object.")
        if operation == "presets" and not arguments:
            return PRESETS
        if operation == "list_accounts" and not arguments:
            return [self.store.public(item) for item in self.store.accounts()]
        if operation == "save_account" and set(arguments) == {"account", "passwords", "expected_revision"}:
            return self.store.save(arguments["account"], arguments["passwords"], arguments["expected_revision"])
        if operation == "list_pending" and not arguments:
            return self.outbox.pending()
        if operation == "prepare_email":
            return self.prepare_email(**arguments)
        if operation == "get_send_status" and set(arguments) == {"request_id"}:
            return self.get_send_status(**arguments)
        if operation == "poll_microsoft_signin" and set(arguments) == {"flow_id"}:
            return self.microsoft.poll(arguments["flow_id"])
        if operation == "cancel_microsoft_signin" and set(arguments) == {"flow_id"}:
            return self.microsoft.cancel(arguments["flow_id"])
        if set(arguments) == {"account_id"}:
            account = self.store.account(arguments["account_id"])
            if operation == "start_microsoft_signin":
                return self.microsoft.start(account["id"])
            if operation == "remove_account":
                self.store.remove(account["id"])
                return {"removed": True}
            if operation == "check_read":
                return self._incoming(account).check_read(account["incoming"], folder=account["folders"][0], **self._credentials(account, "incoming"))
            if operation == "list_folders":
                if account["incoming"]["protocol"] == "pop3":
                    return ["INBOX"]
                with self._incoming_connection(account) as connection:
                    return mail.folders(connection)
            if operation == "check_smtp":
                self._send_account(account["id"])
                with mail.smtp_connection(account["smtp"], **self._credentials(account, "smtp")):
                    return {"connection": "passed", "authentication": "passed", "sent": False,
                            "message": "SMTP authentication succeeded. No email was sent; delivery has not been tested."}
        raise MailError("invalid_operation", "Unsupported local operation or arguments.")
