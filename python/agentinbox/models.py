# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Shared account contracts and explicitly process-local session state.
# Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
# Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
# Evidence: docs/VALIDATION.md (source review; execution checks pending).
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Shared account contracts and explicitly process-local session state."""

from dataclasses import dataclass, field
from typing import Literal, NotRequired, TypedDict


class EndpointPreset(TypedDict):
    """Shared provider defaults without a user's sign-in email."""

    host: str
    port: int
    tls: Literal["tls", "starttls"]


class ServerSettings(EndpointPreset):
    """One encrypted mail endpoint, independent of CLI, MCP and Windows controls."""

    username: str


class IncomingSettings(ServerSettings):
    """The reading endpoint adds the selected incoming protocol."""

    protocol: Literal["imap", "pop3"]


class PasswordSettings(TypedDict):
    """A password choice without the password itself."""

    method: Literal["password"]


class MicrosoftSettings(TypedDict):
    """Public Microsoft registration settings without tokens or sign-in codes."""

    method: Literal["microsoft_oauth"]
    client_id: str
    tenant: str


AuthenticationSettings = PasswordSettings | MicrosoftSettings


class ProviderPreset(TypedDict):
    """The same catalog entry returned to CLI and Windows setup."""

    label: str
    incoming: dict[str, EndpointPreset]
    smtp: EndpointPreset | None
    authentication_methods: list[str]
    notice: NotRequired[str]


class AccountSettings(TypedDict):
    """The same validated account payload used by every management adapter."""

    id: str
    label: str
    email: str
    preset: NotRequired[Literal["custom", "gmail", "icloud", "outlook", "microsoft365"]]
    authentication: AuthenticationSettings
    incoming: IncomingSettings
    smtp: ServerSettings | None
    read_enabled: bool
    send_enabled: bool
    folders: list[str]


class SavedAccount(AccountSettings):
    """Private stored metadata; credential references never belong in public results."""

    revision: int
    incoming_secret: str | None
    smtp_secret: str | None
    oauth_secret: str | None


@dataclass
class SessionState:
    """Own only ephemeral references and access tokens for one engine process.

    Accounts, protected refresh credentials and send outcomes live in their
    existing stores. This object is never serialized or shared across processes.
    """

    references: dict[str, dict] = field(default_factory=dict, repr=False)
    continuations: dict[str, dict] = field(default_factory=dict, repr=False)
    access_tokens: dict[tuple, tuple[str, float]] = field(default_factory=dict, repr=False)

    def expire_references(self, now: float) -> None:
        """Bound each search cache and remove expired entries before they are used.

        Args:
            now: Current Unix timestamp used to expire process-local reference entries.
        """
        for cache in (self.references, self.continuations):
            for key in list(cache):
                if cache[key]["expires"] <= now:
                    del cache[key]
            while len(cache) > 1000:
                del cache[next(iter(cache))]
