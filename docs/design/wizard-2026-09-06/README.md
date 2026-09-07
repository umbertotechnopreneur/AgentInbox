# Wizard design concepts — 2026-09-06

Status: proposed interface artwork for review. These images do not represent implemented or runtime-validated behavior.

The four renderings cover Welcome, Accounts, Sharing, and Connect to Codex in one coherent light Mica direction. Repository UI strings remain English. All account examples use example.test.

- [Welcome](welcome.png): short introduction, original illustration, essential privacy context.
- [Accounts](accounts.png): provider logos, compact account rows, collapsed registration details.
- [Sharing](sharing.png): select one account and reveal its settings, with explicit saving.
- [Connect to Codex](connect-to-codex.png): installation status, one primary action, collapsed manual setup.

## Layout and progressive disclosure

- Keep the title bar, navigation, background material, and primary action consistent across screens.
- Never number the steps or show numeric step counters. Communicate progress with connected icons, completion checkmarks, an active accent, and quieter upcoming stages.
- Use a restrained envelope/calendar illustration, small Fluent icons, short headings, and compact rows.
- Keep a persistent Privacy & terms entry that opens the MailMeUp, Google, and Microsoft links. Keep About & support accessible.
- Keep the essential read-only and AI-service disclosures visible; put detailed explanations behind a dedicated view.
- Show provider registration only when needed, with an always-available Provider setup entry. This preview still requires the user's own OAuth application configuration.
- Show connected accounts as compact rows. Use search and pagination when account volume exceeds the available space.
- Edit one account's sharing choices at a time in a dedicated detail area. New UI-connected accounts start with sharing disabled.
- Preserve per-account Save choices behavior. Let the user choose all current and future calendars or individual calendars; paginate or search long calendar lists.
- Keep installation details and manual Codex commands behind a disclosure. Distinguish local plugin installation from tools loaded in a running Codex session.
- Avoid scrolling for ordinary wizard content at the supported window size. For small displays or large accessibility text, provide an accessible adaptive layout and scrolling fallback rather than clipping or hiding content.

## Single UI instance

Implementation requirement: a second desktop launch should activate and restore the existing setup window, then exit. Apply this only to the desktop UI; independent CLI/MCP processes must remain supported. This behavior is not implemented by these rendering files.

## Assets and provenance

The provider vectors were downloaded from the SVG Logos CDN for later local bundling:

- [Google vector](assets/google-icon.svg) — https://cdn.svgporn.com/logos/google-icon.svg
- [Microsoft vector](assets/microsoft-icon.svg) — https://cdn.svgporn.com/logos/microsoft-icon.svg
- Upstream collection: https://github.com/gilbarbara/logos

Provider marks retain their original colors and identify compatibility. The raster renderings contain generated representations; use the downloaded SVGs in the implementation.

The existing MailMeUp application icon is the identity reference. Rendering tool mode: built-in image_gen; no CLI fallback. Final prompts are recorded in GENERATION.md. No application code or runtime configuration is changed by this concept set.
