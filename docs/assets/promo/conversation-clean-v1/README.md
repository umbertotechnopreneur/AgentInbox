# Earlier AgentInbox conversation variants

These earlier examples are retained as design references. The selected conversation used by the README is in [conversation-clean-v2](../conversation-clean-v2/README.md).

Use a plain native conversation treatment: the original dark Codex background, the AgentInbox mention icon and label, a question on the right and ordinary assistant prose on the left. Keep account identifiers out of public artwork. Do not add illustrated backgrounds, glow, metric cards, avatars or invented application controls.

| File | Dimensions | Use |
| --- | --- | --- |
| `conversation-full.png` | 1776 × 886 | Full English response |
| `conversation-compact.png` | 1991 × 790 | Earlier compact response, superseded by v2 |

These are edited English promotional examples based on owner-supplied Codex captures, made with the built-in ImageGen tool. They are not pixel-exact, unedited captures or a claim about the owner's current mail. The example uses five connected mailboxes, three new unread messages in shared inboxes, and one mailbox left untouched because it is not shared for reading. The samples do not claim continuous background monitoring. Captions explaining the illustrative data belong outside the image.

The compact variant was prepared for an earlier website hero treatment. Use the selected v2 image for current presentation. Keep the original owner captures private. These alternatives do not replace the existing app/initiative master artwork.

Project-authored presentation material follows the repository's MIT license to the extent rights apply. Codex and other third-party marks remain their owners' marks; no vendor endorsement is implied.

These text/UI assets are not Microsoft Store **Super hero art** submissions. That field requires text-free artwork at its prescribed dimensions; no Store upload or certification is claimed here. See [Microsoft's asset guidance](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images).

## English copy

Question: “AgentInbox, how many mailboxes are connected, and do I have any new messages?”

Full answer: “You have 5 connected mailboxes. I found 3 new unread messages in the shared inboxes.

One mailbox isn’t shared for reading, so I left it untouched.”

Compact answer: “5 connected mailboxes. 3 new unread messages in the shared inboxes.

One mailbox isn’t shared for reading, so I left it untouched.”

## Final prompt set

Tool mode: built-in ImageGen edit mode. No CLI/API fallback or Python image editing was used. Inputs were the owner-supplied native question crop and full conversation reference. Rejected decorative concepts are not used in the repository or website.

### Full version

```text
STRICT SCREENSHOT CLEANUP / ENGLISH TEXT EDIT. Image 1 is the exact native Codex question-bubble style to preserve; image 2 shows the same native chat's plain assistant text style. Make a clean cropped screenshot of ONLY a user question and assistant answer, like the supplied real Codex interface. Solid original very dark grey-purple background (#1e1e2e approximately), one native dark-grey user bubble at the upper right, plain left-aligned assistant text below. Use the same ordinary monospaced interface font and muted light-grey text as the originals. Preserve the actual small green AgentInbox mention icon and blue-grey 'AgentInbox' mention label shown in image 1; do not replace it with a new logo or avatar. No window header, no 'Codex' title, no outer card or frame, no border decoration, no avatars for either person, no gradients, no bubbles in the background, no glow, no neon, no shadows, no CRT texture, no perspective, no metric chips, no badges, no illustrated envelopes, no footer or added UI. This must look like a normal real cropped chat screenshot, absolutely not a designed marketing graphic. Replace the question wording after the native AgentInbox mention with: ', how many mailboxes are connected, and do I have any new messages?' The complete visible question should read 'AgentInbox, how many mailboxes are connected, and do I have any new messages?' On the LEFT underneath, as ordinary assistant prose with natural line breaks and comfortable spacing, write EXACTLY: 'You have 5 connected mailboxes. I found 3 new unread messages in the shared inboxes.' Then one paragraph break, then EXACTLY: 'One mailbox isn’t shared for reading, so I left it untouched.' The numbers are an illustrative example supplied by the owner. Do not add an illustrative-example label inside the screenshot; that caption will be outside the image in Markdown. Do not include any other original transcript, Italian text, tool activity, commands, account identifiers, file-change counts or composer. Keep the composition compact, about 1404 x 700 pixels in aspect ratio, with large enough native text to read in a README. Do not invent any UI or turn the response into a bubble.
```

### Compact version

```text
Make a SECOND CLEAN, COMPACT CROP variant of the supplied native-looking Codex conversation screenshot. Preserve the same plain dark grey-purple background, native monospaced typography, muted grey text, original small green AgentInbox mention icon and blue-grey AgentInbox mention label. Keep the user question bubble aligned right and the assistant answer as ordinary left-aligned prose, NOT a bubble. Do not add any UI, avatars, header, window chrome, logos, badges, metrics, colored background, gradients, glow, shadow, texture, illustration or perspective. Remove excessive empty space above and below so this is a tightly framed screenshot suitable for a README or a website hero. Use the same complete question: 'AgentInbox, how many mailboxes are connected, and do I have any new messages?' For this compact wording variant, the answer must be exactly two short paragraphs: '5 connected mailboxes. 3 new unread messages in the shared inboxes.' Then 'One mailbox isn’t shared for reading, so I left it untouched.' Retain comfortable natural native UI spacing and crisp readable text. These are illustrative numbers for marketing; do not add any extra claim or label inside the screenshot. The only change from the reference should be the shorter answer wording and tighter cropping, no visual redesign.
```
