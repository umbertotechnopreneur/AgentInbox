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
            if not isinstance(request, dict) or set(request) not in ({"operation", "arguments"}, {"operation", "arguments", "progress"}, {"operation", "arguments", "diagnostics"}) or not isinstance(request["operation"], str):
                raise MailError("invalid_request", "Supply operation and arguments in the local setup request.")
            report = None
            if "progress" in request:
                if request["progress"] is not True or request["operation"] not in {"check_account", "check_smtp"}:
                    raise MailError("invalid_request", "Progress is available only for an explicit account or SMTP check.")
                report = lambda stage, state: write({"progress": {"stage": stage, "state": state}})
            diagnostic = None
            if "diagnostics" in request:
                if request["diagnostics"] is not True or request["operation"] not in {"start_microsoft_signin", "poll_microsoft_signin"}:
                    raise MailError("invalid_request", "Sign-in details are available only for an explicit Microsoft sign-in.")
                diagnostic = lambda message: write({"diagnostic": message})
            result = application.manage(request["operation"], request["arguments"], progress=report, diagnostic=diagnostic)
            response = {"ok": True, "result": result}
        except MailError as error:
            failed = True
            response = {"ok": False, "error": error.result(include_server_response=True)}
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
    parser = argparse.ArgumentParser(prog="agentinbox",
        description="Connect as many compatible mailboxes as you need to your AI agent through one local MCP connection.",
        epilog="Gmail, Microsoft, iCloud, GoDaddy and other providers that allow IMAP or POP3 access. Work across dozens of selected IMAP folders across accounts (up to twenty per account); POP3 exposes one INBOX per account. Choose read/send permissions for each mailbox in local setup.")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("mcp", help="Expose all enabled mailboxes and their selected folders to your AI agent over stdio MCP.",
        description="One local MCP connection for all enabled mailboxes. Your agent searches each selected account and folder in turn; mail stays with its providers.")
    management = commands.add_parser("manage", help="Add mailboxes, choose shared folders and permissions, and check connections using stdin JSON.",
        description="Configure multiple IMAP/POP3 mailboxes and optional SMTP sending. Choose up to twenty IMAP folders per account; POP3 has one INBOX per account.")
    management.add_argument("--session", action="store_true", help="Read one JSON request per line until EOF. Use this session for search references and continuations.")
    options = parser.parse_args(arguments)
    try:
        application = MailApplication(data_directory())
        if options.command == "mcp":
            from .mcp import serve
            return serve(application)
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
