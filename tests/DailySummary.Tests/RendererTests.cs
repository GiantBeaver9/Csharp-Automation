using DailySummary.Core.Models;
using DailySummary.Core.Rendering;
using Xunit;

namespace DailySummary.Tests;

public class RendererTests
{
    private static DailySummary Sample() => new(
        "Daily Brief — Test",
        new[]
        {
            new SummarySection(0, "Weather", new[] { new SummaryEntry(null, "Sunny all day.") }),
            new SummarySection(1, "Q&A", new[]
            {
                new SummaryEntry("What time is it?", "Morning."),
                new SummaryEntry("Any news?", "None.")
            })
        });

    [Fact]
    public void Markdown_UsesHashHeaders()
    {
        var doc = new SummaryRenderer().Render(Sample());
        Assert.Contains("# Daily Brief — Test", doc.Markdown);
        Assert.Contains("## Weather", doc.Markdown);
        Assert.Contains("### What time is it?", doc.Markdown); // sub-heading
    }

    [Fact]
    public void Html_UsesTags()
    {
        var doc = new SummaryRenderer().Render(Sample());
        Assert.Contains("<h1>", doc.Html);
        Assert.Contains("<h2>", doc.Html);
        Assert.Contains("<h3>", doc.Html);
    }

    [Fact]
    public void Triple_SubjectIsTitle()
    {
        var doc = new SummaryRenderer().Render(Sample());
        Assert.Equal("Daily Brief — Test", doc.Subject);
    }
}
