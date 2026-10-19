# Windows UI implementation

WinUI 3 configures the Python engine through a local process boundary. Keep mail behavior in Python; the UI collects settings, shows results and obtains direct user confirmation.

This source change has not been built or visually validated.

## Account editor

Keep incoming and outgoing settings together. Offer presets and a custom option, then show the required host, port, TLS, username and password fields.

Incoming mail uses IMAP. SMTP is optional and initially disabled. Presets requiring an unsupported sign-in method stop early with an explanation.

Separate read checks, SMTP connection checks and test-message preparation. A connection check is not delivery.

## Approval

Show the complete pending message, including every recipient, body and attachment. Approval applies once to those exact contents. Cancel and close leave it unsent.

MCP cannot approve a message. A UI label or assistant-controlled boolean must never replace the engine's approval enforcement.

## Presentation

Use native controls, clear labels and a compact layout. Keep errors close to the failed field or action. Preserve keyboard access and scrolling at small window sizes. Hover must not move controls.

Keep decorative assets separate from controls. Asset provenance is in the [brand guide](BRAND.md); old screenshots do not validate this UI.
