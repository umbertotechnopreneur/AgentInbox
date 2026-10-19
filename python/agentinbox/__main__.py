"""Start the engine through the package's stable Python execution alias."""

import sys

if sys.version_info < (3, 11):
    sys.stderr.write("AgentInbox requires Python 3.11 or later.\n")
    raise SystemExit(2)

sys.dont_write_bytecode = True

from .cli import main

raise SystemExit(main())
