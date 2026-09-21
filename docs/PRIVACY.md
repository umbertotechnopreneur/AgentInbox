# AgentInbox Privacy Policy

Last updated: 2026-09-20

AgentInbox connects your email and calendars to an assistant you choose. This policy explains what the app reads, what stays on your device and what can leave it.

[Product page](https://umbertogiacobbi.biz/agentinbox/) · [Privacy policy](https://umbertogiacobbi.biz/agentinbox/privacy/) · [Terms of use](https://umbertogiacobbi.biz/agentinbox/terms/)

## Who is responsible

I am Umberto Giacobbi, the developer of AgentInbox. For privacy questions or requests about information you send me, contact hello@umbertogiacobbi.biz.

This policy covers the AgentInbox desktop app, command-line app and local MCP connection. AgentInbox does not upload your connected mailbox or calendar to a server I operate. Browsing this website is a separate activity, covered by the website privacy notice.

- [Website privacy notice](https://umbertogiacobbi.biz/privacy/)

## What AgentInbox reads and why

When you connect Google, AgentInbox reads your account identifier, display name and email address to identify the account. It receives OAuth access and refresh tokens so it can make the reads you authorize without asking you to sign in for every request. Sign-in happens through the provider; AgentInbox does not collect your Google or Microsoft password.

With mail access enabled, AgentInbox searches messages and reads selected messages. Results can contain subjects, senders, recipients, timestamps, snippets, message text, read status and whether attachments are present. Gmail message reads retrieve the message payload, including MIME metadata; the app does not offer a separate attachment-download tool.

With calendar access enabled, AgentInbox lists calendars and searches and reads appointments. Data can include calendar names, identifiers and time zones, event titles, dates and times, cancellation status, locations, descriptions, attendees and meeting links.

Search text, filters and the requested item identifiers are sent to Google or Microsoft to fulfil your request. The purpose is to help you find and read your own mail and appointments through your connected assistant. AgentInbox cannot send mail, change or delete provider messages or events, or send invitations.

## Google permissions

AgentInbox requests openid, email and profile to identify your connected Google account. If you select mail, it also requests https://www.googleapis.com/auth/gmail.readonly. If you select calendars, it requests https://www.googleapis.com/auth/calendar.calendarlist.readonly and https://www.googleapis.com/auth/calendar.events.readonly.

Gmail read access is needed to search your mailbox and read message text; metadata-only access would not provide that feature. The two calendar permissions allow calendar discovery and event reads without requesting calendar write access. You can connect mail, calendars, or both.

Provider authorization and sharing with an assistant are separate controls. Review the account, mail and calendar sharing settings before connecting an assistant. OAuth permission can cover more data than you choose to share through AgentInbox.

## Your assistant and other recipients

AgentInbox returns requested results to the local MCP client connected to it. These results can include account names and email addresses, message text, appointment details, attendees and meeting links. Tokens and provider secrets are not returned as tool results.

A client such as Claude, Codex or Visual Studio Code may forward results to its configured AI service and retain them in a conversation, logs or other storage. That processing may happen outside your country. AgentInbox does not call an AI model itself, but a local connection does not make the whole workflow offline.

Check the client and provider terms, retention and training settings before sharing Google data. Use only a configuration compatible with the Google data-use limits below. AgentInbox cannot enforce an external service’s retention settings or delete copies it has already received.

Google and Microsoft receive the API and sign-in requests needed to provide their services. I do not receive app telemetry or automatically uploaded diagnostic logs. If you send me a support request, I receive the contact details and material you choose to include. Do not send passwords, tokens or complete mailboxes.

## What stays on your device

AgentInbox stores account identifiers, names, email addresses and permission flags in accounts.db. Provider settings, search preferences, sharing choices and selected calendar identifiers are stored separately. These metadata and settings files are not encrypted by AgentInbox.

OAuth tokens and configured provider secrets use operating-system-protected storage. On Windows this uses DPAPI for the current user. This protection does not encrypt the whole data folder. Provider API requests use HTTPS.

The data folder is under your operating system’s Local Application Data location in an AgentInbox folder, normally %LOCALAPPDATA%\AgentInbox on Windows. A packaged installation or the AGENTINBOX_DATA_DIR override can change the resolved location.

AgentInbox does not keep an on-disk archive of message bodies or appointment details. Search state, result references and limited detail caches are kept in process memory. Your operating system, backups and connected assistant may retain their own copies.

Local diagnostic logs record operation names, timing, counts, failure categories and account correlation keys. The logging configuration excludes provider payloads, message bodies, meeting details and credential values. Local read-limit files also retain usage counters, timestamps and hashed scope keys to coordinate processes; they are not a mailbox archive.

## How long data is kept

Account records and protected tokens remain until you remove the account or clear the local profile. Settings, sharing records and diagnostic state can remain after an account is removed.

Result references and paging state expire after 30 minutes. Cached message and event details stop being reusable after two minutes. Expired entries are cleared during subsequent cache operations; stopping every AgentInbox process releases its in-memory state.

Logs roll daily and when a file reaches 10 MiB, with up to 14 files retained during log rotation. This is a file-count limit, not a promise that every log is deleted after 14 days. You can delete local logs yourself.

If you contact me, I use what you send to answer your request. I keep that correspondence while needed for the request and any resulting agreement or applicable record-keeping obligation. You can ask me to delete it; I will explain if a legal obligation requires keeping part of it.

## Stop access and delete data

Turn off sharing for an account, mail or calendars in AgentInbox to stop subsequent assistant reads through that connection. Remove the account to delete its active local account record and stored account tokens. A disabled sharing record containing the account identifier remains so reconnecting does not silently restore earlier assistant access.

Also revoke AgentInbox in your Google Account connections or Microsoft account permissions. Removing an account locally does not revoke the provider-side grant and does not delete the original messages or appointments.

For a full local cleanup on Windows, remove connected accounts, close the desktop app and stop every AgentInbox process started by your assistants. Then delete the AgentInbox data folder for that installation, including settings, sharing records, logs and read-limit state. Remove its MCP connection from each assistant if you no longer use it. Uninstalling alone should not be relied on to remove every copy.

Local deletion is not secure erasure of disk remnants or backups. Separately delete conversations, exports or other copies retained by assistants and external services using their controls. I cannot remotely delete data stored only on your device or in an account with another provider.

- [Google Account connections](https://myaccount.google.com/connections)
- [Microsoft personal account permissions](https://account.live.com/consent/Manage)
- [Microsoft work or school account applications](https://myapplications.microsoft.com/)

## Google data and Limited Use

I commit to using and transferring information obtained through Google APIs only in accordance with the Google API Services User Data Policy and Google Workspace user data and developer policy, including their Limited Use requirements.

I use Google data only to provide the email and calendar features described here. I do not sell it, use it for advertising, credit decisions or surveillance, or use it to train or improve general-purpose AI models. The same restrictions apply to data derived from it.

The app shares results with your chosen assistant to fulfil the features you authorize. This is not permission for that recipient to use Google data for unrelated purposes or general model training. I do not routinely read your Google data or receive it on an AgentInbox backend. If support requires a specific sample, share only what is needed and expressly authorize its review; necessary security investigations or legal obligations may also require limited handling.

- [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy)
- [Google Workspace user data and developer policy](https://developers.google.com/workspace/workspace-api-user-data-developer-policy)

## Your requests and policy changes

For information you send me directly, you can request access, correction or deletion and, where applicable, restriction, objection or portability. You can also contact the competent data-protection authority. Email hello@umbertogiacobbi.biz and identify AgentInbox and the information concerned.

This notice describes the current app. I will update it when data handling changes. If a change introduces a new use of Google data, I will explain that use and obtain any required consent before it starts. The date at the top identifies this version.
