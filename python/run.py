"""Run AgentInbox directly from source or an MSIX-owned Python directory."""

import sys

if sys.version_info < (3, 11):
    sys.stderr.write("AgentInbox requires Python 3.11 or later.\n")
    raise SystemExit(2)

sys.dont_write_bytecode = True

from agentinbox.cli import main

raise SystemExit(main())
