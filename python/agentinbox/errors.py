# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Safe failures for the CLI, desktop adapter and MCP transport.
# Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
# Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
# Evidence: docs/VALIDATION.md (source review; execution checks pending).
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Safe failures for the CLI, desktop adapter and MCP transport."""

import base64
import re
import unicodedata


def server_response(protocol: str, reply: str | bytes, *secrets: str) -> str:
    """Bound and redact an untrusted reply for explicit local diagnostics only.

    Args:
        protocol: Protocol label included in the local redacted diagnostic.
        reply: Untrusted server reply to bound and redact before local display.
        secrets: Sensitive values to remove from local diagnostic output.
    """
    value = reply.decode("utf-8", errors="replace") if isinstance(reply, bytes) else reply
    sensitive = set()
    for secret in secrets:
        if secret:
            sensitive.update((secret, base64.b64encode(secret.encode("utf-8")).decode("ascii"),
                              secret.replace("\\", "\\\\").replace('"', '\\"')))
    for secret in sorted(sensitive, key=len, reverse=True):
        value = value.replace(secret, "[redacted]")
    value = re.sub(r"[\w.+-]+@[\w.-]+", "[redacted address]", value)
    value = " ".join("".join(character if not unicodedata.category(character).startswith("C") else " "
                             for character in value).split())
    if not value:
        return ""
    return f"{protocol}: {value[:1024]}" + ("…" if len(value) > 1024 else "")


class MailError(Exception):
    """Keep public errors separate from redacted, opt-in local server diagnostics."""

    def __init__(self, code: str, message: str, *, server_response: str = ""):
        """Capture a safe public error and optional private local diagnostics.

        Args:
            code: Status or safe error code associated with the operation.
            message: Safe actionable explanation that must not contain credentials or
                message content.
            server_response: Optional bounded, redacted provider explanation for local
                setup only.
        """
        super().__init__(message)
        self.code = code
        self.server_response = server_response

    def result(self, *, include_server_response: bool = False) -> dict:
        """Return public errors, adding server diagnostics only for local setup.

        Args:
            include_server_response: Whether the local caller explicitly requested
                provider diagnostics.
        """
        result = {"code": self.code, "message": str(self)}
        if include_server_response and self.server_response:
            result["server_response"] = self.server_response
        return result
