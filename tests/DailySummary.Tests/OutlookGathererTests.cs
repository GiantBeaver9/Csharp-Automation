using DailySummary.Providers.Gatherers;
using Xunit;

namespace DailySummary.Tests;

public class OutlookGathererTests
{
    [Fact]
    public void BuildFirstPageUrl_Filters_Orders_Selects()
    {
        var s = new OutlookSettings { Folder = "inbox", MaxMessages = 25 };
        var since = new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);

        var url = OutlookGatherer.BuildFirstPageUrl(s, since);
        var decoded = Uri.UnescapeDataString(url);

        Assert.Contains("https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages", url);
        Assert.Contains("receivedDateTime ge 2026-07-01T06:00:00Z", decoded);
        Assert.Contains("$orderby=receivedDateTime desc", decoded);
        Assert.Contains("$top=25", decoded);
        Assert.Contains("$select=subject,from,receivedDateTime,bodyPreview,isRead,body", decoded);
        Assert.DoesNotContain("isRead eq false", decoded); // UnreadOnly defaults false
    }

    [Fact]
    public void BuildFirstPageUrl_UnreadOnly_AddsIsReadFilter()
    {
        var s = new OutlookSettings { UnreadOnly = true };

        var decoded = Uri.UnescapeDataString(OutlookGatherer.BuildFirstPageUrl(s, DateTimeOffset.UtcNow));

        Assert.Contains("isRead eq false", decoded);
    }

    [Fact]
    public void FormatMessage_IncludesSenderSubjectBodyAndUnreadFlag()
    {
        var m = new OutlookGatherer.GraphMessage
        {
            Subject = "Hello",
            IsRead = false,
            ReceivedDateTime = new DateTimeOffset(2026, 7, 1, 6, 30, 0, TimeSpan.Zero),
            From = new OutlookGatherer.GraphFrom
            {
                EmailAddress = new OutlookGatherer.GraphEmailAddress { Name = "Bob", Address = "bob@example.com" }
            },
            Body = new OutlookGatherer.GraphBody { ContentType = "text", Content = "the body text" }
        };

        var text = OutlookGatherer.FormatMessage(m);

        Assert.Contains("[unread]", text);
        Assert.Contains("Bob <bob@example.com>", text);
        Assert.Contains("Subject: Hello", text);
        Assert.Contains("the body text", text);
    }

    [Fact]
    public void FormatMessage_StripsHtml_WhenBodyContentTypeIsHtml()
    {
        var m = new OutlookGatherer.GraphMessage
        {
            Subject = "HTML mail",
            IsRead = true,
            From = new OutlookGatherer.GraphFrom
            {
                EmailAddress = new OutlookGatherer.GraphEmailAddress { Address = "a@b.com" }
            },
            Body = new OutlookGatherer.GraphBody
            {
                ContentType = "html",
                Content = "<style>.x{}</style><p>Hello&nbsp;<b>world</b></p><script>evil()</script>"
            }
        };

        var text = OutlookGatherer.FormatMessage(m);

        Assert.Contains("Hello world", text);
        Assert.DoesNotContain("<", text);
        Assert.DoesNotContain("evil()", text); // script contents removed
    }

    [Fact]
    public void FormatMessage_FallsBackToPreview_WhenBodyEmpty()
    {
        var m = new OutlookGatherer.GraphMessage
        {
            Subject = "No body",
            IsRead = true,
            BodyPreview = "preview snippet",
            From = new OutlookGatherer.GraphFrom
            {
                EmailAddress = new OutlookGatherer.GraphEmailAddress { Address = "x@y.com" }
            }
        };

        var text = OutlookGatherer.FormatMessage(m);

        Assert.DoesNotContain("[unread]", text); // read message
        Assert.Contains("From: x@y.com", text);   // name absent -> address only
        Assert.Contains("preview snippet", text);
    }
}
