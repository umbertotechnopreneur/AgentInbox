# Wizard rendering prompts

Tool mode: built-in image_gen. Generated on 2026-09-06 as concept artwork, not shipping screenshots. The owner requested unnumbered steps and visual progression.

Provider SVGs are saved separately for implementation; raster renderings are illustrative. All account identities are synthetic.

## Welcome

Reference: resources/mailmeup-logo-512-safe.png (existing app icon).

```text
Use case: ui-mockup
Asset type: one high-fidelity visual concept, screen 1 of a coherent four-screen MailMeUp Windows 11 setup wizard. This is proposed UI artwork, not an actual running app.
Input image: brand-icon reference ONLY. Preserve the supplied blue MailMeUp envelope/calendar icon at small app-icon size; completely redesign the old interface. Do not place the reference on a black square.
Primary request: Render a polished, modern native WinUI 3 wizard with visible LIGHT MICA material, a subtle dimensional background illustration, Fluent outline icons and very concise English text. One landscape image, approximately 1600 by 1000. Entire application window must be visible front-on, no perspective or surrounding desktop. Window border has 12px corners, tiny outside margin, delicate shadow.
Shared shell to reuse on all four screens: integrated 42px transparent Mica title bar, tiny MailMeUp icon and wordmark at top left, native minimize/maximize/close at top right. A narrow 225px navigation rail occupying the left; remaining large main area. Material feels like Windows 11 Mica: mostly opaque luminous mineral-white surface with subtle diffuse wallpaper colors and fine grain, not transparent glass, no neon glow. Do NOT obscure it with giant opaque panels. Cool slate/navy text, restrained mint/teal accent, Segoe UI typography, 8px corner radius controls, elegant spacing. Titlebar and main backdrop form one continuous surface.
Navigation rail: small brand icon and "MailMeUp" at top, understated tagline "All your inboxes.\nOne conversation." Four compact steps with fine line icons and short labels: "Welcome", "Accounts", "Sharing", "Connect to Codex". Welcome is active with a thin teal indicator and faint mint rounded selection; remaining steps quiet. Near bottom, a small tasteful dimensional paper-envelope/calendar background vignette, muted midnight blue, warm ivory and mint, fading naturally into the Mica, not inside a card. Very bottom: shield-outline icon with "Read-only by design", then two compact text actions "Privacy & terms" and "About & support". No long legal block.
Main content composition for WELCOME: spacious hero across upper two-thirds. Left two-thirds has small eyebrow "LET'S GET YOU SET UP", a large high-quality clear heading on two lines: "A little setup.\nA clearer day." Under heading, two short lines only: "Bring your mail and calendars into Codex.\nChoose what your assistant can read." Below, accurately colored Google G symbol and Microsoft four-color square symbol, at 22px scale, followed by "Google & Microsoft". In the right third, a refined original 3D paper illustration: ivory envelopes and a small calendar gently converging toward one mint conversation bubble, no arrows, high-quality soft studio highlights, muted navy/mint/ivory/coral palette, abundant breathing space. This illustration is integrated with the background and subtle shadows; no boxed stock photograph.
Under hero: one horizontal row of three compact icon-and-label benefits, NOT big cards: envelope outline "Mail & calendars"; stacked accounts outline "Multiple accounts"; shield outline "Read-only access".
Below that a very compact disclosure row: chevron-right and "How sharing works". Always-visible short privacy sentence in readable muted slate: "Requested content may reach your AI service. Sign-in tokens stay on this device." It must be legible, not microscopic.
Fixed footer within window, subtle horizontal divider: far left small "Windows preview"; far right one primary dark teal filled button with white type "Get started" and a small right arrow. No Back on first step. Footer always in frame.
Constraints: screenshot-quality UI, all specified text spelled correctly in English, no scrollbar, no clipped content, no long paragraphs, no nested decorative cards, no mobile frame, no fake email bodies or private data, no extra controls, no watermark. Use accurate original multicolor Google and Microsoft marks with clear space, never recolor or stylize them. Native crisp typography and realistic control sizes, modern and quiet rather than flat or sterile.
```

## Accounts

Reference: the generated Welcome rendering, keeping the same shell and material.

