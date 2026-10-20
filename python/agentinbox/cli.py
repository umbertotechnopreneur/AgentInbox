"""Executable entry point; passwords are accepted only over local stdin."""

import argparse
import json
import os
import sys

from .application import MailApplication
from .config import data_directory, windows_package_family
from .errors import MailError


def write(value):
    """Write one UTF-8 protocol record."""
    sys.stdout.buffer.write(json.dumps(value, ensure_ascii=False).encode("utf-8") + b"\n")
    sys.stdout.buffer.flush()


def manage(application, session: bool) -> int:
    """Handle local JSON requests, retaining opaque references only in a session."""
    failed = False
    while True:
        raw = sys.stdin.buffer.readline(1_048_577)
        if not raw and session:
            return 1 if failed else 0
        if not raw or len(raw) > 1_048_576:
            write({"ok": False, "error": {"code": "invalid_request", "message": "Supply one setup JSON request of at most 1 MiB per line."}})
            return 1
        try:
            request = json.loads(raw)
            if not isinstance(request, dict) or set(request) not in ({"operation", "arguments"}, {"operation", "arguments", "progress"}) or not isinstance(request["operation"], str):
                raise MailError("invalid_request", "Supply operation and arguments in the local setup request.")
            report = None
            if "progress" in request:
                if request["progress"] is not True or request["operation"] != "check_account":
                    raise MailError("invalid_request", "Progress is available only for an explicit account check.")
                report = lambda stage, state: write({"progress": {"stage": stage, "state": state}})
            result = application.manage(request["operation"], request["arguments"], progress=report)
            response = {"ok": True, "result": result}
        except MailError as error:
            failed = True
            response = {"ok": False, "error": error.result()}
        except (ValueError, TypeError, KeyError):
            failed = True
            response = {"ok": False, "error": {"code": "invalid_request", "message": "The request or saved data has an invalid format."}}
        except Exception:
            write({"ok": False, "error": {"code": "operation_failed", "message": "The operation could not finish. No success is assumed; check local setup."}})
            return 1
        write(response)
        if not session:
            return 1 if failed else 0


def main(arguments=None):
    """Run one explicit current command without legacy aliases or migration."""
    parser = argparse.ArgumentParser(prog="agentinbox", description="Local IMAP/POP3 reads and individually approved SMTP sending.")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("mcp", help="Run the local MCP server over stdio.")
    management = commands.add_parser("manage", help="Manage accounts, inspect profile paths, read mail and prepare or cancel messages using local stdin JSON.")
    management.add_argument("--session", action="store_true", help="Read one JSON request per line until EOF. Use this session for search references and continuations.")
    review = commands.add_parser("review", help="Review one pending outgoing message locally.")
    review.add_argument("--pipe", action="store_true", help="Use private redirected review handles from the Windows app.")
    review.add_argument("request_id")
    options = parser.parse_args(arguments)
    try:
        application = MailApplication(data_directory())
        if options.command == "mcp":
            from .mcp import serve
            return serve(application)
        if options.command == "review":
            from .review import desktop_review, pipe_review
            if options.pipe:
                return pipe_review(application, options.request_id, write)
            result = application.open_review(options.request_id) if os.environ.get("AGENTINBOX_REVIEW_APP") or windows_package_family() else desktop_review(application, options.request_id)
            write({"ok": True, "result": result})
            return 0
        return manage(application, options.session)
    except MailError as error:
        if options.command == "mcp":
            sys.stderr.write(f"AgentInbox startup failed: {error.code}. {error}\n")
        else:
            write({"ok": False, "error": error.result()})
        return 1
    except (ValueError, TypeError, KeyError):
        error = {"code": "invalid_request", "message": "The request or saved data has an invalid format."}
    except Exception:
        error = {"code": "operation_failed", "message": "The operation could not finish. No success is assumed; check local setup."}
    if options.command == "mcp":
        sys.stderr.write("AgentInbox stopped after an internal failure.\n")
    else:
        write({"ok": False, "error": error})
    return 1
