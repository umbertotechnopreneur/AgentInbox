# Calendars and appointments

Ask your assistant for appointments across the calendars you've chosen to share. MailMeUp shows a short agenda first. It fetches descriptions, attendees and meeting links when you ask for more detail.

You need to allow calendar access when connecting each account. Email access alone doesn't include calendars. MailMeUp reads Google Calendar and Microsoft calendars through their APIs.

## Example requests

- "Show tomorrow's appointments across my work calendars."
- "Find meetings with Alex next week."
- "Open the details of this appointment."

You choose which accounts and calendars to include. Each search can cover up to 31 days and 20 selected calendars.

## When reading the results

Check the dates and time zones, especially for all-day events, recurring meetings and daylight-saving changes. The same meeting can appear in more than one account, so its calendar source needs to stay visible.

If a calendar couldn't be searched, the answer must say so. An empty list only means a free day when all the calendars you asked for were checked.

## Reading only

MailMeUp can't create, edit or delete appointments, send or reply to invitations, or change attendees.

This is still an early preview. Windows checks passed across seven real calendars, and tests with made-up data cover date boundaries and fetching more pages. Real recurring meetings, cancellations and time-zone examples still need comparison with Google or Microsoft.

For developers, the tools are `list_calendars`, `search_events` and `read_event`. See [the tool reference](MCP_CONTRACT.md).

API references: [Google Calendar](https://developers.google.com/workspace/calendar/api/v3/reference/events/list), [Microsoft calendar view](https://learn.microsoft.com/en-us/graph/api/calendar-list-calendarview?view=graph-rest-1.0).
