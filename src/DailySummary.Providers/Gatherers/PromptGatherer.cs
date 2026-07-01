using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;
using DailySummary.Core.Summarization;

namespace DailySummary.Providers.Gatherers;

public sealed class PromptSettings
{
    /// <summary>"single" (one instruction → one answer) or "enumerate" (list, then summarize each).</summary>
    public string Mode { get; set; } = "single";

    /// <summary>Instruction for single mode.</summary>
    public string? Prompt { get; set; }

    /// <summary>enumerate: prompt that makes the LLM list items (one per line), no summarizing.</summary>
    public string? ListPrompt { get; set; }

    /// <summary>enumerate: per-item prompt; "{item}" is replaced with each listed line.</summary>
    public string? ItemPrompt { get; set; }
}

/// <summary>
/// The "let the tool/MCP-enabled LLM do it" section. Gather is (almost) a no-op:
/// - single: emit one RawPiece whose Instruction is the prompt (Text empty).
/// - enumerate: run the list prompt through the LLM, then emit one RawPiece per listed item.
/// </summary>
public sealed class PromptGatherer : ISectionGatherer
{
    private readonly SummarizerRegistry _summarizers;

    public PromptGatherer(SummarizerRegistry summarizers) => _summarizers = summarizers;

    public SectionType Type => SectionType.Prompt;

    public async Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig config, CancellationToken ct)
    {
        var s = config.SettingsAs<PromptSettings>();

        if (!s.Mode.Equals("enumerate", StringComparison.OrdinalIgnoreCase))
        {
            var instruction = s.Prompt ?? config.Prompt ?? string.Empty;
            return new[] { new RawPiece(config.Order, config.Heading, null, instruction, string.Empty) };
        }

        // enumerate: one LLM call to list items (the LLM uses its own MCP tools).
        var llm = _summarizers.Resolve(config.Summarizer);
        var listing = await llm.SummarizeAsync(s.ListPrompt ?? "List the items, one per line.", string.Empty, ct)
            .ConfigureAwait(false);

        var itemPrompt = s.ItemPrompt ?? "Summarize this item in 1-2 lines:\n{item}";
        var pieces = listing
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => new RawPiece(
                config.Order, config.Heading, null,
                Instruction: itemPrompt.Replace("{item}", item),
                Text: string.Empty))
            .ToList();

        return pieces;
    }
}
