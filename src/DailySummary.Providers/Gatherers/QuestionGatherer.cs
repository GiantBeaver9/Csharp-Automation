using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Gatherers;

public sealed class QuestionSettings
{
    public string Engine { get; set; } = "duckduckgo";
    public int ResultsPerQuestion { get; set; } = 3;
    public List<string> Questions { get; set; } = new();
}

/// <summary>
/// For each question: search → fetch top results → emit one RawPiece per result page,
/// tagged SubHeading = Instruction = the question. The pipeline summarizes each page (level 1)
/// then folds per question into one answer (level 2).
/// </summary>
public sealed class QuestionGatherer : ISectionGatherer
{
    private readonly IWebSearch _search;
    private readonly IPageFetcher _fetcher;

    public QuestionGatherer(IWebSearch search, IPageFetcher fetcher)
    {
        _search = search;
        _fetcher = fetcher;
    }

    public SectionType Type => SectionType.Question;

    public async Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig config, CancellationToken ct)
    {
        var s = config.SettingsAs<QuestionSettings>();
        var pieces = new List<RawPiece>();

        foreach (var question in s.Questions)
        {
            ct.ThrowIfCancellationRequested();
            var results = await _search.SearchAsync(question, s.ResultsPerQuestion, ct).ConfigureAwait(false);
            if (results.Count == 0)
            {
                pieces.Add(new RawPiece(config.Order, config.Heading, question, question,
                    $"(No search results found for: {question})"));
                continue;
            }

            foreach (var result in results)
            {
                try
                {
                    var page = await _fetcher.FetchAsync(result.Url, ct).ConfigureAwait(false);
                    pieces.Add(new RawPiece(config.Order, config.Heading, question, question, page.Text));
                }
                catch (Exception ex)
                {
                    pieces.Add(new RawPiece(config.Order, config.Heading, question, question, string.Empty,
                        $"{result.Url}: {ex.Message}"));
                }
            }
        }

        return pieces;
    }
}
