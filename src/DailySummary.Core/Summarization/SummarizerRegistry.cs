using DailySummary.Core.Abstractions;

namespace DailySummary.Core.Summarization;

/// <summary>Resolves a summarizer by name ("claude", "ollama", "none", …).</summary>
public sealed class SummarizerRegistry
{
    private readonly IReadOnlyDictionary<string, ISummarizer> _byName;

    public SummarizerRegistry(IEnumerable<ISummarizer> summarizers) =>
        _byName = summarizers.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    public ISummarizer Resolve(string name) =>
        _byName.TryGetValue(name, out var s)
            ? s
            : throw new KeyNotFoundException(
                $"No summarizer named '{name}'. Available: {string.Join(", ", _byName.Keys)}.");

    /// <summary>True when the named summarizer is the verbatim passthrough.</summary>
    public static bool IsPassthrough(string name) => string.Equals(name, "none", StringComparison.OrdinalIgnoreCase);
}
