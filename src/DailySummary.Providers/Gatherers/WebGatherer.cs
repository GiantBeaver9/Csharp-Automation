using DailySummary.Core.Abstractions;
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

        foreach (var url in s.Urls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var page = await _fetcher.FetchAsync(url, ct).ConfigureAwait(false);
                var text = page.Text.Length > s.MaxChars ? page.Text[..s.MaxChars] : page.Text;
                // Append the on-page links so the summarizer can cite the top stories by URL.
                var links = page.Links.Count == 0
                    ? string.Empty
                    : "\n\nLINKS (headline — url):\n" +
                      string.Join("\n", page.Links.Take(s.MaxLinks).Select(l => $"- {l.Text} — {l.Url}"));
                pieces.Add(new RawPiece(config.Order, config.Heading, null, null, $"{page.Title}\n{text}{links}"));
            }
            catch (Exception ex)
            {
                pieces.Add(new RawPiece(config.Order, config.Heading, null, null, string.Empty, $"{url}: {ex.Message}"));
            }
        }

        return pieces;
    }
}
