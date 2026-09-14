# Brand guide

## Identity

Name: **MailMeUp**. Preserve capitalization; do not use spaces in the product name. Executable and MCP server identifier: `mailmeup`.

Tagline: **All your inboxes. One conversation.**

One-line description: **Search your email accounts from one conversation with your AI assistant.**

Expanded product description: **Find mail and check appointments across your Google and Microsoft accounts. MailMeUp runs on your computer and only reads what you choose to share.**

The name follows the same personal style as PromptMeUp and TrackMeUp. The artwork brings several envelopes together into one conversation.

## Writing style

Write as if you're helping a friend try the app. Start with what they want to do, then explain the next step. Use short sentences, everyday words and natural contractions.

Say "connect your accounts" instead of "configure multi-account access," and "choose what to share" instead of "manage sharing capabilities." Keep exact command names and button labels. Explain terms such as MCP or Client ID when the reader first needs them.

Be clear about what works, what's still being tried and what hasn't been tested. Keep privacy information close to the action it affects. Put build history and implementation details in the developer references. Label concept artwork and keep its source prompts and creation notes.

## Visual vocabulary

| Role | Color |
| --- | --- |
| Midnight background | `#071525` |
| Mint signal / primary accent | `#71DEB7` |
| Warm ivory | `#F5EADB` |
| Coral detail | `#FF8F79` |
| Quiet slate | `#6E879E` |

Use restrained dimensional envelopes, generous empty space and clear connection lines. The hero uses an editorial serif wordmark with simple supporting typography. Keep diagrams and code documentation highly legible. A geometric envelope SVG is provided for compact technical uses; generated illustrations are conceptual artwork rather than the canonical icon source.

## Assets

- `resources/wizard/hero.png` and `resources/wizard/rail.png`: generated cutouts for the approved Mica wizard. [Final prompts and provenance](../resources/wizard/GENERATION.md).
- `resources/providers/google.svg` and `resources/providers/microsoft.svg`: original-color provider vectors downloaded from SVG Logos CDN and bundled locally. [Sources](../resources/providers/README.md).
- `resources/mailmeup-about-banner.png`: 2172 by 724 pixel banner for the Windows app's About & Support dialog, generated with the built-in image generation tool on September 6, 2026. Its calm left area is reserved for accessible native title text; the illustration contains no embedded text or vendor marks.
- `assets/branding/mailmeup-hero.png`: generated README/product hero.
- `assets/branding/mailmeup-concept.png`: generated planned architecture illustration.
- `assets/branding/mailmeup-setup-flow-welcome-accounts.png` and `assets/branding/mailmeup-setup-flow-sharing-codex.png`: two generated README concepts that present the four-screen Windows setup flow. They are not shipping UI screenshots. [Final prompts and provenance](assets/branding/GENERATION.md).
- `assets/branding/mailmeup-icon.svg`: editable vector envelope mark.
- `assets/branding/mailmeup-app-icon-source.png`: large raster app-icon artwork.
- `assets/branding/mailmeup-app-icon-256.png`: compact app-icon variant.
- `assets/branding/GENERATION.md`: final prompts, tool mode and provenance.

Keep the concept caption whenever an image could be mistaken for a shipping UI. Do not promise offline processing, supported providers or secure token storage before those features ship. Product names and any indicative provider symbols identify compatibility, not endorsement or ownership of another company's marks.

Project-authored assets are distributed with the repository under MIT. This does not grant rights to third-party trademarks or imply that AI-generated artwork has exclusive rights in every jurisdiction.

## About & Support banner prompt

Tool mode: built-in image generation. Original standalone artwork; no reference image was supplied. The existing application icon was inspected for context and remains unchanged.

```text
Use case: stylized-concept
Asset type: wide product banner for the English-only MailMeUp Windows desktop app About & Support dialog.
Primary request: Create a polished premium digital illustration that expresses multiple email inboxes and calendars coming together in one helpful AI conversation.
Scene/backdrop: deep midnight navy #071525, smooth subtle atmospheric gradient, no frame, a quiet refined desktop-app aesthetic.
Subject: on the center-right, three small dimensional ivory envelopes and one minimal ivory calendar card flowing along delicate mint connection curves into one larger rounded mint conversation bubble, with a subtle small coral accent. The cards should feel clean, friendly and tactile, like softly beveled paper objects, with crisp edge highlights and restrained natural shadows. Calendar has only a small grid of blank rounded cells, no dates or letters. Conversation bubble has three ivory dots, no robot face. Avoid arrows implying email sending; flowing undirected lines express reading and gathering.
Composition/framing: panoramic 3:1 landscape image, ideally 1536 by 512. Left 38 percent remains calm midnight negative space for a native title overlay, while illustration occupies center-right. Keep all objects comfortably inside image safe margins. Sophisticated spacious composition, readable at 600 by 200 pixels.
Lighting/mood: soft studio illumination, subtle mint glow, welcoming trustworthy calm, high quality dimensional illustration.
Color palette: midnight #071525, mint #71DEB7, warm ivory #F5EADB, coral #FF8F79, quiet slate #6E879E.
Constraints: original standalone artwork. No text, letters, words, logos, vendor marks, GitHub marks, watermark, borders, screenshots, button shapes, padlocks or shields. No busy particles, no people. The image must look finished without text.
```
