using System.Text;
using System.Text.Json;
using MailMeUp.Core;
using MailMeUp.Providers.Google;
using Xunit;

namespace MailMeUp.Tests;

public sealed class GoogleMailReaderTests
{
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
