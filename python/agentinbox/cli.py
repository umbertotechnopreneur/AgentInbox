"""Executable entry point; passwords are accepted only over local stdin."""

import argparse
import json
import sys

from .application import MailApplication
from .config import data_directory
from .errors import MailError


def write(value):
    """Write one UTF-8 protocol record."""
    sys.stdout.buffer.write(json.dumps(value, ensure_ascii=False).encode("utf-8") + b"\n")
    sys.stdout.buffer.flush()


def main(arguments=None):
    """Run one explicit current command without legacy aliases or migration."""
    parser = argparse.ArgumentParser(prog="agentinbox", description="Local IMAP reads and individually approved SMTP sending.")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("mcp", help="Run the local MCP server over stdio.")
    commands.add_parser("manage", help="Read one local setup request as JSON from stdin.")
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
            write({"ok": True, "result": desktop_review(application, options.request_id)})
            return 0
        raw = sys.stdin.buffer.readline(1_048_577)
        if not raw or len(raw) > 1_048_576:
            raise MailError("invalid_request", "Supply one setup JSON request of at most 1 MiB over stdin.")
        request = json.loads(raw)
        if not isinstance(request, dict) or set(request) != {"operation", "arguments"} or not isinstance(request["operation"], str):
            raise MailError("invalid_request", "Supply operation and arguments in the local setup request.")
        result = application.manage(request["operation"], request["arguments"])
        write({"ok": True, "result": result})
        return 0
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
