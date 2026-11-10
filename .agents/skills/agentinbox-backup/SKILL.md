---
name: agentinbox-backup
description: Guide a user through exporting or restoring AgentInbox account settings and saved passwords in an encrypted local backup. Use for moving AgentInbox configuration to a new profile or computer and checking the restored MCP account list.
---

1. Identify the source or destination profile and the encrypted file path. An export needs a new filename. An import needs an empty profile. Keep the selected profile consistent with AgentInbox setup and the MCP client.
2. Give the user the appropriate command to run in their own terminal. Use `agentinbox profile export FILE` or `agentinbox profile import FILE` with the installed Linux CLI. From a checkout, use `python -B python/run.py profile export FILE` or `python -B python/run.py profile import FILE`. Set `AGENTINBOX_DATA_DIR` to an absolute path when using a separate profile.
3. Let the user's terminal request the passphrase with hidden input. Never request a passphrase, mailbox password or Microsoft sign-in code in chat or pass one through MCP, shell arguments, or environment variables. Use only the encrypted file as the transfer artifact.
4. After an import, tell the user to sign in again to restored Microsoft accounts. Keep the saved read and send choices. If the user asks to verify MCP access, call AgentInbox's `list_accounts` in a new session configured for the restored profile. The result shows shared accounts and folders. Provider access requires its own check.
