# Brand guide

## Identity

Name: **AgentInbox**. Preserve capitalization; do not use spaces in the product name. MCP server identifier: `agentinbox`.

Tagline: **Read and send email with your AI assistant.**

One-line description: **Read email through IMAP or POP3 and send requested messages through optional SMTP, with your AI assistant.**

Expanded product description: **Connect your email accounts in the Windows app, then read and send email through Codex. Gmail and iCloud need a separate password for this app. For Outlook and Microsoft 365, choose Sign in with Microsoft. You choose which accounts Codex can read and send from. Your email provider may limit access.**

The name joins an AI agent with the inboxes it can read. Envelopes and a shared mint palette connect the product to the MeUp family.

## Writing style

Write as if you're helping a friend try the app. Start with what they want to do, then explain the next step. Use short sentences, everyday words and natural contractions.

The first README headline must explain what AgentInbox does in one short sentence. Local MCP clients are connection options, not proof of tested compatibility. Keep the current implementation's unvalidated status nearby. Multiple accounts does not imply unlimited or tested capacity.

Use "I", "me", and "my" when speaking as Umberto, the solo maintainer with help from a few contributors. Speak to the reader as "you". Avoid company-style "we", slogans that replace an explanation, hype, and formulaic AI wording. Apply this voice to all original documentation, including setup, support, and contributor guides.

Say "connect your accounts" instead of "configure multi-account access," and "choose what to share" instead of "manage sharing capabilities." Keep exact command names and button labels. Explain IMAP, POP3, SMTP and MCP when the reader first needs them.

In setup, say **Sign in with Microsoft**, **Open Microsoft**, and **Enter this code**. Avoid OAuth, web authorization, authentication, tenant and credentials in everyday instructions. Explain a provider's special password before using its official name. Keep technical server replies below the plain-language error, with private sign-in values hidden.

Use **Email address for reading** and **Email address for sending**, including in validation messages. Tell the reader which field or tab needs attention. Keep `username` as an internal protocol key; do not rename stored settings or change accepted sign-in values just to update the wording.

Be clear about what works, what's still being tried and what hasn't been tested. Keep privacy information close to the action it affects. Put build history and implementation details in the developer references. Label concept artwork and keep its source prompts and creation notes.

## Visual vocabulary

Use the [shared MeUp style](assets/meup/README.md) for the current README presentation: ink navy, ivory forms, and a mint accent. That guide contains the current illustration and its exact generation prompt. The earlier artwork and palette below remain part of the asset history.

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
- `resources/agentinbox-about-banner.png`: 2172 by 724 pixel banner for the Windows app's About & Support dialog, generated with the built-in image generation tool on September 6, 2026. Its calm left area is reserved for accessible native title text; the illustration contains no embedded text or vendor marks.
- `assets/branding/agentinbox-icon.svg`: editable vector envelope mark.
- `assets/branding/agentinbox-app-icon-source.png`: large raster app-icon artwork.
- `assets/branding/agentinbox-app-icon-256.png`: compact app-icon variant.
- `assets/branding/agentinbox-chat-search.png`: historical conversation screenshot; not used to claim current behavior.
- `assets/branding/agentinbox-task-manager-lightweight.png`: historical Task Manager screenshot; not evidence of current runtime memory use.

Keep the concept caption whenever an image could be mistaken for a shipping UI. Do not promise offline processing, tested providers or validated credential protection without evidence. Product names do not imply endorsement or ownership of another company's marks.

Project-authored assets are distributed with the repository under MIT. This does not grant rights to third-party trademarks or imply that AI-generated artwork has exclusive rights in every jurisdiction.

## VibeWare participation

AgentInbox participates in **VibeWare**, the open initiative created by Umberto Giacobbi for software built with AI under human direction and responsibility. VibeWare is not a separate author, publisher, development team, license or quality certification. Keep AgentInbox's own name, application icon and authorship visible.

Use the exact tagline: **Human intent, AI execution, and plenty of tokens**. Do not claim that Umberto coined the term, or that the mark proves every change has been tested or reviewed. The project's [validation record](VALIDATION.md) describes its actual checks.

