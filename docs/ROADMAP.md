# Roadmap

We're working toward a preview you can install, connect to your accounts and use with confidence. All planned account features are read-only. Sending mail or changing calendars would need a separate decision.

The [plan for the first usable version](MVP_PLAN.md) lists the work and checks for each step.

| Step | Result |
| --- | --- |
| 0. Foundation | Set up the repository, app, branding, tests and packages |
| 1. Account setup | Sign in through a browser, add several accounts and protect sign-in tokens |
| 2. Email reads | Search for messages and open the ones you need |
| 3. Unified mail tools | Search across accounts and show clearly which ones were checked |
| 3C. Calendars | Find calendars, search appointments and show one combined agenda |
| 4. Public preview | Make installation easier, prepare Google and Microsoft registration, and check supported platforms |

Each step needs passing tests before we call it available.

## Where things stand

The latest recorded local Windows x64 installation is **`0.1.1.20`, from September 12**. It adds commands to open individual setup screens, a demo with made-up accounts and layout improvements. The preceding local preview passed 291 automated tests and basic CLI/MCP checks. Desktop interactions still need checking.

Newer code adds limits on requests and assistant output, small in-memory read caches, and a Windows editor for limits and usage. The September 12–13 changes have **not been built, tested or installed**. See [read limits](READ_GUARDRAILS.md) for details, including restart requirements and the remaining work.

Earlier versions passed real reads across four accounts and several Windows installation, command and UI checks. The [test record](VALIDATION.md) keeps the version-by-version results; those results don't cover newer code automatically.

## What's next

When the owner requests the checks: try the desktop interactions, install on a clean Windows machine, sign in through the UI, set up the Codex plugin, and test recovery after real account access is revoked. Then run a small pilot.

The Windows setup includes a local Codex plugin. A public marketplace release, web dashboard and hosted service aren't planned.
