using DailySummary.Core.Constants;

namespace DailySummary.Core.Summarization;

/// <summary>Splits text into overlapping sliding windows so no context is lost at the seams.</summary>
public static class TextChunker
{
    public static IReadOnlyList<string> Split(
        string text,
        int maxChars = SummarizationLimits.MaxChunkChars,
        int overlap = SummarizationLimits.ChunkOverlapChars)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        if (maxChars <= 0) throw new ArgumentOutOfRangeException(nameof(maxChars));
        if (overlap < 0 || overlap >= maxChars) overlap = Math.Clamp(overlap, 0, maxChars - 1);
        if (text.Length <= maxChars) return new[] { text };

        var chunks = new List<string>();
        var step = maxChars - overlap;
        for (var start = 0; start < text.Length; start += step)
        {
            var len = Math.Min(maxChars, text.Length - start);
            chunks.Add(text.Substring(start, len));
            if (start + len >= text.Length) break;
        }
        return chunks;
    }
}
