using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Core.Rendering;

/// <summary>
/// Walks the structured <see cref="DailySummary"/> and produces the rendered triple
/// (subject + markdown + html) via the <see cref="FormatSection"/> delegate.
/// No LLM pass — pure assembly.
/// </summary>
public sealed class SummaryRenderer : ISummaryRenderer
{
    public RenderedSummary Render(DailySummary summary) => new(
        Subject: summary.Title,
        Markdown: RenderWith(summary, SectionFormats.Markdown, title => $"# {title}\n"),
        Html: RenderWith(summary, SectionFormats.Html, title => $"<h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>"));

    private static string RenderWith(DailySummary summary, FormatSection fmt, Func<string, string> title)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(title(summary.Title));

        foreach (var section in summary.Sections.OrderBy(s => s.Order))
        {
            // Sections with a single null sub-heading render flat; otherwise each entry gets a sub-heading.
            var flat = section.Entries.Count == 1 && section.Entries[0].SubHeading is null;
            if (flat)
            {
                sb.AppendLine(fmt(section.Heading, 2, section.Entries[0].Body));
            }
            else
            {
                sb.AppendLine(fmt(section.Heading, 2, string.Empty));
                foreach (var entry in section.Entries)
                    sb.AppendLine(fmt(entry.SubHeading ?? section.Heading, 3, entry.Body));
            }
        }
        return sb.ToString().TrimEnd() + "\n";
    }
}
