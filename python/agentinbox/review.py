"""Local human review; no approval capability is exposed through MCP."""

import json
import secrets
import sys
import time

from .errors import MailError


def pipe_review(application, request_id: str, write):
    """Review over private redirected handles owned by the Windows setup app."""
    record, frozen = application.review_snapshot(request_id)
    challenge = secrets.token_urlsafe(32)
    public = {key: value for key, value in frozen.items() if key not in {"mime", "recipients"}}
    write({"ok": True, "result": {"request_id": request_id, "challenge": challenge, "expires": record["expires"], **public}})
    raw = sys.stdin.buffer.readline(4097)
    if not raw:
        return 0
    if len(raw) > 4096:
        raise MailError("invalid_approval", "The review response is invalid.")
    response = json.loads(raw)
    if not isinstance(response, dict) or set(response) != {"challenge", "approved"} or not isinstance(response["challenge"], str):
        raise MailError("invalid_approval", "The review response is invalid.")
    if not secrets.compare_digest(challenge, response["challenge"]) or type(response["approved"]) is not bool:
        raise MailError("invalid_approval", "This response does not belong to the current review.")
    if time.time() >= record["expires"]:
        raise MailError("approval_expired", "The review expired. Prepare a new message.")
    result = application.send_reviewed(record, frozen, response["approved"])
    write({"ok": True, "result": result})
    return 0


def desktop_review(application, request_id: str):
    """Offer a direct local review window on source-based desktop installations."""
    try:
        import tkinter as tk
        from tkinter import messagebox, scrolledtext
        window = tk.Tk()
    except Exception as error:
        raise MailError("review_unavailable", "Sending needs a local review window. Installations without tkinter or a desktop session cannot approve mail.") from error
    window.title("AgentInbox — Review outgoing email")
    window.geometry("820x650")
    record, frozen = application.review_snapshot(request_id)
    approved = False
    header = "\n".join([f"From: {frozen['from']}", f"To: {', '.join(frozen['to'])}",
                        f"Cc: {', '.join(frozen['cc'])}", f"Bcc: {', '.join(frozen['bcc'])}", f"Subject: {frozen['subject']}"])
    tk.Label(window, text="Review this exact email before sending", font=("TkDefaultFont", 16, "bold"), anchor="w").pack(fill="x", padx=20, pady=15)
    content = scrolledtext.ScrolledText(window, wrap="word", padx=12, pady=12)
    content.pack(fill="both", expand=True, padx=20)
    content.insert("end", header + "\n\n" + frozen["body"] + "\n\nAttachments:\n")
    for attachment in frozen["attachments"]:
        content.insert("end", f"{attachment['name']} — {attachment['size']} bytes\nSHA-256: {attachment['sha256']}\n")
    content.configure(state="disabled")

    def send():
        nonlocal approved
        if time.time() >= record["expires"]:
            messagebox.showerror("Review expired", "Prepare a new message before sending.", parent=window)
            return
        approved = True
        window.destroy()

    controls = tk.Frame(window)
    controls.pack(fill="x", padx=20, pady=20)
    tk.Button(controls, text="Cancel", command=window.destroy).pack(side="right", padx=8)
    tk.Button(controls, text="Send this email", command=send).pack(side="right", padx=8)
    window.mainloop()
    return application.send_reviewed(record, frozen, approved)
