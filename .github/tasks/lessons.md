# Lessons

Keep reusable findings here. Rules belong in [AGENTS.md](../../AGENTS.md), evidence in [validation](../../docs/VALIDATION.md).

- Keep an editable draft separate from saved engine snapshots and native controls. Publish collection/selection changes coherently so two-way selection bindings cannot overwrite an account while it is loading. Recreating a page should rebind its model, not reconstruct or discard its draft.
- A successful build does not establish UI behavior, native credentials, provider interoperability or delivery. Record each boundary separately.
- Save the user's provider choice as metadata. Server names alone cannot recover that choice; unfinished sending fields must not erase an explicit Microsoft sign-in selection.
- A generic Microsoft registration failure does not identify its cause. Preserve the bounded provider reason locally, redact request and response secrets, and keep the public MCP error separate.
- Keep submission outcomes after later local failures. A missing final SMTP result is never permission to retry an email automatically.
- Claim a caller-known request ID before SMTP. SQLite uniqueness is enough to reject duplicates; no message queue or background worker is needed.
- Bound protocol reads before allocation. Checking size after parsing cannot prevent oversized responses from exhausting memory.
- Search references belong to their process. Use a persistent management session for search followed by read.
- Packaged integrations need stable execution aliases and an explicit shared profile, not versioned WindowsApps paths.
- WinUI resource dictionaries cannot be reparented from application resources to a control. Prefer the stock TabView; build success does not establish selection contrast or page recreation.
- Keep import scans separate from builds and reuse evidence for unchanged inputs. Exclude stale `bin`/`obj` source trees and prevent compiler servers from retaining disposable output.
