using DailySummary.Core.Models;

namespace DailySummary.Core.Abstractions;

/// <summary>An LLM (or passthrough) that condenses text under a prompt.</summary>
public interface ISummarizer
{
    /// <summary>Name this summarizer is registered under (e.g. "claude", "ollama", "none").</summary>
    string Name { get; }

    Task<string> SummarizeAsync(string prompt, string input, CancellationToken ct);
}

/// <summary>Gathers the raw pieces for one section type.</summary>
public interface ISectionGatherer
{
    /// <summary>The section type this gatherer handles (its registry key).</summary>
    SectionType Type { get; }

    Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig config, CancellationToken ct);
}

/// <summary>Captures a fully rendered web page (headed Firefox under the hood).</summary>
public interface IPageFetcher : IAsyncDisposable
{
    Task<PageContent> FetchAsync(string url, CancellationToken ct);
}

/// <summary>Web search returning the top result links for a query.</summary>
public interface IWebSearch
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int count, CancellationToken ct);
}

/// <summary>Speech-to-text for podcast audio.</summary>
public interface ITranscriber
{
    Task<string> TranscribeAsync(Stream audio, CancellationToken ct);
}

/// <summary>Turns the structured <see cref="DigestDocument"/> into the rendered triple.</summary>
public interface ISummaryRenderer
{
    RenderedSummary Render(DigestDocument summary);
}

/// <summary>Delivers a rendered digest (markdown file, console, email, …).</summary>
public interface IDeliveryChannel
{
    /// <summary>The channel name this handles (e.g. "markdown", "console", "email").</summary>
    string Channel { get; }

    Task DeliverAsync(RenderedSummary doc, DeliveryConfig config, CancellationToken ct);
}

/// <summary>Runs a named digest end to end: gather → summarize → render → deliver.</summary>
public interface ISummaryOrchestrator
{
    Task RunAsync(string digestName, CancellationToken ct);
}
