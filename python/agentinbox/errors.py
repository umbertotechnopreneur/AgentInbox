"""Safe failures for the CLI, desktop adapter and MCP transport."""

import base64
import re
import unicodedata


def server_response(protocol: str, reply: str | bytes, *secrets: str) -> str:
    """Bound and redact an untrusted reply for explicit local diagnostics only."""
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
        super().__init__(message)
        self.code = code
        self.server_response = server_response

    def result(self, *, include_server_response: bool = False) -> dict:
        """Return public errors, adding server diagnostics only for local setup."""
        result = {"code": self.code, "message": str(self)}
        if include_server_response and self.server_response:
            result["server_response"] = self.server_response
        return result
