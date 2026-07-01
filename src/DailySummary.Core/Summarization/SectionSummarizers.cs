using DailySummary.Core.Constants;
using DailySummary.Core.Models;

namespace DailySummary.Core.Summarization;

/// <summary>
/// Builds the (chunk, final) <see cref="SummarizePair"/> for a given section + piece.
/// The effective prompt = the section's prompt (or the type default), augmented with the
/// piece's <c>Instruction</c> when present (the question, or a prompt-section instruction).
/// </summary>
public sealed class SectionSummarizers
{
    private readonly SummarizerRegistry _registry;

    public SectionSummarizers(SummarizerRegistry registry) => _registry = registry;

    public SummarizePair For(SectionConfig config, RawPiece piece)
    {
        var summarizer = _registry.Resolve(config.Summarizer);
        var basePrompt = config.Prompt ?? Prompts.Default(config.Type);
        var prompt = Combine(basePrompt, piece.Instruction);

        SummarizeFunc chunk = (input, ct) => summarizer.SummarizeAsync(prompt, input, ct);
        SummarizeFunc final = (input, ct) => summarizer.SummarizeAsync(prompt + Prompts.ReduceSuffix, input, ct);
        return new SummarizePair(chunk, final);
    }

    private static string Combine(string basePrompt, string? instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction)) return basePrompt;
        return string.IsNullOrWhiteSpace(basePrompt) ? instruction : $"{basePrompt}\n\n{instruction}";
    }
}
