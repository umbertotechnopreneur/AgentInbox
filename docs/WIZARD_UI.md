# Windows UI implementation

The Windows app is configuration only. Keep mail behavior in Python and delegate settings, sign-in and connection checks through the local JSON adapter. Sending belongs to the MCP interface used by Codex.

Use native WinUI controls, an Acrylic backdrop and top-left content that fits the viewport. Keep a visible connection list, the editable connection name above the stock TabView, colored leading icons and a bold selected label. Save and Cancel remain next to the form in a persistent bottom bar. Do not add decorative cards or controls that move on hover.

IMAP and SMTP checks share `AccountCheckWindow`, actual progress stages, cancellation and selectable server diagnostics. Only the incoming check offers folder selection and an optional sample read. No send button or approval flow belongs here.

Use clear English, tooltips, accessible names and keyboard navigation. Keep advanced Codex configuration collapsed. The footer links to the product policies and shows VibeWare with its floppy symbol.

[Windows setup](WINDOWS_SETUP.md) describes the user flow. [Validation](VALIDATION.md) records runtime evidence; source edits and old screenshots do not establish current visual correctness. Artwork provenance remains in [brand](BRAND.md).
