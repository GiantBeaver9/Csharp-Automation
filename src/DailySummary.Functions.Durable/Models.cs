using DailySummary.Core.Models;

namespace DailySummary.Functions.Durable;

/// <summary>A gathered piece plus the section it belongs to — the input to the summarize activity.</summary>
public sealed record PieceWork(SectionConfig Section, RawPiece Piece);

/// <summary>A summarized piece flowing back to the orchestrator (small — the raw text stayed in the activity).</summary>
public sealed record SummarizedPiece(int Order, string Heading, string? SubHeading, string Body, bool Failed);

/// <summary>Input to the delivery activity: which digest, and the assembled document.</summary>
public sealed record DeliverWork(string DigestName, DigestDocument Document);
