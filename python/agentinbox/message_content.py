# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Decode bounded MIME message bodies without executing HTML or loading resources.
# Created with AI: OpenAI Codex; extracted shared body conversion, 2026-10-27.
# Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
# Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
# Evidence: docs/VALIDATION.md (source review; execution checks pending).
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Decode bounded MIME message bodies without executing HTML or loading resources."""

from html.parser import HTMLParser

from .errors import MailError


class _PlainHtml(HTMLParser):
    """Extract visible text without executing markup or fetching remote resources."""

    def __init__(self):
        """Initialize visible text collection with HTML character references decoded."""
        super().__init__(convert_charrefs=True)
        self.parts = []
        self.hidden = 0

    def handle_starttag(self, tag, attrs):
        """Hide active HTML sections and preserve visible block boundaries.

        Args:
            tag: Lowercase HTML element name supplied by HTMLParser.
            attrs: Parsed HTML attributes; none are executed or fetched.
        """
        if tag in {"script", "style", "head"}:
            self.hidden += 1
        elif tag in {"br", "p", "div", "li"}:
            self.parts.append("\n")

    def handle_endtag(self, tag):
        """Leave a hidden HTML section when its closing tag is encountered.

        Args:
            tag: Lowercase HTML element name supplied by HTMLParser.
        """
        if tag in {"script", "style", "head"}:
            self.hidden = max(0, self.hidden - 1)

    def handle_data(self, data):
        """Collect text only outside hidden HTML sections.

        Args:
            data: Decoded HTML text fragment supplied by HTMLParser.
        """
        if not self.hidden:
            self.parts.append(data)


def message_body(message) -> str:
    """Prefer plain text and strip HTML markup when only HTML is available.

    Args:
        message: Parsed EmailMessage from an already size-bounded transport read.
    """
    part = message.get_body(preferencelist=("plain", "html"))
    content = ""
    if part is not None:
        try:
            content = part.get_content()
        except (LookupError, UnicodeError, ValueError) as error:
            raise MailError("invalid_message", "The message text could not be decoded.") from error
        if part.get_content_type() == "text/html":
            parser = _PlainHtml()
            parser.feed(content)
            content = "".join(parser.parts)
    return content
