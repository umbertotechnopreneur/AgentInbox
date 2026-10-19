"""Run AgentInbox directly from source or an MSIX-owned Python directory."""

import sys
from pathlib import Path

if sys.version_info < (3, 11):
    sys.stderr.write("AgentInbox requires Python 3.11 or later.\n")
    raise SystemExit(2)

sys.dont_write_bytecode = True
# Embedded Python ignores the script directory unless it is added explicitly.
sys.path.insert(0, str(Path(__file__).resolve().parent))

from agentinbox.cli import main

raise SystemExit(main())
