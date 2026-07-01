using System.Collections.Concurrent;
using System.Threading.Channels;
using DailySummary.Core.Models;
using DailySummary.Core.Summarization;

namespace DailySummary.Core.Pipeline;

/// <summary>
/// Bounded-channel producer→consumer: concurrent gatherers write <see cref="RawPiece"/>s;
/// a single (or few) LLM consumer lanes summarize each as it arrives; results fold per
/// (section, sub-heading) into the structured <see cref="DigestDocument"/>.
/// </summary>
public sealed class GatherSummarizePipeline
{
    private readonly SectionGathererRegistry _gatherers;
    private readonly SectionSummarizers _summarizers;
    private readonly SummarizerRegistry _summarizerRegistry;
    private readonly ChunkedSummarizer _chunked;

    public GatherSummarizePipeline(
        SectionGathererRegistry gatherers,
        SectionSummarizers summarizers,
        SummarizerRegistry summarizerRegistry,
        ChunkedSummarizer chunked)
    {
        _gatherers = gatherers;
        _summarizers = summarizers;
        _summarizerRegistry = summarizerRegistry;
        _chunked = chunked;
    }

    private sealed record PieceResult(int Order, string Heading, string? Sub, string Body, bool Failed);

    public async Task<DigestDocument> RunAsync(DigestConfig digest, AppConfig app, CancellationToken ct)
    {
        var sections = digest.Sections;
        var byOrder = sections.GroupBy(s => s.Order).ToDictionary(g => g.Key, g => g.First());

        var channel = Channel.CreateBounded<(SectionConfig Section, RawPiece Piece)>(
            new BoundedChannelOptions(Math.Max(1, app.ChannelCapacity)) { SingleReader = false, SingleWriter = false });

        // ---- Producers: gather concurrently, fail-soft ----
        var producers = Task.Run(async () =>
        {
            await Parallel.ForEachAsync(
                sections,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, app.Concurrency.Fetch), CancellationToken = ct },
                async (section, _) =>
                {
                    try
                    {
                        var gatherer = _gatherers.Resolve(section.Type);
                        var pieces = await WithTimeout(
                            token => gatherer.GatherAsync(section, token), section.TimeoutSeconds, ct)
                            .ConfigureAwait(false);
                        foreach (var piece in pieces)
                            await channel.Writer.WriteAsync((section, piece), ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await channel.Writer.WriteAsync(
                            (section, Unavailable(section, ex.Message)), ct).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            channel.Writer.Complete();
        }, ct);

        // ---- Consumers: summarize as pieces arrive (concurrency.summarize lanes) ----
        var results = new ConcurrentQueue<PieceResult>();
        var lanes = Math.Max(1, app.Concurrency.Summarize);
        var consumers = Enumerable.Range(0, lanes).Select(_ => Task.Run(async () =>
        {
            await foreach (var (section, piece) in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (piece.Error is not null)
                {
                    results.Enqueue(new PieceResult(section.Order, section.Heading, piece.SubHeading,
                        $"_this call was unable to complete: {piece.Error}_", Failed: true));
                    continue;
                }

                try
                {
                    var pair = _summarizers.For(section, piece);
                    var body = string.IsNullOrEmpty(piece.Text)
                        ? await WithTimeout(t => pair.Chunk(string.Empty, t), section.TimeoutSeconds, ct).ConfigureAwait(false)
                        : await WithTimeout(t => _chunked.SummarizeAsync(piece.Text, pair.Chunk, pair.Final, t),
                            section.TimeoutSeconds, ct).ConfigureAwait(false);
                    results.Enqueue(new PieceResult(section.Order, section.Heading, piece.SubHeading, body, Failed: false));
                }
                catch (Exception ex)
                {
                    results.Enqueue(new PieceResult(section.Order, section.Heading, piece.SubHeading,
                        $"_this call was unable to complete: {ex.Message}_", Failed: true));
                }
            }
        }, ct)).ToArray();

        await producers.ConfigureAwait(false);
        await Task.WhenAll(consumers).ConfigureAwait(false);

        // ---- Fold + assemble (mechanical; no LLM pass over the whole doc) ----
        var ordered = results.ToList(); // global enqueue order preserved
        var summarySections = new List<SummarySection>();

        foreach (var orderGroup in ordered.GroupBy(r => r.Order).OrderBy(g => g.Key))
        {
            if (!byOrder.TryGetValue(orderGroup.Key, out var cfg)) continue;
            var entries = new List<SummaryEntry>();

            // Preserve first-seen sub-heading order.
            foreach (var subKey in orderGroup.Select(r => r.Sub).Distinct())
            {
                var group = orderGroup.Where(r => r.Sub == subKey).ToList();
                var body = await FoldAsync(cfg, subKey, group, ct).ConfigureAwait(false);
                entries.Add(new SummaryEntry(subKey, body));
            }

            summarySections.Add(new SummarySection(cfg.Order, cfg.Heading, entries));
        }

        var title = $"Daily Brief — {DateTimeOffset.UtcNow:ddd, MMM d, yyyy}";
        return new DigestDocument(title, summarySections);
    }

    private async Task<string> FoldAsync(SectionConfig cfg, string? sub, List<PieceResult> group, CancellationToken ct)
    {
        var bodies = group.Where(r => !r.Failed).Select(r => r.Body).ToList();
        if (bodies.Count == 0) return string.Join("\n", group.Select(r => r.Body)); // all failed → the notes
        if (bodies.Count == 1) return bodies[0];

        // Mechanical join for passthrough + prompt sections; LLM reduce for the rest.
        if (SummarizerRegistry.IsPassthrough(cfg.Summarizer) || cfg.Type == SectionType.Prompt)
            return string.Join("\n\n", bodies.Select(b => $"- {b}"));

        var pair = _summarizers.For(cfg, new RawPiece(cfg.Order, cfg.Heading, sub, sub, string.Empty));
        return await pair.Final(string.Join("\n\n", bodies), ct).ConfigureAwait(false);
    }

    private static async Task<T> WithTimeout<T>(Func<CancellationToken, Task<T>> work, int seconds, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (seconds > 0) cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return await work(cts.Token).ConfigureAwait(false);
    }

    private static RawPiece Unavailable(SectionConfig section, string error) =>
        new(section.Order, section.Heading, null, null, string.Empty, error);
}
