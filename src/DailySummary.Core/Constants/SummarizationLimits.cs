namespace DailySummary.Core.Constants;

/// <summary>Chunking limits — no floating magic numbers.</summary>
public static class SummarizationLimits
{
    /// <summary>Most characters sent to the LLM in one chunk.</summary>
    public const int MaxChunkChars = 3000;

    /// <summary>Characters each chunk shares with the previous one (context across the seam).</summary>
    public const int ChunkOverlapChars = 100;
}
