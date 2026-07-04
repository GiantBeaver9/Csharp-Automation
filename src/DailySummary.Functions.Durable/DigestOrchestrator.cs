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

        // If the heavy compute lives on a separate machine (Pi orchestrator → GPU PC), wake it and wait
        // for its LLM server before doing any work. Deterministic: durable timers + recorded activity
        // results only, so this survives orchestrator replay. Sleep is the PC's own idle timer, not ours.
        await EnsureComputeAwakeAsync(context);

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

    /// <summary>
    /// Wake-on-LAN the compute node, then poll its LLM server on a durable timer until it answers or the
    /// budget runs out. If it never comes up we proceed anyway — LLM-dependent sections fail in isolation
    /// and still render as unavailable, while local sections (weather, calendar) are unaffected.
    /// </summary>
    private static async Task EnsureComputeAwakeAsync(TaskOrchestrationContext context)
    {
        var compute = await context.CallActivityAsync<RemoteComputeConfig?>(
            nameof(DigestActivities.LoadComputeActivity), "");
        if (compute is not { Enabled: true }) return;

        await context.CallActivityAsync(nameof(DigestActivities.WakePcActivity), compute);

        var deadline = context.CurrentUtcDateTime.AddSeconds(compute.MaxWaitSeconds);
        var pollSeconds = Math.Max(1, compute.PollSeconds);
        while (context.CurrentUtcDateTime < deadline)
        {
            if (await context.CallActivityAsync<bool>(nameof(DigestActivities.PcReadyActivity), compute))
                return;
            await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(pollSeconds), CancellationToken.None);
        }
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