```text
Use case: ui-mockup
Asset type: a high-fidelity proposed MailMeUp Windows 11 setup wizard screen.
Input image: the supplied Welcome rendering is the exact visual and layout reference. Render a NEW screen of this SAME application. Keep the exact same landscape image dimensions, whole front-facing app window, light Mica surface, typography family, colors, navigation rail width, titlebar, native window controls, borders, footer, and bottom-left envelope/calendar vignette. Keep the MailMeUp brand icon recognizable. Reference is design artwork, not live state.
Materials: luminous Windows 11 Mica, faint diffuse mineral-white/mint/slate wallpaper coloration, very subtle grain, native crisp Segoe-style text. Do not cover the entire main background with an opaque panel. Maintain the reference's refinement, whitespace, dimensional illustration and quiet warmth. Muted navy text and dark teal actions.
Critical owner requirement: NEVER number the wizard steps. No numbered circles, step numbers, percentages or numeric progress counters. Progress is conveyed visually through connected navigation icons, thin vertical progress line, small teal completion checkmarks on past stages, active mint selection and muted upcoming stages. The line should be discreet and must not cross the icon shapes or text. Navigation labels exactly: "Welcome", "Accounts", "Sharing", "Connect to Codex".
Persistent lower rail exactly like reference: shield outline plus "Read-only by design", actions "Privacy & terms" and "About & support". Keep concise text and realistic readable desktop control sizes.
All email examples MUST be the synthetic example.test addresses given below. No personal account details, no real messages, no placeholder garbled text. Google uses accurate multicolor G and Microsoft the accurate four-color square mark, not generic envelopes. No scrollbars, clipped text, long paragraphs, unnecessary cards or large blocks of explanations. Do not label anything "Step". One screen only, no collage, no exterior caption or watermark. Essential information stays visible; further controls appear only when relevant.

Main content: ACCOUNTS.
Progress rail: Welcome completed with a teal checkmark, Accounts active; Sharing and Connect to Codex upcoming.
At main upper left, heading "Connect your accounts", beneath it one line "Add personal, work or client accounts."
Below, a horizontally balanced row of two polished provider connection controls: multicolor Google G plus "Connect Google", and Microsoft four-color square plus "Connect Microsoft". Use native white/translucent surfaces, thin borders and small restrained shadows, not giant cards. Both buttons are clearly actionable.
Directly under buttons, compact permission selector line: "Request read access to" then checked options "Mail" and "Calendars".
Then one slim disclosure line with a small settings outline icon, text "Provider setup", status "Configured" and right chevron. Subtext, one concise line only: "Your own app registration is required on first use." The provider-setup instructions are not expanded.
Lower main content: section "Connected on this device" with a small account-count label "5 accounts". Show five COMPACT horizontal rows with subtle separators, not five giant cards. Each row has a 22px provider logo on left, semibold email, and status on right:
Google / alex@example.test / "Sharing off"
Microsoft / work@example.test / "Sharing off"
Google / client@example.test / "Sharing off"
Microsoft / studio@example.test / "Sharing off"
Google / team@example.test / "Sharing off"
No toggles on this screen because sharing is configured in the following stage.
Under the rows one short note with a small shield icon: "New accounts start with sharing off."
Composition: every row and control comfortably fits above fixed footer. The content is one organized column, with small subdued ivory-envelope shapes fading into the far upper-right backdrop, avoiding the controls. The large Welcome hero illustration is replaced by this very quiet decorative detail.
Footer: left button with arrow "Back"; primary filled dark teal button at right "Choose sharing" with right arrow.
```

## Sharing

Reference: the generated Welcome rendering, keeping the same shell and material.

```text
Use case: ui-mockup
Asset type: a high-fidelity proposed MailMeUp Windows 11 setup wizard screen.
Input image: the supplied Welcome rendering is the exact visual and layout reference. Render a NEW screen of this SAME application. Keep the exact same landscape image dimensions, whole front-facing app window, light Mica surface, typography family, colors, navigation rail width, titlebar, native window controls, borders, footer, and bottom-left envelope/calendar vignette. Keep the MailMeUp brand icon recognizable. Reference is design artwork, not live state.
Materials: luminous Windows 11 Mica, faint diffuse mineral-white/mint/slate wallpaper coloration, very subtle grain, native crisp Segoe-style text. Do not cover the entire main background with an opaque panel. Maintain the reference's refinement, whitespace, dimensional illustration and quiet warmth. Muted navy text and dark teal actions.
Critical owner requirement: NEVER number the wizard steps. No numbered circles, step numbers, percentages or numeric progress counters. Progress is conveyed visually through connected navigation icons, thin vertical progress line, small teal completion checkmarks on past stages, active mint selection and muted upcoming stages. The line should be discreet and must not cross the icon shapes or text. Navigation labels exactly: "Welcome", "Accounts", "Sharing", "Connect to Codex".
Persistent lower rail exactly like reference: shield outline plus "Read-only by design", actions "Privacy & terms" and "About & support". Keep concise text and realistic readable desktop control sizes.
All email examples MUST be the synthetic example.test addresses given below. No personal account details, no real messages, no placeholder garbled text. Google uses accurate multicolor G and Microsoft the accurate four-color square mark, not generic envelopes. No scrollbars, clipped text, long paragraphs, unnecessary cards or large blocks of explanations. Do not label anything "Step". One screen only, no collage, no exterior caption or watermark. Essential information stays visible; further controls appear only when relevant.

Main content: SHARING with a selected account detail area. This screen must demonstrate progressive disclosure clearly.
Progress rail: Welcome and Accounts completed with teal checkmarks; Sharing active; Connect to Codex upcoming.
At main upper left, heading "Choose what to share", followed by "Select an account, then choose what Codex can read."
Below, split the MAIN area into a compact account list on the left (about 42 percent of main width) and a selected account editor on the right (58 percent); a subtle vertical divider separates them. This is in addition to the existing application navigation rail.
List title "Your accounts". Five dense tidy rows, each 60px high approximately, with provider logo, address and small status:
Google / alex@example.test / "Shared"
Microsoft / work@example.test / "Sharing off" -- SELECTED with faint mint surface and an active left indicator
Google / client@example.test / "Sharing off"
Microsoft / studio@example.test / "Sharing off"
Google / team@example.test / "Sharing off"
Small right chevron for each account, no toggle in the list.
Selected editor: Microsoft four-color mark alongside bold "work@example.test". Show a compact "Unsaved changes" amber text indicator.
At top of settings, "Share this account" with teal toggle On. Short one-line description "Controls future reads by your assistant."
After a subtle divider: envelope outline icon with label "Mail" and teal toggle On; calendar outline icon with label "Calendars" and teal toggle On. These controls are revealed because Share this account is enabled.
Below Calendars, one closed selector with exact text "All current and future calendars" and down chevron. Under it, small secondary link "Choose individual calendars". Do not show a full calendar list yet.
At bottom of selected editor, a clear native dark teal button "Save choices". Because choices are unsaved, the account list still accurately reads "Sharing off" for this selected account.
Under the two-column content and above the footer, a short note "Changes apply to future reads. Existing conversations keep what was already shared." Then a small collapsed disclosure action "What reaches your assistant" with chevron.
Quiet decorative background at far upper-right only, no illustrations behind settings.
Footer fixed: "Back" left; primary dark teal "Connect to Codex" at right with arrow. No enormous decorative cards; the single account editor is an open region on Mica with whitespace.
```

