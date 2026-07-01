using DailySummary.Core.Abstractions;

namespace DailySummary.Providers.Summarizers;

/// <summary>The built-in "none" summarizer — returns the input verbatim (no LLM). Good for factual data.</summary>
public sealed class PassthroughSummarizer : ISummarizer
{
    public string Name => "none";

    public Task<string> SummarizeAsync(string prompt, string input, CancellationToken ct) =>
        Task.FromResult(input);
}
