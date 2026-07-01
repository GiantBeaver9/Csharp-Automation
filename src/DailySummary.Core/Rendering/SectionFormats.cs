namespace DailySummary.Core.Rendering;

/// <summary>Formats one section/sub-heading + body into a target format (Markdown or HTML).</summary>
public delegate string FormatSection(string heading, int level, string body);

/// <summary>The two shipped formats. Same structured document, two renderings.</summary>
public static class SectionFormats
{
    public static string Markdown(string heading, int level, string body)
    {
        var hashes = new string('#', Math.Clamp(level, 1, 6));
        return $"{hashes} {heading}\n\n{body}\n";
    }

    public static string Html(string heading, int level, string body)
    {
        var tag = $"h{Math.Clamp(level, 1, 6)}";
        // Bodies are markdown-ish text from the LLM. A real build swaps this for Markdig.ToHtml(body);
        // kept dependency-free here so Core stays pure.
        var paragraphs = body
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => $"<p>{System.Net.WebUtility.HtmlEncode(p.Trim())}</p>");
        return $"<{tag}>{System.Net.WebUtility.HtmlEncode(heading)}</{tag}>\n{string.Join("\n", paragraphs)}";
    }
}
