"""Shared application behavior for the MCP and local setup adapters."""

import datetime as dt
import email.policy
import email.utils
import mimetypes
import secrets
import sys
import time
from email.message import EmailMessage
from pathlib import Path

from . import mail, pop
from .config import MAX_ATTACHMENT_BYTES, MAX_BODY_CHARS, MAX_RECIPIENTS, PRESETS, SCHEMA_VERSION, account_input, address, flag, text
from .errors import MailError
from .storage import Store
from .sending import SendLedger, request_identity
from .microsoft import MicrosoftAuth


class MailApplication:
    """Apply account policy consistently, including when references are reused."""

    def __init__(self, root: Path):
        self.store = Store(root)
        self.sends = SendLedger(root)
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
            raise MailError("sending_disabled", "Enable SMTP for this account in Windows setup before sending mail.")
        if revision is not None and account["revision"] != revision:
            raise MailError("configuration_changed", "The account settings changed before submission. Check the account before sending again.")
        return account

    def list_accounts(self) -> dict:
        """List only accounts enabled for assistant reading or sending."""
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

    @staticmethod
    def _compose_message(account: dict, to: list[str], subject: str, body: str,
                         cc: list[str] | None, bcc: list[str] | None,
                         attachments: list[str] | None) -> tuple[list[str], bytes]:
        """Validate recipients and snapshot bounded attachment bytes into an in-memory MIME message."""
        groups = []
        for group in (to, [] if cc is None else cc, [] if bcc is None else bcc):
            if not isinstance(group, list) or len(group) > MAX_RECIPIENTS:
                raise MailError("invalid_recipients", "Supply a list of at most 25 explicit recipient addresses.")
            groups.append([address(value) for value in group])
        recipients = list(dict.fromkeys(item for group in groups for item in group))
        if not recipients or len(recipients) > MAX_RECIPIENTS:
            raise MailError("invalid_recipients", "Choose between one and 25 recipients.")
        subject = text(subject, "Subject", 300)
        if not isinstance(body, str) or not body.strip() or len(body) > MAX_BODY_CHARS or "\0" in body:
            raise MailError("invalid_body", "Enter a nonempty message of at most 100,000 characters.")
        paths = [] if attachments is None else attachments
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
        total = 0
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
        return recipients, message.as_bytes()

    def send_email(self, request_id: str, account_id: str, to: list[str], subject: str, body: str,
                   cc: list[str] | None = None, bcc: list[str] | None = None,
                   attachments: list[str] | None = None) -> dict:
        """Send the assistant's explicit request once, with no separate desktop confirmation."""
        identity = request_identity(request_id)
        account = self._send_account(account_id)
        recipients, content = self._compose_message(account, to, subject, body, cc, bcc, attachments)
        credentials = self._credentials(account, "smtp")
        self.sends.begin(identity, account_id)
        error_code = None
        try:
            access_token = credentials.get("access_token")
            outcome = mail.submit(account["smtp"], credentials["password"], account["email"], recipients, content,
                                  lambda: self._send_account(account_id, account["revision"]), access_token=access_token)
        except MailError as error:
            outcome, error_code = "rejected", error.code
        except Exception:
            outcome, error_code = "outcome_unknown", "submission_interrupted"
        try:
            record = self.sends.finish(identity, outcome, error_code)
            return self._send_result(record)
        except Exception:
            # A local disk failure must not erase an already observed SMTP outcome.
            return {"request_id": identity, "status": outcome, "error": error_code,
                    "delivery_confirmed": False, "status_recorded": False,
                    "record_error": "The send result could not be saved. Do not resend automatically."}

    @staticmethod
    def _send_result(record: dict) -> dict:
        result = {"request_id": record["id"], "status": record["status"], "error": record["error"],
                  "delivery_confirmed": False, "status_recorded": True}
        if record["status"] == "submitting":
            result["notice"] = "No final SMTP result has been recorded. The operation may still be running or may have stopped. Do not resend automatically."
        return result

    def get_send_status(self, request_id: str) -> dict:
        """Report server acceptance separately from end-to-end delivery."""
        return self._send_result(self.sends.get(request_id))

    def _check_credentials(self, value, passwords, expected_revision, protocols):
        account = account_input(value)
        if not isinstance(passwords, dict) or set(passwords) != {"incoming", "smtp"} or any(
            not isinstance(secret, str) or len(secret) > 4096 or any(c in secret for c in "\0\r\n")
            for secret in passwords.values()
        ):
            raise MailError("invalid_credentials", "Check the passwords for reading and sending in AgentInbox.")
        if expected_revision is not None and (type(expected_revision) is not int or expected_revision < 1):
            raise MailError("invalid_configuration", "An existing account revision must be a positive integer.")
        previous = self.store.account(account["id"]) if account["id"] else None
        if (previous and previous["revision"] != expected_revision) or (previous is None and expected_revision is not None):
            raise MailError("configuration_changed", "The account changed in another window. Reload it before checking.")
        credentials = {}
        if account["authentication"]["method"] == "microsoft_oauth":
            if any(passwords.values()):
                raise MailError("invalid_credentials", "Microsoft sign-in does not need a password in this app.")
            if previous is None or not previous["oauth_secret"] or any(
                previous[key] != account[key] for key in ("authentication", "incoming", "smtp", "email")
            ) or (account["send_enabled"] and not previous["send_enabled"]):
                raise MailError("authorization_required", "Save changes, then press Sign in with Microsoft before checking this account.")
            token = self.microsoft.access_token(previous)
            credentials = {protocol: {"password": "", "access_token": token} for protocol in ("incoming", "smtp")}
        else:
            for protocol in protocols:
                if account[protocol] is None:
                    continue
                secret = passwords[protocol]
                if not secret and previous and previous["authentication"] == account["authentication"] and previous[protocol] == account[protocol]:
                    secret = self.store.vault.get(previous[protocol + "_secret"])
                if not secret:
                    purpose = "reading" if protocol == "incoming" else "sending"
                    section = "Read mail" if protocol == "incoming" else "Send mail"
                    raise MailError("password_required", f"Enter the password for {purpose} under {section}. A new server or email address needs its password entered again.")
                credentials[protocol] = {"password": secret}
        return account, credentials

    def check_smtp(self, value: dict, passwords: dict, expected_revision: int | None, progress=None) -> dict:
        """Check only current-form SMTP authentication without saving, reading or enabling sending."""
        if progress:
            progress("settings", "started")
        account, credentials = self._check_credentials(value, passwords, expected_revision, ("smtp",))
        if account["smtp"] is None:
            raise MailError("smtp_not_configured", "Configure an SMTP server before checking its connection.")
        if progress:
            progress("settings", "passed")
        with mail.smtp_connection(account["smtp"], **credentials["smtp"], progress=progress):
            return {"connection": "passed", "authentication": "passed", "saved": False, "sent": False}

    def check_account(self, value: dict, passwords: dict, expected_revision: int | None, progress=None, *, read_message=False) -> dict:
        """Check current settings, discover folders and optionally test one read without saving or sending."""
        if progress:
            progress("settings", "started")
        read_message = flag(read_message, "Reading a sample")
        account, credentials = self._check_credentials(value, passwords, expected_revision, ("incoming", "smtp"))
        if progress:
            progress("settings", "passed")
        connect = pop.pop_connection if account["incoming"]["protocol"] == "pop3" else mail.imap_connection
        with connect(account["incoming"], **credentials["incoming"], progress=progress) as connection:
            deadline = time.monotonic() + mail.TIMEOUT
            if progress:
                progress("folders", "started")
            available = ["INBOX"] if account["incoming"]["protocol"] == "pop3" else mail.folders(connection, deadline=deadline)
            if not available:
                raise MailError("no_folders", "The server returned no selectable folders. No account was saved.")
            if progress:
                progress("folders", "passed")
            sample = "not_requested"
            if read_message:
                if progress:
                    progress("reading", "started")
                if any(folder not in available for folder in account["folders"]):
                    raise MailError("unsupported_folder", "Choose available server folders before testing a message read.")
                sample = self._incoming(account).check_connection_read(connection, account["folders"][0], deadline=deadline)["message_read"]
                if progress:
                    progress("reading", "passed" if sample == "passed" else "empty")
        if account["smtp"] is not None:
            if progress:
                progress("smtp", "started")
            with mail.smtp_connection(account["smtp"], **credentials["smtp"]):
                if progress:
                    progress("smtp", "passed")
        return {"connection": "passed", "authentication": "passed", "folders": available,
                "protocol": account["incoming"]["protocol"], "smtp": "passed" if account["smtp"] else "not_configured",
                "saved": False, "message_read": sample, "sent": False}

    def manage(self, operation: str, arguments: dict, *, progress=None, diagnostic=None):
        """Dispatch local setup operations through a closed allowlist."""
        if not isinstance(arguments, dict):
            raise MailError("invalid_arguments", "Arguments must be an object.")
        if operation == "presets" and not arguments:
            return PRESETS
        if operation == "profile_info" and not arguments:
            protection = "Windows Credential Manager" if sys.platform == "win32" else "macOS Keychain" if sys.platform == "darwin" else "Linux Secret Service"
            return {"schema_version": SCHEMA_VERSION, "data_directory": str(self.store.root),
                    "settings_file": str(self.store.settings), "outgoing_ledger": str(self.sends.path),
                    "cleanup_queue_file": str(self.store.cleanup_path),
                    "temporary_protected_directory": str(self.store.root / "protected"),
                    "credential_store": protection,
                    "temporary_store": "current-user DPAPI" if sys.platform == "win32" else protection,
                    **self.store.cleanup_status()}
        if operation == "retry_cleanup" and not arguments:
            return self.store.retry_cleanup()
        if operation == "list_accounts" and not arguments:
            return [self.store.public(item) for item in self.store.accounts()]
        if operation == "save_account" and set(arguments) == {"account", "passwords", "expected_revision"}:
            return self.store.save(arguments["account"], arguments["passwords"], arguments["expected_revision"])
        if operation == "check_account" and set(arguments) in ({"account", "passwords", "expected_revision"}, {"account", "passwords", "expected_revision", "read_message"}):
            return self.check_account(arguments["account"], arguments["passwords"], arguments["expected_revision"], progress,
                                      read_message=arguments.get("read_message", False))
        if operation == "check_smtp" and set(arguments) == {"account", "passwords", "expected_revision"}:
            return self.check_smtp(arguments["account"], arguments["passwords"], arguments["expected_revision"], progress)
        if operation == "search_mail":
            return self.search_mail(**arguments)
        if operation == "read_mail" and set(arguments) == {"reference"}:
            return self.read_mail(**arguments)
        if operation == "get_send_status" and set(arguments) == {"request_id"}:
            return self.get_send_status(**arguments)
        if operation == "poll_microsoft_signin" and set(arguments) == {"flow_id"}:
            return self.microsoft.poll(arguments["flow_id"], diagnostic=diagnostic)
        if operation == "cancel_microsoft_signin" and set(arguments) == {"flow_id"}:
            return self.microsoft.cancel(arguments["flow_id"])
        if operation in {"start_microsoft_signin", "remove_account", "check_read", "list_folders"} and set(arguments) == {"account_id"}:
            account = self.store.account(arguments["account_id"])
            if operation == "start_microsoft_signin":
                return self.microsoft.start(account["id"], diagnostic=diagnostic)
            if operation == "remove_account":
                return self.store.remove(account["id"])
            if operation == "check_read":
                return self._incoming(account).check_read(account["incoming"], folder=account["folders"][0], **self._credentials(account, "incoming"))
            if operation == "list_folders":
                if account["incoming"]["protocol"] == "pop3":
                    return ["INBOX"]
                with self._incoming_connection(account) as connection:
                    return mail.folders(connection)
        raise MailError("invalid_operation", "Unsupported local operation or arguments.")
