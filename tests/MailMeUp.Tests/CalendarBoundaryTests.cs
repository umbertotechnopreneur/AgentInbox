// SPDX-License-Identifier: MIT

/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MailMeUp.Core;
using MailMeUp.Providers.Google;
using MailMeUp.Providers.Microsoft;
using Xunit;

namespace MailMeUp.Tests;

public sealed class CalendarBoundaryTests
{
    [Fact]
    public void GoogleAllDayDatesRetainTheirExclusiveEndAndSortInTheCalendarTimeZone()
    {
        var summary = GoogleSummary("""
            { "id": "synthetic-all-day", "summary": "Sample", "start": { "date": "2026-09-05" }, "end": { "date": "2026-09-07" } }
            """, "Asia/Tokyo");

        Assert.NotNull(summary);
        Assert.True(summary.AllDay);
        Assert.Equal("2026-09-05", summary.Start);
        Assert.Equal("2026-09-07", summary.End);
        Assert.Equal(Instant("2026-09-04T15:00:00Z"), summary.SortStart);
    }

    [Fact]
    public void GoogleOffsetlessTimedEventUsesTheDeclaredZoneRatherThanTheMachineZone()
    {
        var summary = GoogleSummary("""
            {
              "id": "synthetic-local-time",
              "start": { "dateTime": "2026-09-05T09:00:00", "timeZone": "Europe/Rome" },
              "end": { "dateTime": "2026-09-05T10:00:00", "timeZone": "Europe/Rome" }
            }
            """);

        Assert.NotNull(summary);
        Assert.False(summary.AllDay);
        Assert.Equal(Instant("2026-09-05T07:00:00Z"), summary.SortStart);
        Assert.Equal(Instant("2026-09-05T08:00:00Z"), Instant(summary.End));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitOffsetsDisambiguateTheRepeatedDaylightSavingHour(bool microsoft)
    {
        const string json = """
            {
              "id": "synthetic-dst-instance", "isAllDay": false,
              "start": { "dateTime": "2026-11-01T01:30:00-04:00", "timeZone": "America/New_York" },
              "end": { "dateTime": "2026-11-01T01:30:00-05:00", "timeZone": "America/New_York" }
            }
            """;
        var summary = microsoft ? MicrosoftSummary(json) : GoogleSummary(json);

        Assert.NotNull(summary);
        Assert.Equal(Instant("2026-11-01T05:30:00Z"), summary.SortStart);
        Assert.Equal(TimeSpan.FromHours(1), Instant(summary.End) - Instant(summary.Start));
    }

    [Theory]
    [InlineData(false, "2026-03-08T02:30:00", "2026-03-08T04:00:00")]
    [InlineData(true, "2026-03-08T02:30:00", "2026-03-08T04:00:00")]
    [InlineData(false, "2026-11-01T01:30:00", "2026-11-01T03:00:00")]
    [InlineData(true, "2026-11-01T01:30:00", "2026-11-01T03:00:00")]
    public void MissingOffsetDuringAGapOrRepeatedHourFailsInsteadOfGuessing(bool microsoft, string start, string end)
    {
        var json = $$"""
            {
              "id": "synthetic-unresolved-dst", "isAllDay": false,
              "start": { "dateTime": "{{start}}", "timeZone": "America/New_York" },
              "end": { "dateTime": "{{end}}", "timeZone": "America/New_York" }
            }
            """;

        AssertInvalidSummary(microsoft, json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOffsetAndMissingTimeZoneCannotBecomeACompleteEvent(bool microsoft)
    {
        AssertInvalidSummary(microsoft, """
            {
              "id": "synthetic-no-zone",
              "start": { "dateTime": "2026-09-05T09:00:00" },
              "end": { "dateTime": "2026-09-05T10:00:00" }
            }
            """);
    }

    [Theory]
    [InlineData("2026-09-04T15:00:00", "2026-09-05T15:00:00", "Tokyo Standard Time", "2026-09-05", "2026-09-06")]
    [InlineData("2026-03-08T05:00:00", "2026-03-09T04:00:00", "Eastern Standard Time", "2026-03-08", "2026-03-09")]
    [InlineData("2026-11-01T04:00:00", "2026-11-02T05:00:00", "Eastern Standard Time", "2026-11-01", "2026-11-02")]
    public void MicrosoftAllDayDatesRecoverTheirOriginalZoneAcrossOffsetAndDstChanges(
        string start, string end, string originalZone, string startDate, string exclusiveEndDate)
    {
        var json = $$"""
            {
              "id": "synthetic-all-day", "isAllDay": true,
              "originalStartTimeZone": "{{originalZone}}", "originalEndTimeZone": "{{originalZone}}",
              "start": { "dateTime": "{{start}}", "timeZone": "UTC" },
              "end": { "dateTime": "{{end}}", "timeZone": "UTC" }
            }
            """;
        var summary = MicrosoftSummary(json);

        Assert.NotNull(summary);
        Assert.True(summary.AllDay);
        Assert.Equal(startDate, summary.Start);
        Assert.Equal(exclusiveEndDate, summary.End);
        Assert.Equal(Instant(start + "Z"), summary.SortStart);
    }

    [Fact]
    public void MicrosoftTimedEventRespectsItsReturnedZone()
    {
        var summary = MicrosoftSummary("""
            {
              "id": "synthetic-non-utc", "isAllDay": false,
              "start": { "dateTime": "2026-09-05T09:00:00", "timeZone": "Tokyo Standard Time" },
              "end": { "dateTime": "2026-09-05T10:00:00", "timeZone": "Tokyo Standard Time" }
            }
            """);

        Assert.NotNull(summary);
        Assert.Equal(Instant("2026-09-05T00:00:00Z"), summary.SortStart);
        Assert.Equal(Instant("2026-09-05T01:00:00Z"), Instant(summary.End));
    }

    [Fact]
    public void GoogleCancelledTombstoneCanBeSkippedButAnActiveUntimedEventCannot()
    {
        Assert.Null(GoogleSummary("""
            { "id": "synthetic-cancelled-instance", "status": "cancelled", "recurringEventId": "synthetic-series", "originalStartTime": { "dateTime": "2026-09-05T09:00:00Z" } }
            """));
        AssertInvalidSummary(false, """{ "id": "synthetic-active", "status": "confirmed", "start": null, "end": null }""");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationWithValidBoundariesRetainsTheCancelledFlag(bool microsoft)
    {
        const string json = """
            {
              "id": "synthetic-cancelled", "status": "cancelled", "isCancelled": true,
              "start": { "dateTime": "2026-09-05T09:00:00Z", "timeZone": "UTC" },
              "end": { "dateTime": "2026-09-05T10:00:00Z", "timeZone": "UTC" }
            }
            """;
        var summary = microsoft ? MicrosoftSummary(json) : GoogleSummary(json);

        Assert.NotNull(summary);
        Assert.True(summary.Cancelled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalNullObjectsAndIncompleteAttendeesDoNotBreakAnOtherwiseValidEvent(bool microsoft)
    {
        const string json = """
            {
              "id": "synthetic-optional-fields", "summary": "Sample", "subject": "Sample",
              "start": { "dateTime": "2026-09-05T09:00:00Z", "timeZone": "UTC" },
              "end": { "dateTime": "2026-09-05T10:00:00Z", "timeZone": "UTC" },
              "body": null, "location": null, "onlineMeeting": null, "conferenceData": null,
              "attendees": [ null, { "emailAddress": null },
                { "emailAddress": { "address": "guest@example.test" }, "status": null },
                { "email": "guest@example.test", "responseStatus": null } ]
            }
            """;
        var readerType = microsoft ? typeof(MicrosoftCalendarReader) : typeof(GoogleCalendarReader);
        var summary = microsoft ? MicrosoftSummary(json) : GoogleSummary(json);

        Assert.NotNull(summary);
        Assert.Equal(string.Empty, summary.Location);
        Assert.Null(Invoke(readerType, "ReadMeetingLink", json));
        var attendees = Assert.IsAssignableFrom<IReadOnlyList<string>>(Invoke(readerType, "ReadAttendees", json));
        Assert.Equal("guest@example.test", Assert.Single(attendees));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReversedEventBoundariesAreReportedInsteadOfDisappearing(bool microsoft)
    {
        AssertInvalidSummary(microsoft, """
            {
              "id": "synthetic-reversed",
              "start": { "dateTime": "2026-09-05T11:00:00Z", "timeZone": "UTC" },
              "end": { "dateTime": "2026-09-05T10:00:00Z", "timeZone": "UTC" }
            }
            """);
    }

    private static ProviderEventSummary? GoogleSummary(string json, string? calendarTimeZone = "UTC") =>
        (ProviderEventSummary?)Invoke(typeof(GoogleCalendarReader), "ParseEventSummary", json, calendarTimeZone);

    private static ProviderEventSummary? MicrosoftSummary(string json) =>
        (ProviderEventSummary?)Invoke(typeof(MicrosoftCalendarReader), "ParseEventSummary", json);

    private static void AssertInvalidSummary(bool microsoft, string json)
    {
        var failure = Assert.Throws<TargetInvocationException>(() =>
        {
            _ = microsoft ? MicrosoftSummary(json) : GoogleSummary(json);
        });
        Assert.IsType<ProviderReadException>(failure.InnerException);
    }

    private static object? Invoke(Type readerType, string methodName, string json, params object?[] extraArguments)
    {
        // Exercise parsing with synthetic provider-shaped JSON without constructing OAuth-backed readers.
        using var document = JsonDocument.Parse(json);
        var method = readerType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(null, [document.RootElement, .. extraArguments]);
    }

    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
