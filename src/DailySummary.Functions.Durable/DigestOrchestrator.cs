using System.Globalization;
using DailySummary.Core.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace DailySummary.Functions.Durable;

/// <summary>
/// Deterministic coordination only — no I/O, no wall-clock, no services. All real work is in activities.
/// Fan-out gather (parallel), single LLM lane (sequential summarize), then a pure fold + render + deliver.
/// </summary>
public static class DigestOrchestrator
{
    [Function(nameof(DigestOrchestrator))]
    public static async Task RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var digestName = context.GetInput<string>() ?? "morning";

        var sections = await context.CallActivityAsync<SectionConfig[]>(
            nameof(DigestActivities.LoadSectionsActivity), digestName);

        // FAN-OUT: gather every section in parallel (I/O-bound activities).
        var gatherTasks = sections
            .Select(s => context.CallActivityAsync<RawPiece[]>(nameof(DigestActivities.GatherSectionActivity), s))
            .ToList();
        var gathered = await Task.WhenAll(gatherTasks);

        // Pair each piece with its section (for the summarize prompt), preserving order.
        var work = new List<PieceWork>();
        for (var i = 0; i < sections.Length; i++)
            foreach (var piece in gathered[i])
                work.Add(new PieceWork(sections[i], piece));

        // SINGLE LLM LANE: summarize one piece at a time (sequential await → never overwhelms the local model).
        var summarized = new List<SummarizedPiece>(work.Count);
        foreach (var w in work)
            summarized.Add(await context.CallActivityAsync<SummarizedPiece>(
                nameof(DigestActivities.SummarizePieceActivity), w));

        // FOLD + ASSEMBLE — pure, deterministic (no LLM pass over the whole doc).
        var doc = BuildDocument(digestName, summarized, context.CurrentUtcDateTime);

        // DELIVER (I/O) → activity.
        await context.CallActivityAsync(nameof(DigestActivities.DeliverActivity), new DeliverWork(digestName, doc));
    }

    private static DigestDocument BuildDocument(string digestName, List<SummarizedPiece> pieces, DateTimeOffset now)
    {
        var sections = pieces
            .GroupBy(p => p.Order)
            .OrderBy(g => g.Key)
            .Select(orderGroup =>
            {
                var heading = orderGroup.First().Heading;
                var entries = orderGroup
                    .GroupBy(p => p.SubHeading)                       // preserve first-seen sub order
                    .Select(sub => new SummaryEntry(
                        sub.Key,
                        string.Join("\n\n", sub.Select(p => p.Body))))
                    .ToList();
                return new SummarySection(orderGroup.Key, heading, entries);
            })
            .ToList();

        var label = string.IsNullOrWhiteSpace(digestName)
            ? "Daily"
            : char.ToUpperInvariant(digestName[0]) + digestName[1..];
        var date = now.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture);
        return new DigestDocument($"{label} Brief — {date}", sections);
    }
}
