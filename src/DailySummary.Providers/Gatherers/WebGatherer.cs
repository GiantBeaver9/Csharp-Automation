using DailySummary.Core.Abstractions;
using DailySummary.Core.Constants;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Gatherers;

public sealed class WebSettings
{
    public List<string> Urls { get; set; } = new();
    public int MaxChars { get; set; } = 8000;

    /// <summary>How many extracted page links to hand the LLM as link candidates (DOM order = lead stories first).</summary>
    public int MaxLinks { get; set; } = 40;
}

/// <summary>Fetches configured pages via the (Firefox) page fetcher — one RawPiece per URL, folded to one entry.</summary>
public sealed class WebGatherer : ISectionGatherer
{
    private readonly IPageFetcher _fetcher;

    public WebGatherer(IPageFetcher fetcher) => _fetcher = fetcher;

    public SectionType Type => SectionType.Web;

    public async Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig config, CancellationToken ct)
    {
        var s = config.SettingsAs<WebSettings>();
        var pieces = new List<RawPiece>();
        var allLinks = new List<PageLink>();
        var seen = new HashSet<string>();

        foreach (var url in s.Urls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var page = await _fetcher.FetchAsync(url, ct).ConfigureAwait(false);
                var text = page.Text.Length > s.MaxChars ? page.Text[..s.MaxChars] : page.Text;
                // Content piece: clean page text only (the summary call shouldn't juggle links).
                pieces.Add(new RawPiece(config.Order, config.Heading, null, null, $"{page.Title}\n{text}"));
                foreach (var l in page.Links)
                    if (seen.Add(l.Url)) allLinks.Add(l);
            }
            catch (Exception ex)
            {
                pieces.Add(new RawPiece(config.Order, config.Heading, null, null, string.Empty, $"{url}: {ex.Message}"));
            }
        }

        // Separate "Selected Top Links" piece: its own clean LLM call (PromptOverride) over just the links.
        if (allLinks.Count > 0)
        {
            var linksText = string.Join("\n", allLinks.Take(s.MaxLinks).Select(l =>
                string.IsNullOrEmpty(l.Context) ? $"- {l.Text} — {l.Url}" : $"- {l.Text} — {l.Url} — {l.Context}"));
            pieces.Add(new RawPiece(config.Order, config.Heading, "Selected Top Links", null, linksText,
                PromptOverride: Prompts.LinkSelection));
        }

        return pieces;
    }
}
