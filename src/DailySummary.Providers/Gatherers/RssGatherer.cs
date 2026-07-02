using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using DailySummary.Core.Abstractions;
using DailySummary.Core.Constants;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Gatherers;

public sealed class RssSettings
{
    public List<string> Feeds { get; set; } = new();
    public int MaxItemsPerFeed { get; set; } = 10;
    public string Since { get; set; } = "1d";
    public bool FetchFullArticle { get; set; }
}

/// <summary>RSS/Atom feeds via SyndicationFeed — structured XML, no browser. One RawPiece per item, grouped by feed.</summary>
public sealed class RssGatherer : ISectionGatherer
{
    private readonly HttpClient _http;

    public RssGatherer(HttpClient http) => _http = http;

    public SectionType Type => SectionType.Rss;

    public async Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig config, CancellationToken ct)
    {
        var s = config.SettingsAs<RssSettings>();
        var cutoff = DateTimeOffset.UtcNow - ParseSince(s.Since);
        var pieces = new List<RawPiece>();
        var allLinks = new List<PageLink>();
        var seen = new HashSet<string>();

        foreach (var feedUrl in s.Feeds)
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = await _http.GetStreamAsync(feedUrl, ct).ConfigureAwait(false);
            using var reader = XmlReader.Create(stream);
            var feed = SyndicationFeed.Load(reader);
            if (feed is null) continue;

            var feedTitle = feed.Title?.Text ?? feedUrl;
            foreach (var item in feed.Items.Where(i => i.PublishDate >= cutoff).Take(s.MaxItemsPerFeed))
            {
                var body = item.Summary?.Text ?? (item.Content as TextSyndicationContent)?.Text ?? string.Empty;
                var title = item.Title?.Text ?? "(untitled)";
                pieces.Add(new RawPiece(config.Order, config.Heading, feedTitle, null, $"{title}\n{body}"));

                var link = item.Links.FirstOrDefault()?.Uri?.ToString();
                if (link is not null && seen.Add(link))
                {
                    var snippet = body.Length > 200 ? body[..200] : body;
                    allLinks.Add(new PageLink(title, link, snippet));
                }
            }
        }

        // Separate "Selected Top Links" piece: its own clean LLM call over just the feed item links.
        if (allLinks.Count > 0)
        {
            var linksText = string.Join("\n", allLinks.Select(l =>
                string.IsNullOrEmpty(l.Context) ? $"- {l.Text} — {l.Url}" : $"- {l.Text} — {l.Url} — {l.Context}"));
            pieces.Add(new RawPiece(config.Order, config.Heading, "Selected Top Links", null, linksText,
                PromptOverride: Prompts.LinkSelection));
        }

        return pieces;
    }

    private static TimeSpan ParseSince(string since) => since.Trim().ToLowerInvariant() switch
    {
        "today" => DateTimeOffset.UtcNow.TimeOfDay,
        var d when d.EndsWith('d') && int.TryParse(d[..^1], out var days) => TimeSpan.FromDays(days),
        var h when h.EndsWith('h') && int.TryParse(h[..^1], out var hours) => TimeSpan.FromHours(hours),
        _ => TimeSpan.FromDays(1)
    };
}
