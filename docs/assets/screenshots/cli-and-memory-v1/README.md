# CLI and memory screenshots

These two owner-requested documentation images use the supplied captures from October 28, 2026. The raw captures remain private.

- `cli.png`: 1555 × 1011 pixels. The original help text is retained with improved spacing, a decorative ASCII banner and a mint-to-cyan-to-lilac text gradient. The shell widgets and unrelated desktop edges are removed. The banner is part of this presentation; no CLI code was changed.
- `memory.png`: 1881 × 836 pixels. The Task Manager capture is translated into English, with decimal points, column labels and a tighter crop. The process names, IDs and all performance values match the supplied snapshot.

## Evidence and limits

The memory snapshot contains five Python processes under AgentInbox. PIDs 22112, 39976, 42392, 27888 and 16900 show 14.2, 14.6, 14.6, 14.6 and 14.6 MB respectively. The captured group total is 72.5 MB; keep that displayed total rather than summing rounded rows. All visible CPU/GPU readings are 0%, disk readings 0 MB/s and network readings 0 Mbps.

The original column header was outside the crop. These values are the memory figures shown by Task Manager in one moment, not a measurement of total virtual memory, peak use, every workload, or shared-page savings. No benchmark or live desktop inspection was performed for these edits. Microsoft documents that a [process working set includes shared code pages](https://learn.microsoft.com/en-us/windows/win32/procthread/process-working-set); this is general operating-system behavior, not a measured saving in this capture.

Both images were visually reviewed against the provided content. They are documentation presentations rather than untouched runtime captures. Third-party marks belong to their owners; no endorsement is implied.

## Final prompts

Tool mode: built-in ImageGen, edit mode.

### CLI

```text
Use case: text-localization. Edit target: supplied screenshot of the real AgentInbox CLI help.
Create a clean, crisp terminal screenshot suitable for a README. Preserve the real help content, improve legibility and spacing, remove the desktop/background slivers and the personal shell status widgets at the bottom. Front-facing flat charcoal terminal surface, modest padding, no perspective, no floating mockup or extra decorations. Large sharp monospaced lettering.
At the top add a prominent ASCII-art banner reading AGENT INBOX. This must be built from visible monospaced ASCII characters, not a normal giant font. Use exactly this character grid, preserving whitespace:
 ###    ###   #####  #   #  #####       #####  #   #  ####    ###   #   #
#   #  #   #  #      ##  #    #           #    ##  #  #   #  #   #  #   #
#   #  #      #      ##  #    #           #    ##  #  #   #  #   #   # #
#####  # ###  ####   # # #    #           #    # # #  ####   #   #    #
#   #  #   #  #      #  ##    #           #    #  ##  #   #  #   #   # #
#   #  #   #  #      #  ##    #           #    #  ##  #   #  #   #  #   #
#   #   ###   #####  #   #    #         #####  #   #  ####    ###   #   #
Color ONLY this ASCII banner with a tasteful left-to-right mint-to-cyan-to-muted-lilac gradient as if ANSI terminal text; no background gradient or glow.
Below it show the actual command and help, in English, using the following text verbatim; line wrapping and alignment may adapt to the canvas:
> AgentInbox.exe -B -m agentinbox --help

usage: agentinbox [-h] {profile,presets,accounts,mcp,manage} ...

Connect as many compatible mailboxes as you need to your AI agent through one local MCP connection.

positional arguments:
  profile    Show profile paths or retry pending credential cleanup.
  presets    Show the supported providers and their connection requirements.
  accounts   Add, inspect, update, sign in and check existing mailboxes.
  mcp        Expose all enabled mailboxes and their selected folders to your AI agent over stdio MCP.
  manage     Advanced automation: read local management requests as stdin JSON.

options:
  -h, --help  show this help message and exit

Start with: agentinbox accounts add --help.
Gmail, Microsoft, iCloud, GoDaddy and other compatible IMAP/POP3 providers.
Choose up to twenty IMAP folders per account; POP3 exposes one INBOX.
Setup never creates a mailbox at the provider.

Use light gray body text, restrained accent for command names. Fit all text without truncation. No additional copy, subtitle, AI attribution, watermark, promotional slogans, invented command, success status or runtime result. Approximate canvas 2000 by 1300, prioritize sharp legible text. The banner is a presentation treatment for the screenshot.
```

### Memory

```text
Use case: text-localization. Edit target: supplied Windows Task Manager screenshot.
Make a clean, legible English version while retaining its native Windows dark appearance and factual contents exactly. Crop out the left navigation and right cut-off command-line/private path column. Keep a simple straight rectangular Task Manager view, no title card, decoration, glow, extra logos, feature claims or added performance claims. Improve clarity of text, consistent row spacing and margins. Preserve the AgentInbox app icon and Python icons.
Translate the visible group headings to "Apps (0)", "Background processes (1)", "Windows processes (0)". Retain the expanded "AgentInbox (5)" group with exactly five Python children. Add a compact native header row with the column labels "Name", "PID", "CPU", "Memory", "Disk", "Network", "GPU" above the data to make the crop understandable.
Mandatory exact values (convert decimal commas to English decimal points):
AgentInbox (5) | blank PID | 0% | 72.5 MB | 0 MB/s | 0 Mbps | 0%
  Python | 22112 | 0% | 14.2 MB | 0 MB/s | 0 Mbps | 0%
  Python | 39976 | 0% | 14.6 MB | 0 MB/s | 0 Mbps | 0%
  Python | 42392 | 0% | 14.6 MB | 0 MB/s | 0 Mbps | 0%
  Python | 27888 | 0% | 14.6 MB | 0 MB/s | 0 Mbps | 0%
  Python | 16900 | 0% | 14.6 MB | 0 MB/s | 0 Mbps | 0%
Do not change, sum, round or infer values. In particular preserve total 72.5 MB as captured, even though rounded individual values sum differently. Use a very restrained native plum tint in metric cells like the original. No charts, arrows, badges or callouts. No mention of AI generation. Show every row completely, with sharp readable type. Approximate size 1800 by 800. This is a localized presentation of a supplied snapshot, not a new measurement. Do not print that production note.
```
