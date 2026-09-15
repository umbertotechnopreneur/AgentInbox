using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MailMeUp.Core;
using MailMeUp.Providers.Microsoft;
using Xunit;

namespace MailMeUp.Tests;

public sealed class MicrosoftMailSearchTests
{
    private static readonly string[] ExcludedFolders = ["synthetic-junk", "synthetic-deleted"];
    private static readonly DateTimeOffset Start = new(2026, 9, 5, 0, 0, 0, TimeSpan.FromHours(7));
    private static readonly DateTimeOffset End = Start.AddDays(1);

    [Theory]
    [InlineData("", null, true)]
    [InlineData("invoice OR contract", null, true)]
    [InlineData("", "sender@example.test", true)]
    [InlineData("", null, false)]
    [InlineData("invoice OR contract", null, false)]
    public void FolderScopeAppliesToStructuredAndTextSearch(string text, string? sender, bool inboxOnly)
    {
        var query = new ProviderMailQuery(text, sender, Start, End, UnreadOnly: true, InboxOnly: inboxOnly);
        var url = (string)Invoke("CreateSearchUrl", query, 3, inboxOnly ? Array.Empty<string>() : ExcludedFolders)!;

        Assert.Equal(inboxOnly ? "/v1.0/me/mailFolders/inbox/messages" : "/v1.0/me/messages", new Uri(url).AbsolutePath);
        var parameters = SearchParameters(query);
        if (text.Length == 0 && sender is null)
        {
            Assert.Contains("isRead eq false", parameters["$filter"]);
            Assert.Contains("receivedDateTime ge 2026-09-04T17:00:00Z", parameters["$filter"]);
            Assert.Contains("receivedDateTime lt 2026-09-05T17:00:00Z", parameters["$filter"]);
            if (inboxOnly)
                Assert.DoesNotContain("parentFolderId", parameters["$filter"]);
        }
        else
        {
            Assert.Contains("$search", parameters.Keys);
            Assert.DoesNotContain("$filter", parameters.Keys);
        }
    }

    [Theory]
    [InlineData(true, "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?%24skiptoken=synthetic%2Bnext")]
    [InlineData(false, "https://graph.microsoft.com/v1.0/me/messages?%24skiptoken=synthetic%2Bnext")]
    public void ContinuationRetainsItsExactMailCollectionAndProviderQuery(bool inboxOnly, string cursor)
    {
        Assert.Equal(cursor, MicrosoftMailReader.ValidateNextLink(cursor, inboxOnly));
    }

