"""Synthetic checks for process isolation, bounded references and private session state."""

import unittest

from agentinbox.models import SessionState


class SessionStateTests(unittest.TestCase):
    """Exercise session lifetime without files, credentials, network or UI objects."""

    def test_engine_sessions_cannot_reuse_each_others_references_or_tokens(self):
        first = SessionState()
        second = SessionState()
        first.references["opaque-reference"] = {"expires": 2000, "account_id": "synthetic"}
        first.continuations["opaque-page"] = {"expires": 2000}
        first.access_tokens[("synthetic", 1)] = ("synthetic-token", 2000)
        self.assertEqual({}, second.references)
        self.assertEqual({}, second.continuations)
        self.assertEqual({}, second.access_tokens)

    def test_expiry_and_capacity_apply_to_both_search_caches(self):
        state = SessionState()
        for cache in (state.references, state.continuations):
            cache["expired"] = {"expires": 1000}
            for index in range(1001):
                cache[str(index)] = {"expires": 2000}
        state.expire_references(1000)
        for cache in (state.references, state.continuations):
            self.assertEqual(1000, len(cache))
            self.assertNotIn("expired", cache)
            self.assertNotIn("0", cache)
            self.assertIn("1000", cache)
        state.expire_references(2000)
        self.assertEqual({}, state.references)
        self.assertEqual({}, state.continuations)

    def test_diagnostic_representation_never_includes_reference_content_or_tokens(self):
        state = SessionState()
        state.references["private-reference"] = {"folder": "private-folder"}
        state.continuations["private-continuation"] = {"query": "private-query"}
        state.access_tokens[("synthetic", 1)] = ("private-token", 2000)
        for private in ("private-reference", "private-folder", "private-continuation", "private-query", "private-token"):
            self.assertNotIn(private, repr(state))
