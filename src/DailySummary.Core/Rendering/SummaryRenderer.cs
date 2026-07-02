using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Core.Rendering;

/// <summary>
/// Walks the structured <see cref="DigestDocument"/> and produces the rendered triple
/// (subject + markdown + html) via the <see cref="FormatSection"/> delegate.
/// No LLM pass — pure assembly.
/// </summary>
public sealed class SummaryRenderer : ISummaryRenderer
{
    public RenderedSummary Render(DigestDocument summary) => new(
        Subject: summary.Title,
        Markdown: RenderWith(summary, SectionFormats.Markdown, title => $"# {title}\n"),
        Html: RenderWith(summary, SectionFormats.Html, title => $"<h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>"));

    private static string RenderWith(DigestDocument summary, FormatSection fmt, Func<string, string> title)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(title(summary.Title));

        foreach (var section in summary.Sections.OrderBy(s => s.Order))
        {
            // Entries with no sub-heading are the section body (rendered under the H2);
            // entries with a sub-heading (e.g. "Selected Top Links", per-day, per-question) become H3 blocks.
            var mainBody = string.Join("\n\n", section.Entries.Where(e => e.SubHeading is null).Select(e => e.Body));
            sb.AppendLine(fmt(section.Heading, 2, mainBody));
            foreach (var entry in section.Entries.Where(e => e.SubHeading is not null))
                sb.AppendLine(fmt(entry.SubHeading!, 3, entry.Body));
        }
        return sb.ToString().TrimEnd() + "\n";
    }
}
