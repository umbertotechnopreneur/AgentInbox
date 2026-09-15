using System.Text;
using System.Text.Json;
using MailMeUp.Core;
using MailMeUp.Providers.Google;
using Xunit;

namespace MailMeUp.Tests;

public sealed class GoogleMailReaderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("synthetic-next+/=&")]
    public void InboxListFiltersBeforeHydrationAndPreservesUnreadDatesAndPaging(string? cursor)
    {
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(7));
        var end = start.AddDays(14);
        var request = new ProviderMailQuery("from:sender@example.test OR label:GitHub", null, start, end,
            UnreadOnly: true, InboxOnly: true);

        var uri = new Uri(GoogleMailReader.CreateListRequestUrl(request, 10, cursor));
        var parameters = uri.Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));

        Assert.Equal("/gmail/v1/users/me/messages", uri.AbsolutePath);
        Assert.Equal("INBOX", parameters["labelIds"]);
        Assert.Equal("false", parameters["includeSpamTrash"]);
        Assert.Equal("10", parameters["maxResults"]);
        Assert.Contains("is:unread", parameters["q"], StringComparison.Ordinal);
        Assert.Contains($"after:{start.ToUnixTimeSeconds()}", parameters["q"], StringComparison.Ordinal);
        Assert.Contains($"before:{end.ToUnixTimeSeconds()}", parameters["q"], StringComparison.Ordinal);
        Assert.Contains("from:sender@example.test OR label:GitHub", parameters["q"], StringComparison.Ordinal);
        if (cursor is null)
            Assert.DoesNotContain("pageToken", parameters.Keys);
        else
            Assert.Equal(cursor, parameters["pageToken"]);
    }

    [Fact]
    public void BroadSearchDoesNotRestrictLabels()
    {
        var uri = new Uri(GoogleMailReader.CreateListRequestUrl(
            new ProviderMailQuery("", null, null, null, UnreadOnly: true), 10, null));

        Assert.DoesNotContain("labelIds", uri.Query, StringComparison.Ordinal);
        Assert.Contains("includeSpamTrash=false", uri.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("UNREAD")]
    [InlineData("UNREAD,Label_GitHub")]
    [InlineData("INBOX,UNREAD,SPAM")]
    [InlineData("INBOX,UNREAD,TRASH")]
    public void InboxSearchOmitsMessagesMovedOutBeforeHydration(string? labels)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            labelIds = labels?.Split(','),
            payload = new { mimeType = "text/plain" }
        }));

        Assert.Null(GoogleMailReader.ParseSearchSummary("abc123", document.RootElement, inboxOnly: true));
    }

    [Fact]
    public void InboxSearchKeepsMessagesWithBothInboxAndCustomLabels()
    {
        using var document = JsonDocument.Parse("""
            { "labelIds": ["INBOX", "UNREAD", "Label_GitHub"],
              "payload": { "mimeType": "text/plain" } }
            """);

        var summary = GoogleMailReader.ParseSearchSummary("abc123", document.RootElement, inboxOnly: true);

        Assert.NotNull(summary);
        Assert.False(summary.IsRead);
    }

    [Fact]
    public void BroadSearchStillReturnsArchivedUnreadMessages()
    {
        using var document = JsonDocument.Parse("""
            { "labelIds": ["UNREAD", "Label_GitHub"], "payload": { "mimeType": "text/plain" } }
            """);

        var summary = GoogleMailReader.ParseSearchSummary("abc123", document.RootElement, inboxOnly: false);

        Assert.NotNull(summary);
        Assert.False(summary.IsRead);
    }

    [Fact]
    public void SummaryRequestSelectsMimeStructureWithoutDownloadingBodies()
    {
        var uri = new Uri(GoogleMailReader.CreateSummaryRequestUrl("abc123"));
        var query = Uri.UnescapeDataString(uri.Query);

        Assert.Contains("format=full", query, StringComparison.Ordinal);
        Assert.Contains("headers(name,value)", query, StringComparison.Ordinal);
        Assert.Contains("mimeType,filename,parts(", query, StringComparison.Ordinal);
        Assert.Contains("parts(partId)", query, StringComparison.Ordinal);
        Assert.DoesNotContain("body", query, StringComparison.Ordinal);
        Assert.DoesNotContain("data", query, StringComparison.Ordinal);
        Assert.DoesNotContain('*', query);
    }

    [Fact]
    public void BodyFreeSummaryReportsANestedAttachmentAndUnreadState()
    {
        using var document = JsonDocument.Parse("""
            {
              "id": "abc123",
              "internalDate": "1789171200000",
              "snippet": "Synthetic preview",
              "labelIds": ["INBOX", "UNREAD"],
              "payload": {
                "mimeType": "multipart/mixed",
                "headers": [
                  { "name": "Subject", "value": "Synthetic subject" },
                  { "name": "From", "value": "sender@example.test" }
                ],
                "parts": [
                  { "mimeType": "text/plain", "filename": "" },
                  {
                    "mimeType": "multipart/mixed",
                    "parts": [{ "mimeType": "application/pdf", "filename": "sample.pdf" }]
                  }
                ]
              }
            }
            """);

        var summary = GoogleMailReader.ParseSummary("abc123", document.RootElement);

        Assert.True(summary.HasAttachments);
        Assert.False(summary.IsRead);
        Assert.Equal("Synthetic preview", summary.Preview);
        Assert.Equal("sender@example.test", summary.Sender);
    }

    [Fact]
    public void ACompleteMimeTreeWithoutFilesReportsNoAttachments()
    {
        using var document = JsonDocument.Parse("""
            { "payload": { "mimeType": "multipart/alternative", "parts": [
              { "mimeType": "text/plain", "filename": "" },
              { "mimeType": "text/html", "filename": "" }
            ] } }
            """);

        Assert.False(GoogleMailReader.ParseSummary("abc123", document.RootElement).HasAttachments);
    }

    [Fact]
    public void APreviewCannotSilentlyHideAttachmentsBeyondTheProjectedMimeDepth()
    {
        object part = new { partId = "unprojected-child" };
        for (var depth = 0; depth < 17; depth++)
            part = new { mimeType = "multipart/mixed", parts = new[] { part } };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { payload = part }));

        var failure = Assert.Throws<ProviderReadException>(() => GoogleMailReader.ParseSummary("abc123", document.RootElement));

        Assert.Equal(ReadFailureKind.ResultLimit, failure.Kind);
    }

    [Fact]
    public void MissingMimeStructureIsNotReportedAsNoAttachments()
    {
        using var document = JsonDocument.Parse("""{ "id": "abc123", "labelIds": ["INBOX"] }""");

        var failure = Assert.Throws<ProviderReadException>(() => GoogleMailReader.ParseSummary("abc123", document.RootElement));

        Assert.Equal(ReadFailureKind.ProviderUnavailable, failure.Kind);
    }

    [Fact]
    public void ReadingTextDoesNotDecodeBinaryPartsOrIncludeAttachedTextFiles()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            mimeType = "multipart/mixed",
            parts = new object[]
            {
                new { mimeType = "text/plain", body = new { data = Convert.ToBase64String(Encoding.UTF8.GetBytes("Message text")) } },
                new { mimeType = "image/png", body = new { data = "not valid base64" } },
                new { mimeType = "text/plain", filename = "notes.txt", body = new { attachmentId = "external-file" } }
            }
        }));

        Assert.Equal("Message text", GoogleMailReader.ReadBody(document.RootElement));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    public void ExternallyStoredMessageTextReturnsAnExplicitIncompleteRead(string mimeType)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            mimeType,
            body = new { attachmentId = "external-text-part", size = 1024 }
        }));

        var failure = Assert.Throws<ProviderReadException>(() => GoogleMailReader.ReadBody(document.RootElement));

        Assert.Equal(ReadFailureKind.ResultLimit, failure.Kind);
        Assert.Contains("could not be read completely", failure.Message, StringComparison.Ordinal);
    }
}
