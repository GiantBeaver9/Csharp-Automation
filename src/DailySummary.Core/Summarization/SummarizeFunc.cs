namespace DailySummary.Core.Summarization;

/// <summary>
/// The passable unit of summarization work. Each section supplies its own
/// (e.g. SummarizeNews, SummarizeWeather); the chunk loop just invokes it.
/// </summary>
public delegate Task<string> SummarizeFunc(string input, CancellationToken ct);

/// <summary>The per-chunk delegate plus the explicit final fold delegate for a section.</summary>
public sealed record SummarizePair(SummarizeFunc Chunk, SummarizeFunc Final);
