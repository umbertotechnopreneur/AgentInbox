"""Safe failures for the CLI, desktop adapter and MCP transport."""


class MailError(Exception):
    """Expose an actionable error without including credentials or provider replies."""

    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code

    def result(self) -> dict:
        """Return the public error contract."""
        return {"code": self.code, "message": str(self)}
