# Windows UI implementation

WinUI 3 configures the Python engine through a local process boundary. Keep mail behavior in Python; the UI collects settings, shows results and obtains direct user confirmation.

This source change has not been built or visually validated.

## Account editor

Keep incoming and outgoing settings together. Offer presets and a custom option, an IMAP/POP3 selector before incoming fields, and the required host, port, TLS and username fields.

Password or app password is the default. Explicit Microsoft OAuth2 shows a public client ID and tenant field, a device-code dialog and a human-triggered browser button. Never switch methods or open a browser automatically. Microsoft presets are editable settings, not proof of account compatibility. No Google OAuth path is included.

POP3 fixes the folder to INBOX and explains that server folders and unread flags are unavailable. Its search uses headers and email Date values. SMTP is optional and initially disabled. TLS and hostname/certificate checks are required, without plaintext or custom-CA controls.

Separate read checks, SMTP connection checks and test-message preparation. A connection check is not delivery.

## Approval

Show the complete pending message, including every recipient, body and attachment. Approval applies once to those exact contents. Cancel and close leave it unsent.

MCP cannot approve a message. A UI label or assistant-controlled boolean must never replace the engine's approval enforcement.

## Presentation

Use native controls, clear labels and a compact layout. Keep errors close to the failed field or action. Preserve keyboard access and scrolling at small window sizes. Hover must not move controls.

Keep decorative assets separate from controls. Asset provenance is in the [brand guide](BRAND.md); old screenshots do not validate this UI.