The approved logo is [resources/vibeware/vibeware-logo.png](../resources/vibeware/vibeware-logo.png), copied unchanged from `Branding/vibeware-logo.png` in the [VibeWare repository](https://github.com/umbertotechnopreneur/VibeWare). It is a 1254 × 1254 transparent PNG, SHA-256 `84321fe9f77ccc083813e02c02ff3716ad50a45ed000b8663c5a2d416e2ba5af`. Preserve the original pixels, dimensions, denim blue, cream, aspect ratio and transparent canvas. Do not crop, stretch, recolor, add effects or substitute a stock floppy icon. The [original MIT notice](../resources/vibeware/LICENSE) accompanies the asset and is included in the desktop output.

For AgentInbox's configuration window, the owner requested the initiative mark on the right of the footer, with the personal website, Terms and Privacy on the left. Use the approved image on the right of a keyboard-accessible link with **This is VibeWare** on the first line and **Manifesto** on the second. Its tooltip explains human direction and responsibility. Keep links available and put the mark on a second row on narrow windows. This placement follows the owner's app-specific request; do not add the central template's timed fading or About window as incidental branding work.

The canonical destination is the [VibeWare manifesto](https://umbertogiacobbi.biz/vibeware/manifesto). The source guidance is the initiative's [press kit](https://github.com/umbertotechnopreneur/VibeWare/blob/main/PRESS-KIT.md) and [app publication template](https://github.com/umbertotechnopreneur/VibeWare/blob/main/templates/APP-PUBLICATION.md). MIT reuse does not imply trademark rights, endorsement or certification.

## Historical About & Support banner provenance

Tool mode: built-in image generation. Original standalone artwork; no reference image was supplied. The existing application icon was inspected for context and remains unchanged.

```text
Use case: stylized-concept
Asset type: wide product banner for the English-only AgentInbox Windows desktop app About & Support dialog.
Primary request: Create a polished premium digital illustration that expresses multiple email inboxes and calendars coming together in one helpful AI conversation.
Scene/backdrop: deep midnight navy #071525, smooth subtle atmospheric gradient, no frame, a quiet refined desktop-app aesthetic.
Subject: on the center-right, three small dimensional ivory envelopes and one minimal ivory calendar card flowing along delicate mint connection curves into one larger rounded mint conversation bubble, with a subtle small coral accent. The cards should feel clean, friendly and tactile, like softly beveled paper objects, with crisp edge highlights and restrained natural shadows. Calendar has only a small grid of blank rounded cells, no dates or letters. Conversation bubble has three ivory dots, no robot face. Avoid arrows implying email sending; flowing undirected lines express reading and gathering.
Composition/framing: panoramic 3:1 landscape image, ideally 1536 by 512. Left 38 percent remains calm midnight negative space for a native title overlay, while illustration occupies center-right. Keep all objects comfortably inside image safe margins. Sophisticated spacious composition, readable at 600 by 200 pixels.
Lighting/mood: soft studio illumination, subtle mint glow, welcoming trustworthy calm, high quality dimensional illustration.
Color palette: midnight #071525, mint #71DEB7, warm ivory #F5EADB, coral #FF8F79, quiet slate #6E879E.
Constraints: original standalone artwork. No text, letters, words, logos, vendor marks, GitHub marks, watermark, borders, screenshots, button shapes, padlocks or shields. No busy particles, no people. The image must look finished without text.
```

## Historical screenshot provenance

The two README screenshots were edited with the built-in image generation tool on September 22, 2026. Their previous MailMeUp versions were the edit targets. The edits changed only the product, process and executable names; account data remains synthetic or hidden. Both results were visually reviewed before use.

```text
Use case: text-localization. Edit the supplied conversation screenshot by replacing only “MailMeUp 0.1.1” with “AgentInbox 0.1.1”. Preserve the original composition, perspective, typography, colors, UI content, synthetic addresses and all other text. Do not add a logo, watermark or decoration.
```

```text
Use case: text-localization. Edit the supplied Task Manager screenshot by replacing “MailMeUp CLI (10)” with “AgentInbox CLI (10)”, every visible process name “mailmeup” with “agentinbox”, and every visible “mailmeup.exe” with “agentinbox.exe”. Preserve the Italian UI, row count, PIDs, memory values, highlighting, privacy blur and all other content. Do not add a logo, watermark or decoration.
```
