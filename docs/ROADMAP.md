# Roadmap

**All planned provider features are read-only.** Write actions require a separate future decision.

For the practical steps and completion checks, see the [MVP delivery plan](MVP_PLAN.md).

| Step | Result |
| --- | --- |
| 0. Foundation | Repository, brand, executable, discovery tools, tests and packaging |
| 1. Account setup | Browser sign-in, multiple accounts and protected credentials |
| 2. Email reads | Provider search and selected-message reading |
| 3. Unified mail tools | Search across accounts with short results and clear coverage |
| 3C. Calendars | Calendar discovery, appointment search and a unified agenda |
| 4. Public preview | Simple installation, provider registration readiness and platform checks |

Each step needs working tests before it is advertised as available.

The September 11 increment adds configurable recent-mail defaults and Google request-limit handling. Windows x64 MSIX `0.1.1.19` is built, signed and locally installed, and all 229 .NET tests passed. Desktop interactions and live-provider checks remain pending. See [validation](VALIDATION.md) for the evidence and earlier preview records below.

The current source passed 113 synthetic tests and the Desktop Release build. Step 4 includes Windows MSIX / WinUI 3 preview `0.1.1.10`, locally installed with the Acrylic wizard, progressive disclosure, account connection checks and single-instance implementation. The upgrade, synthetic native-window launch without diagnostics and installed alias CLI/MCP smoke passed; the redesigned desktop's visual and interaction checks remain with the owner. The earlier CLI build passed real reads across four accounts. Next, when requested: validate desktop interactions, clean-machine installation, UI sign-in/Codex plugin setup, deliberate real recovery scenarios and a small pilot.

A bundled local Codex plugin is included in the Windows setup scope. No public marketplace publication, web dashboard or hosted service is planned.
