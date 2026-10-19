# Lessons

Keep reusable technical findings here. Repository rules belong in [AGENTS.md](../../AGENTS.md); completed work belongs in [archive](archive.md).

- Read the actual PromptMeUp packaging script when aligning MSIX builds. A stale instruction is not evidence of the current workflow. See [Windows packaging](../../packaging/windows/README.md).
- Keep first-import security evidence separate from builds. Repeated scans of unchanged dependencies and installed SDK trees add delay without satisfying a new import requirement.
- When build outputs move outside the project, exclude old `bin`/`obj` trees from source discovery. Disposable builds must not leave shared compiler processes holding their temporary files.
- Bound protocol reads before allocating message data. A size check after parsing cannot prevent an oversized response from exhausting memory.
- Preserve a committed account save, sign-in or SMTP outcome when protected-data cleanup fails. Report cleanup separately; an uncertain SMTP submission must not become an automatic resend.
- Search references belong to the process that created them. Use a persistent management session when reading a search result. Packaged integrations need a stable execution alias rather than a versioned installation path.
- Synthetic tests establish the tested application boundaries. They do not establish live-provider interoperability, native credential protection, GUI behavior or MSIX startup. Record those separately in [validation](../../docs/VALIDATION.md).
- WinUI rejects reparenting an application-owned resource dictionary into a control. Create each TabView and its local resources together in a DataTemplate; keep the native control template. Verify actual selection contrast and page recreation at runtime, since a successful XAML build does not establish either.
