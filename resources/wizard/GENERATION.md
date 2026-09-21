# Wizard artwork

Generated with the built-in image_gen tool on 2026-09-06, using an approved Welcome rendering as the style reference. That design exploration is archived privately; the shipped cutouts and their provenance remain here.

Final project assets:

- [hero.png](hero.png): envelope, calendar and conversation illustration.
- [rail.png](rail.png): compact envelope/calendar vignette.

Both final assets use background extraction after the initial illustrations contained a painted checkerboard. Only the final cutouts are included in the application package. The wizard uses native text, controls and separate provider SVGs; these illustrations contain no UI copy or vendor logos.

## Hero generation prompt

```text
Use case: compositing
Asset type: transparent PNG illustration to ship inside the approved AgentInbox WinUI 3 desktop wizard.
Input image: approved Welcome UI mockup, visual style reference only. Extract/recreate ONLY its upper-right dimensional illustration as a standalone original asset. Do not include any part of the UI.
Subject: two warm ivory dimensional paper envelopes, a small ivory desk-calendar card with muted navy binding and blank pastel grid squares, and one mint/aqua conversation bubble with three ivory dots. A few translucent pale mint and dusty blue organic arcs softly connect the composition without arrows.
Style: match the approved reference exactly: refined tactile paper and ceramic materials, soft studio highlights, restrained soft shadows, subtle navy/ivory/mint palette and tiny coral calendar accent. Calm, modern and premium, no neon, no glossy tech clutter.
Composition: landscape about 5:4, asset intended to be displayed at 390 by 310 pixels. All objects fully inside safe margins, envelope upper left, conversation bubble right, calendar lower center. Keep airy negative space, preserve proportions and actual object sharpness.
CRITICAL: genuinely transparent alpha background everywhere outside the illustration, including corners; no white canvas, no grey checkerboard painted into the image, no opaque gradient backdrop, no drop-shadow rectangle. Soft object shadows and semi-transparent arcs are allowed. No text, numbers, logos, UI controls, screenshot frame or watermark.
```

## Navigation illustration generation prompt

```text
Use case: compositing
Asset type: transparent PNG decorative navigation-rail vignette to ship inside the approved AgentInbox WinUI 3 desktop wizard.
Input image: approved Welcome UI mockup, visual style reference only. Recreate ONLY its bottom-left paper envelope/calendar vignette as a standalone original asset. Do not include the UI or typography.
Subject: a small compact arrangement of a muted navy envelope at back left, two warm ivory paper envelopes, one ivory calendar with dusty blue binding and blank pastel grid squares in front, and two restrained mint paper leaves. No conversation bubble.
Style: same tactile dimensional paper, soft studio illumination, subtle ambient shadows, muted blue/ivory/mint palette as the reference. Harmonize with both light and dark Mica surfaces.
Composition: approximately square, all objects contained with 8 percent margin. Intended display size 190 by 180 pixels. Composition weight near lower-left but nothing clipped. Crisp, calm, decorative but subtle.
CRITICAL: genuinely transparent alpha background everywhere outside objects. No opaque white or grey canvas, no artificial checkerboard, no gradient background rectangle. Soft semi-transparent object shadows allowed. No words, dates, letters, numerals, logos, controls, borders, UI or watermark.
```

## Final background extraction prompt

Applied separately to each initial illustration with the built-in image_gen tool:

```text
Use case: background-extraction. Edit target: the attached illustration. Remove the entire painted grey-and-white checkerboard background. Deliver a true transparent RGBA PNG cutout with alpha zero outside the objects, not a visualization of transparency. The checkerboard in this source is an error, not part of the artwork. Preserve the paper envelopes, calendar, mint elements, object shapes, materials, colors and composition exactly. Remove any checkerboard showing through translucent arcs as well. Keep antialiased edges. No opaque canvas, no drawn checkerboard, no UI, no labels, no words. Background must be genuinely transparent.
```
