namespace DailySummary.Core.Models;

/// <summary>
/// One gathered source item flowing through the pipeline channel.
/// Emitted by an <c>ISectionGatherer</c>, consumed by the summarizer lane.
/// </summary>
/// <param name="SectionOrder">Canonical position of the owning section in the digest.</param>
/// <param name="Heading">The section heading (for rendering).</param>
/// <param name="SubHeading">Optional sub-group within a section (e.g. one per question/feed/day). Folds separately.</param>
/// <param name="Instruction">Optional per-piece prompt context (e.g. the question, or a prompt-section instruction).</param>
/// <param name="Text">The raw gathered text to summarize. May be empty for prompt sections.</param>
/// <param name="Error">When set, this piece failed to gather; renders an "unavailable" note.</param>
public sealed record RawPiece(
    int SectionOrder,
    string Heading,
    string? SubHeading,
    string? Instruction,
    string Text,
    string? Error = null);

/// <summary>A fetched web page.</summary>
public sealed record PageContent(string Url, string Title, string Text);

/// <summary>A single web-search result.</summary>
public sealed record SearchResult(string Title, string Url, string Snippet);

/// <summary>One rendered entry under a section (optionally under a sub-heading).</summary>
public sealed record SummaryEntry(string? SubHeading, string Body);

/// <summary>A finished section of the newspaper: a heading plus one or more entries.</summary>
public sealed record SummarySection(int Order, string Heading, IReadOnlyList<SummaryEntry> Entries);

/// <summary>The assembled, format-agnostic document. The orchestrator builds this — no LLM pass over it.</summary>
public sealed record DailySummary(string Title, IReadOnlyList<SummarySection> Sections);

/// <summary>The rendered "triple": subject + markdown + html. Each delivery channel pulls what it needs.</summary>
public sealed record RenderedSummary(string Subject, string Markdown, string Html);