## Connect to Codex

Reference: the generated Welcome rendering, keeping the same shell and material.

```text
Use case: ui-mockup
Asset type: a high-fidelity proposed MailMeUp Windows 11 setup wizard screen.
Input image: the supplied Welcome rendering is the exact visual and layout reference. Render a NEW screen of this SAME application. Keep the exact same landscape image dimensions, whole front-facing app window, light Mica surface, typography family, colors, navigation rail width, titlebar, native window controls, borders, footer, and bottom-left envelope/calendar vignette. Keep the MailMeUp brand icon recognizable. Reference is design artwork, not live state.
Materials: luminous Windows 11 Mica, faint diffuse mineral-white/mint/slate wallpaper coloration, very subtle grain, native crisp Segoe-style text. Do not cover the entire main background with an opaque panel. Maintain the reference's refinement, whitespace, dimensional illustration and quiet warmth. Muted navy text and dark teal actions.
Critical owner requirement: NEVER number the wizard steps. No numbered circles, step numbers, percentages or numeric progress counters. Progress is conveyed visually through connected navigation icons, thin vertical progress line, small teal completion checkmarks on past stages, active mint selection and muted upcoming stages. The line should be discreet and must not cross the icon shapes or text. Navigation labels exactly: "Welcome", "Accounts", "Sharing", "Connect to Codex".
Persistent lower rail exactly like reference: shield outline plus "Read-only by design", actions "Privacy & terms" and "About & support". Keep concise text and realistic readable desktop control sizes.
All email examples MUST be the synthetic example.test addresses given below. No personal account details, no real messages, no placeholder garbled text. Google uses accurate multicolor G and Microsoft the accurate four-color square mark, not generic envelopes. No scrollbars, clipped text, long paragraphs, unnecessary cards or large blocks of explanations. Do not label anything "Step". One screen only, no collage, no exterior caption or watermark. Essential information stays visible; further controls appear only when relevant.

Main content: CONNECT TO CODEX, before installation, with honest actionable status. Do not show a successful connection or green completion banner.
Progress rail: Welcome, Accounts and Sharing completed with teal checks; Connect to Codex active. No step numbers.
At main upper left, heading "Connect to Codex", beneath it one sentence "Install the local plugin to make MailMeUp available in Codex."
Upper-right background vignette: small dimensional ivory envelope and mint conversation bubble with subtle soft navy brackets, tasteful and secondary, not a huge hero.
Below, a compact visual connection diagram: small MailMeUp app icon with label "MailMeUp", a fine dotted connection line, and a simple dark native terminal-style icon with label "Codex". The dotted link indicates a pending connection. No OpenAI logo invention.
Below diagram, one contained translucent status surface, 60 percent main width approximately:
small amber status dot and label "Plugin not installed"
one short line "Ready for local installation."
a primary dark teal button "Install local plugin", alongside a secondary text-style button with refresh icon "Refresh status".
This is a synthetic illustrative state, not evidence of actual local configuration.
Under status surface, show a compact read-only summary "Sharing: 1 account" with small envelope and calendar outline icons and text "Mail & calendars". Secondary action "Review sharing" beside it.
Below summary, show the next action guidance as one restrained info row: information-outline icon and exactly "After installation, reload Codex and ask MailMeUp for its status."
Then a CLOSED disclosure row with chevron "Installation details & manual setup". No command text, no technical file paths, no setup wall of text.
Footer fixed: left secondary "Back"; right understated button "Close setup". Installation is the main primary action within the status area; Close setup is NOT a bright completion CTA. No "All done", "Connected" or "You're ready" because loaded tools have not been confirmed.
All content must fit comfortably with ample whitespace, no scrolling.
```

