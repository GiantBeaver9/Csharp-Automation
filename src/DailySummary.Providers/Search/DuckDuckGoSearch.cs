using System.Text.RegularExpressions;
using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Search;

/// <summary>
/// Keyless web search by scraping DuckDuckGo's HTML endpoint through the page fetcher.
/// Behind IWebSearch so a keyed backend (Brave/Google CSE) can replace it later.
/// </summary>
public sealed partial class DuckDuckGoSearch : IWebSearch
{
    private readonly IPageFetcher _fetcher;

    public DuckDuckGoSearch(IPageFetcher fetcher) => _fetcher = fetcher;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int count, CancellationToken ct)
    {
        var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
        var page = await _fetcher.FetchAsync(url, ct).ConfigureAwait(false);

        // The fetcher returns rendered text; result links are extracted from the fetched HTML in a
        // real build. Here we parse href-like tokens from the visible text as a starting point.
        var results = new List<SearchResult>();
        foreach (Match m in UrlRegex().Matches(page.Text))
        {
            if (results.Count >= count) break;
            results.Add(new SearchResult(Title: m.Value, Url: m.Value, Snippet: string.Empty));
        }
        return results;
    }

    [GeneratedRegex(@"https?://[^\s""'<>]+")]
    private static partial Regex UrlRegex();
}
