namespace DailySummary.Core.Summarization;

/// <summary>
/// Map-reduce over delegates: summarize each chunk with <c>chunkSummarizer</c>, then fold
/// the partials with the explicit <c>finalSummarizer</c>. A single chunk skips the fold.
/// </summary>
public sealed class ChunkedSummarizer
{
    public async Task<string> SummarizeAsync(
        string source,
        SummarizeFunc chunkSummarizer,
        SummarizeFunc finalSummarizer,
        CancellationToken ct)
    {
        var chunks = TextChunker.Split(source);
        if (chunks.Count == 0) return string.Empty;
        if (chunks.Count == 1) return await chunkSummarizer(chunks[0], ct).ConfigureAwait(false);

        var partials = new List<string>(chunks.Count);
        foreach (var chunk in chunks)
            partials.Add(await chunkSummarizer(chunk, ct).ConfigureAwait(false)); // map

        return await finalSummarizer(string.Join("\n\n", partials), ct).ConfigureAwait(false); // reduce
    }
}
