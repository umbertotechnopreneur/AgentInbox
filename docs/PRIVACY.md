# Privacy

MailMeUp reads the email and calendars you've chosen to share. It doesn't call an AI service itself, use analytics or save copies of your messages and appointments to disk.

## What MailMeUp can do

It can search and read. It can't send messages, change or delete mail or appointments, or send invitations.

Your app settings are saved locally. Sign-in tokens are protected by your operating system.

## What your assistant receives

When you ask for a message or appointment, MailMeUp returns that information to your assistant. The assistant may send it to its AI service. Running MailMeUp on your computer doesn't make the conversation offline.

Message text, meeting titles, attendees and joining links can contain private information. Choose which accounts and calendars to share, and ask for the details you need.

## What stays on your computer

The local database stores account names, addresses and the types of read access you've granted. It doesn't contain tokens or app secrets, and it isn't encrypted, so keep the data folder private to your Windows user.

Search references and the information needed to fetch another page stay in memory for about 30 minutes. The newer, untested [read-limit changes](READ_GUARDRAILS.md) also keep small copies of message and event details in memory for up to two minutes. Neither is saved to disk.

Logs leave out message content, meeting details, full provider responses and sign-in secrets. They can include error codes and an account key that helps connect related errors; treat logs as private when sharing them. See [logging details](LOGGING.md).

Removing an account from MailMeUp deletes its local record and cached sign-in tokens. To revoke the app's access as well, use your Google or Microsoft account settings.
