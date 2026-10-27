# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Human-facing account setup commands backed by the shared mail application.
# Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
# Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
# Evidence: docs/VALIDATION.md (source review; execution checks pending).
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Human-facing account setup commands backed by the shared mail application."""

import getpass
import json
import sys
import time
import warnings
from pathlib import Path

from .config import account_input
from .errors import MailError


def add_commands(commands):
    """Register discoverable local setup commands without changing the JSON adapter.

    Args:
        commands: argparse subparser collection receiving the local setup commands.
    """
    profile = commands.add_parser("profile", help="Show profile paths or retry pending credential cleanup.")
    actions = profile.add_subparsers(dest="action", required=True)
    for name, description in (("show", "Show where settings and protected credentials are stored."),
                              ("cleanup", "Retry local cleanup of retired credentials; never repeat a save or send.")):
        command = actions.add_parser(name, help=description, description=description)
        command.add_argument("--json", action="store_true", help="Write one JSON result for automation.")
    presets = commands.add_parser("presets", help="Show the supported providers and their connection requirements.")
    presets.add_argument("--json", action="store_true", help="Write one JSON result for automation.")
    accounts = commands.add_parser("accounts", help="Add, inspect, update, sign in and check existing mailboxes.",
        description="Connect existing mailboxes. This does not create Gmail or Microsoft accounts at the provider.",
        epilog="Start with accounts add --help. Setup files contain settings only; passwords are requested with hidden input.")
    actions = accounts.add_subparsers(dest="action", required=True)
    descriptions = {
        "list": "List every saved connection, including disabled accounts.",
        "show": "Show one account's settings, revision and Microsoft sign-in status.",
        "add": "Create one local connection from a named entry in a setup JSON file.",
        "update": "Save replacement settings for an account at an explicit revision.",
        "signin": "Sign in with Microsoft in your browser and wait for the result.",
        "folders": "Connect to the provider and list exact selectable folder names.",
        "check": "Check saved mail settings without sending; reading a sample is optional.",
        "check-smtp": "Check the saved SMTP connection without sending a message.",
        "remove": "Remove a local connection and its credentials; keep the provider mailbox.",
    }
    for name, description in descriptions.items():
        command = actions.add_parser(name, help=description, description=description)
        command.add_argument("--json", action="store_true", help="Write one JSON result; prompts and progress use stderr.")
        if name not in {"list", "add"}:
            command.add_argument("account_id", help="The ID printed by accounts add or accounts list.")
        if name in {"add", "update"}:
            command.add_argument("--file", required=True, type=Path, help="UTF-8 setup JSON with an accounts object; never the saved accounts.json.")
            command.add_argument("--entry", required=True, help="Named entry inside accounts, for example gmail or microsoft.")
        if name == "update":
            command.add_argument("--revision", required=True, type=int, help="Current revision from accounts show; stale edits fail.")
            command.add_argument("--replace-passwords", action="store_true", help="Prompt for replacement passwords; required when password-based server settings change.")
        if name == "check":
            command.add_argument("--read-message", action="store_true", help="Also read one bounded sample without returning its content.")


def _unique_object(pairs):
    """Build a JSON object while rejecting duplicate keys.

    Args:
        pairs: Ordered JSON key/value pairs inspected for duplicate keys.
    """
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON key.")
        result[key] = value
    return result


def load_entry(path: Path, entry: str) -> dict:
    """Read a bounded settings-only template and validate it before any password prompt.

    Args:
        path: Local file path read or atomically replaced by this operation.
        entry: Named account entry inside the settings-only setup document.
    """
    try:
        with path.expanduser().open("rb") as stream:
            raw = stream.read(256_001)
        if len(raw) > 256_000:
            raise ValueError()
        document = json.loads(raw.decode("utf-8-sig"), object_pairs_hook=_unique_object)
        if not isinstance(document, dict) or set(document) != {"accounts"} or not isinstance(document["accounts"], dict):
            raise ValueError()
    except (OSError, ValueError, UnicodeError):
        raise MailError("invalid_setup_file", "Use a UTF-8 JSON file of at most 256 KB with one accounts object and unique keys.") from None
    if entry not in document["accounts"]:
        raise MailError("setup_entry_missing", "The selected --entry is absent from the setup file's accounts object.")
    account = account_input(document["accounts"][entry])
    if account["id"]:
        raise MailError("invalid_setup_file", "Leave id empty in a setup template. Use accounts update ACCOUNT_ID to edit a saved connection.")
    return account


def _password(prompt):
    """Request a nonempty password with hidden terminal input or fail closed.

    Args:
        prompt: Safe terminal prompt text; the entered secret is never echoed.
    """
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("error", getpass.GetPassWarning)
            value = getpass.getpass(prompt, stream=sys.stderr)
    except (getpass.GetPassWarning, EOFError):
        raise MailError("terminal_required", "Open an interactive terminal with hidden password input. Passwords are never accepted as command-line options.") from None
    if not value:
        raise MailError("password_required", "Enter a nonempty password or app password. No account was saved.")
    return value


def _passwords(account, prompt):
    """Prompt only for the password-based endpoints selected by the setup command.

    Args:
        account: Account settings or saved snapshot used by this operation; credentials
            remain in protected storage.
        prompt: Whether this command should ask for new hidden-input passwords.
    """
    passwords = {"incoming": "", "smtp": ""}
    if account["authentication"]["method"] == "microsoft_oauth" or not prompt:
        return passwords
    label = "Gmail app password (not your Google password or device PIN): " if account.get("preset") == "gmail" else "Incoming password or app password: "
    passwords["incoming"] = _password(label)
    if account["smtp"] is not None:
        passwords["smtp"] = _password("SMTP password or app password (enter it again if the same): ")
    return passwords


def _saved_account(application, identity):
    """Resolve one public saved-account snapshot through the management adapter.

    Args:
        application: Shared MailApplication instance whose session owns this adapter
            connection.
        identity: Saved account ID shown by the accounts list command.
    """
    for account in application.manage("list_accounts", {}):
        if account["id"] == identity:
            return account
    raise MailError("account_not_found", "No saved account has this ID. Run accounts list.")


def _signin(application, identity):
    """Display a Microsoft device code and poll until completion or local cancellation.

    Args:
        application: Shared MailApplication instance whose session owns this adapter
            connection.
        identity: Saved Microsoft account ID selected for this explicit sign-in.
    """
    flow = application.manage("start_microsoft_signin", {"account_id": identity})
    try:
        sys.stderr.write(f"Open {flow['verification_uri']}\nEnter code: {flow['user_code']}\n"
                         "Complete sign-in with Microsoft. Waiting; Ctrl+C stops this local sign-in.\n")
        sys.stderr.flush()
        interval = flow["interval"]
        while True:
            time.sleep(interval)
            result = application.manage("poll_microsoft_signin", {"flow_id": flow["flow_id"]})
            if result["status"] == "connected":
                return result
            if result["status"] != "pending":
                raise MailError("microsoft_response", "Microsoft sign-in returned an unsupported state.")
            interval = result["interval"]
    except (Exception, KeyboardInterrupt):
        # Cancellation retires temporary state only; a committed sign-in is preserved.
        try:
            cleanup = application.manage("cancel_microsoft_signin", {"flow_id": flow["flow_id"]})
            if cleanup.get("cleanup_pending"):
                sys.stderr.write("Temporary credential cleanup is pending. Run profile cleanup.\n")
        except Exception:
            sys.stderr.write("Temporary sign-in cleanup could not finish. Check profile show and retry profile cleanup.\n")
        raise


def run(application, options):
    """Translate one setup command into existing shared application operations.

    Args:
        application: Shared MailApplication instance whose session owns this adapter
            connection.
        options: Parsed command-line options for the selected setup action.
    """
    if options.command == "presets":
        return application.manage("presets", {})
    if options.command == "profile":
        return application.manage("profile_info" if options.action == "show" else "retry_cleanup", {})
    if options.action == "list":
        return application.manage("list_accounts", {})
    if options.action in {"add", "update"}:
        account = load_entry(options.file, options.entry)
        revision = None
        if options.action == "update":
            current = _saved_account(application, options.account_id)
            if options.revision != current["revision"]:
                raise MailError("configuration_changed", "The account revision changed. Run accounts show before updating.")
            account["id"] = options.account_id
            revision = options.revision
        passwords = _passwords(account, options.action == "add" or options.replace_passwords)
        try:
            return application.manage("save_account", {"account": account, "passwords": passwords, "expected_revision": revision})
        finally:
            passwords.clear()
    if options.action == "show":
        return _saved_account(application, options.account_id)
    if options.action == "signin":
        return _signin(application, options.account_id)
    if options.action in {"check", "check-smtp"}:
        saved = _saved_account(application, options.account_id)
        account = {key: value for key, value in saved.items() if key not in {"revision", "microsoft_connected"}}
        arguments = {"account": account, "passwords": {"incoming": "", "smtp": ""}, "expected_revision": saved["revision"]}
        if options.action == "check":
            arguments["read_message"] = options.read_message
        return application.manage("check_account" if options.action == "check" else "check_smtp", arguments,
            progress=lambda stage, state: sys.stderr.write(f"{stage}: {state}\n"))
    operation = {"folders": "list_folders", "remove": "remove_account"}[options.action]
    return application.manage(operation, {"account_id": options.account_id})


def display(options, result):
    """Show local outcomes without hiding cleanup warnings or claiming provider success.

    Args:
        options: Parsed command-line options for the selected setup action.
        result: Protocol or application result being checked or displayed.
    """
    if options.command == "accounts" and options.action == "list":
        if not result:
            print("No accounts configured. Start with: accounts add --help")
        for account in result:
            print(f"{account['label']} <{account['email']}>\n  ID: {account['id']}  Revision: {account['revision']}\n"
                  f"  Read: {account['read_enabled']}  Send: {account['send_enabled']}")
    elif options.command == "accounts" and options.action in {"add", "update"}:
        print(f"Account saved.\nID: {result['id']}\nRevision: {result['revision']}\nConnection not checked.")
        if result["authentication"]["method"] == "microsoft_oauth" and not result["microsoft_connected"]:
            print(f"Next: accounts signin {result['id']}")
        else:
            print(f"Next: accounts check {result['id']}")
    elif options.command == "accounts" and options.action == "signin":
        print(f"Microsoft sign-in saved.\nID: {result['account']['id']}\nNext: accounts check {result['account']['id']}")
    else:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    if isinstance(result, dict) and result.get("cleanup_pending"):
        print(f"Credential cleanup pending ({result.get('cleanup_pending_count', 0)} references). Run profile cleanup.", file=sys.stderr)
