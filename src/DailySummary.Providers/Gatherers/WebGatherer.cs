using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Gatherers;

public sealed class WebSettings
{
    public List<string> Urls { get; set; } = new();
    public int MaxChars { get; set; } = 8000;
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
                pieces.Add(new RawPiece(config.Order, config.Heading, null, null, $"{page.Title}\n{text}"));
            }
            catch (Exception ex)
            {
                pieces.Add(new RawPiece(config.Order, config.Heading, null, null, string.Empty, $"{url}: {ex.Message}"));
            }
        }

        return pieces;
    }
}