    [Theory]
    [InlineData(true, "https://graph.microsoft.com/v1.0/me/messages?%24skiptoken=next")]
    [InlineData(false, "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?%24skiptoken=next")]
    [InlineData(true, "https://graph.microsoft.com/v1.0/me/mailFolders/archive/messages?%24skiptoken=next")]
    [InlineData(true, "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/abc123")]
    [InlineData(true, "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messagesOther?%24skiptoken=next")]
    [InlineData(false, "https://graph.microsoft.com/v1.0/me/messagesOther?%24skiptoken=next")]
    [InlineData(true, "https://graph.microsoft.com:8443/v1.0/me/mailFolders/inbox/messages")]
    [InlineData(true, "https://other.example.test/v1.0/me/mailFolders/inbox/messages")]
    [InlineData(true, "http://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages")]
    [InlineData(true, "https://user@graph.microsoft.com/v1.0/me/mailFolders/inbox/messages")]
    [InlineData(true, "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages#fragment")]
    public void ContinuationCannotWidenOrChangeTheRequestedCollection(bool inboxOnly, string cursor)
    {
        Assert.Throws<ProviderReadException>(() => MicrosoftMailReader.ValidateNextLink(cursor, inboxOnly));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invoice")]
    public void InboxPageKeepsExactUnreadAndDateConstraints(string text)
    {
        const string nextLink = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?%24skiptoken=synthetic-next";
        var page = ParsePage([
                Message("start", Start),
                Message("before", Start.AddTicks(-1)),
                Message("end", End),
                Message("read", Start, isRead: true),
                Message("last", End.AddTicks(-1))
            ], new(text, null, Start, End, UnreadOnly: true, InboxOnly: true), nextLink);

        Assert.Equal(["start", "last"], page.Items.Select(item => item.ProviderMessageId));
        Assert.Equal(nextLink, page.NextCursor);
    }

    [Theory]
    [InlineData("Microsoft", null, null)]
    [InlineData("", "sender@example.test", null)]
    [InlineData("", null, "recipient@example.test")]
    public void TextOrAddressSearchNeverCombinesSearchWithFilterOrOrderBy(string text, string? sender, string? recipient)
    {
        var query = new ProviderMailQuery(text, sender, Start, End, recipient, true, true);
        var parameters = SearchParameters(query);

        Assert.Contains("$search", parameters.Keys);
        Assert.DoesNotContain("$filter", parameters.Keys);
        Assert.DoesNotContain("$orderby", parameters.Keys);
        Assert.Equal("3", parameters["$top"]);
        Assert.Contains("parentFolderId", parameters["$select"]);
        Assert.Contains("isRead", parameters["$select"]);
        Assert.Contains("hasAttachments", parameters["$select"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StructuredListingRetainsServerFiltersAndDateOrder(bool attachments)
    {
        var parameters = SearchParameters(new("", null, Start, End, UnreadOnly: true, HasAttachments: attachments));

        Assert.DoesNotContain("$search", parameters.Keys);
        Assert.Equal("receivedDateTime DESC", parameters["$orderby"]);
        Assert.StartsWith("receivedDateTime ge 2026-09-04T17:00:00Z", parameters["$filter"]);
        Assert.Contains("receivedDateTime lt 2026-09-05T17:00:00Z", parameters["$filter"]);
        Assert.Contains("isRead eq false", parameters["$filter"]);
        Assert.Contains($"hasAttachments eq {attachments.ToString().ToLowerInvariant()}", parameters["$filter"]);
        Assert.All(ExcludedFolders, folder => Assert.Contains($"parentFolderId ne '{folder}'", parameters["$filter"]));
    }

    [Fact]
    public void TextSearchBoundsProviderWorkAndPreservesUtcPrecision()
    {
        var start = Start.AddTicks(1);
        var end = End.AddTicks(2);
        var parameters = SearchParameters(new("invoice OR contract", null, start, end));

        Assert.Contains("(invoice OR contract) AND received>=2026-09-04T17:00:00.0000001Z AND received<2026-09-05T17:00:00.0000002Z", parameters["$search"]);
        Assert.DoesNotContain("$filter", parameters.Keys);
        Assert.DoesNotContain("$orderby", parameters.Keys);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddressSearchKeepsExplicitOneSidedDateRange(bool lowerBound)
    {
        var parameters = SearchParameters(new("", "sender@example.test", lowerBound ? Start : null, lowerBound ? null : End));

        Assert.Contains(lowerBound ? "received>=" : "received<", parameters["$search"]);
        Assert.DoesNotContain(lowerBound ? "received<" : "received>=", parameters["$search"]);
    }

    [Fact]
    public void TextSearchFiltersProviderPageLocallyAndRetainsContinuation()
    {
        const string nextLink = "https://graph.microsoft.com/v1.0/me/messages?%24skiptoken=synthetic-next";
        var values = new[]
        {
            Message("start", Start),
            Message("before", Start.AddTicks(-1)),
            Message("end", End),
            Message("junk", Start, "synthetic-junk"),
            Message("deleted", Start, "synthetic-deleted"),
            Message("unknown-folder", Start, null),
            Message("read", Start, isRead: true),
            Message("no-attachment", Start, attachments: false),
            Message("last", End.AddTicks(-1))
        };
        var page = ParsePage(values, new("Microsoft", null, Start, End, UnreadOnly: true, HasAttachments: true), nextLink);

        Assert.Equal(["start", "last"], page.Items.Select(item => item.ProviderMessageId));
        Assert.Equal(nextLink, page.NextCursor);
    }

    [Fact]
    public void FullyExcludedPageKeepsContinuationSoLaterMatchesAreReachable()
    {
        const string nextLink = "https://graph.microsoft.com/v1.0/me/messages?%24skiptoken=synthetic-next";
        var page = ParsePage([Message("junk", Start, "synthetic-junk")], new("Microsoft", null, null, null), nextLink);

        Assert.Empty(page.Items);
        Assert.Equal(nextLink, page.NextCursor);
    }

    [Fact]
    public void AttachmentFalseIsEnforcedEvenWithoutServerFilter()
    {
        var page = ParsePage([Message("with", Start), Message("without", Start, attachments: false)],
            new("Microsoft", null, null, null, HasAttachments: false));

        Assert.Equal("without", Assert.Single(page.Items).ProviderMessageId);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void GraphBadRequestHasActionableCategory()
    {
        Assert.Equal(ReadFailureKind.InvalidRequest, MicrosoftReadRequests.ClassifyStatus(400));
    }

    private static Dictionary<string, string> SearchParameters(ProviderMailQuery query)
    {
        var url = (string)Invoke("CreateSearchUrl", query, 3, query.InboxOnly ? Array.Empty<string>() : ExcludedFolders)!;
        return new Uri(url).Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
    }

    private static object Message(string id, DateTimeOffset date, string? folder = "synthetic-inbox", bool isRead = false, bool attachments = true) => new
    {
        id,
        parentFolderId = folder,
        receivedDateTime = date.ToString("O", CultureInfo.InvariantCulture),
        isRead,
        hasAttachments = attachments,
        subject = "Synthetic Microsoft sample",
        bodyPreview = "Synthetic preview"
    };

    private static ProviderMailSearchPage ParsePage(object[] messages, ProviderMailQuery query, string? nextLink = null)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["value"] = messages,
            ["@odata.nextLink"] = nextLink
        }));
        return (ProviderMailSearchPage)Invoke("ParseSearchPage", document.RootElement, query, 50,
            query.InboxOnly ? Array.Empty<string>() : ExcludedFolders)!;
    }

    private static object? Invoke(string name, params object?[] arguments)
    {
        // Like calendar boundary tests, use synthetic Graph data without constructing an OAuth-backed reader.
        var method = typeof(MicrosoftMailReader).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method.Invoke(null, arguments);
    }
}
