# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Profile-scoped activity leases for live stdio MCP sessions.
# Created with AI: OpenAI Codex, 2026-11-10.
# Human guidance: Umberto Giacobbi; report live connections in both desktop trays.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Detect live local MCP sessions using OS locks released on process exit."""

import errno
import os
import re
from pathlib import Path
import uuid

PROBE_ENV = "AGENTINBOX_MCP_STARTUP_CHECK"
READY = b"1"


def _lock(stream):
    """Acquire the reserved first byte without waiting.

    Args:
        stream: Open writable lease file positioned anywhere.
    """
    stream.seek(0)
    try:
        if os.name == "nt":
            import msvcrt
            msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError as error:
        if error.errno not in {errno.EACCES, errno.EAGAIN}:
            raise
        return False
    return True


class ConnectionLease:
    """Publish one ready marker for as long as a negotiated MCP session lives."""

    def __init__(self, root: Path):
        """Prepare a lease without touching disk before MCP initialization.

        Args:
            root: Absolute profile directory shared with the desktop configuration app.
        """
        self.path = root / "mcp-activity" / (uuid.uuid4().hex + ".lease")
        self._stream = None

    def __enter__(self):
        """Return the lease owned by the stdio request loop."""
        return self

    def start(self):
        """Publish activity only after the MCP client finishes initialization."""
        if os.environ.get(PROBE_ENV) == "1":
            return
        try:
            self.path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
            descriptor = os.open(self.path, os.O_CREAT | os.O_EXCL | os.O_RDWR, 0o600)
            self._stream = os.fdopen(descriptor, "r+b")
            self._stream.write(b"\0")
            self._stream.flush()
            if not _lock(self._stream):
                self.close()
                return
            # The readiness byte is outside the locked range on Windows.
            self._stream.seek(1)
            self._stream.write(READY)
            self._stream.flush()
        except OSError:
            self.close()

    def close(self):
        """Release and remove this session's lease without masking an MCP outcome."""
        if self._stream is None:
            return
        try:
            self._stream.close()
            self.path.unlink(missing_ok=True)
        except OSError:
            pass
        finally:
            self._stream = None

    def __exit__(self, _type, _value, _traceback):
        """Release this session's lease when stdio closes or fails.

        Args:
            _type: Exception type, if the MCP loop failed.
            _value: Original exception, propagated without suppression.
            _traceback: Original exception traceback, never logged here.
        """
        self.close()


def is_connected(root: Path) -> bool:
    """Report whether any initialized stdio session still holds its lease.

    Args:
        root: Absolute profile directory whose MCP activity should be inspected.
    """
    directory = root / "mcp-activity"
    try:
        entries = os.scandir(directory)
    except FileNotFoundError:
        return False
    with entries:
        return _probe(entries)


def _probe(entries):
    """Look for a locked, published lease while the profile probe lock is held.

    Args:
        entries: Iterator over this profile's MCP activity directory.
    """
    for entry in entries:
        if not re.fullmatch(r"[0-9a-f]{32}\.lease", entry.name) or not entry.is_file(follow_symlinks=False):
            continue
        path = Path(entry.path)
        try:
            with path.open("r+b", buffering=0) as stream:
                stream.seek(1)
                if stream.read(1) != READY:
                    continue
                if not _lock(stream):
                    return True
        except FileNotFoundError:
            continue
        # A ready lease is published only once, so an unlocked one cannot become active.
        try:
            path.unlink(missing_ok=True)
        except OSError:
            pass
    return False
