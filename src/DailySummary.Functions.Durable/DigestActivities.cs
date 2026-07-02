using DailySummary.Core;
using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;
using DailySummary.Core.Pipeline;
using DailySummary.Core.Summarization;
using Microsoft.Azure.Functions.Worker;

namespace DailySummary.Functions.Durable;

/// <summary>
/// The side-effectful work, wrapping the existing Core/Providers services. Activities are where all
/// I/O and LLM calls live; the orchestrator only coordinates them.
/// </summary>
public sealed class DigestActivities
{
    private readonly AppConfig _app;
    private readonly SectionGathererRegistry _gatherers;
    private readonly SectionSummarizers _summarizers;
    private readonly ChunkedSummarizer _chunked;
    private readonly ISummaryRenderer _renderer;
    private readonly IReadOnlyDictionary<string, IDeliveryChannel> _delivery;

    public DigestActivities(
        AppConfig app,
        SectionGathererRegistry gatherers,
        SectionSummarizers summarizers,
        ChunkedSummarizer chunked,
        ISummaryRenderer renderer,
        IEnumerable<IDeliveryChannel> deliveryChannels)
    {
        _app = app;
        _gatherers = gatherers;
        _summarizers = summarizers;
        _chunked = chunked;
        _renderer = renderer;
        _delivery = deliveryChannels.ToDictionary(d => d.Channel, StringComparer.OrdinalIgnoreCase);
    }

    [Function(nameof(LoadSectionsActivity))]
    public SectionConfig[] LoadSectionsActivity([ActivityTrigger] string digestName)
    {
        var digest = _app.Digest(digestName)
            ?? throw new KeyNotFoundException($"No digest named '{digestName}' in app.json.");
        return digest.Sections.ToArray();
    }

    [Function(nameof(GatherSectionActivity))]
    public async Task<RawPiece[]> GatherSectionActivity([ActivityTrigger] SectionConfig section)
    {
        using var cts = Timeout(section.TimeoutSeconds);
        try
        {
            var gatherer = _gatherers.Resolve(section.Type);
            var pieces = await gatherer.GatherAsync(section, cts.Token).ConfigureAwait(false);
            return pieces.ToArray();
        }
        catch (Exception ex)
        {
            return new[] { new RawPiece(section.Order, section.Heading, null, null, string.Empty, ex.Message) };
        }
    }

    [Function(nameof(SummarizePieceActivity))]
    public async Task<SummarizedPiece> SummarizePieceActivity([ActivityTrigger] PieceWork work)
    {
        var (section, piece) = (work.Section, work.Piece);
        if (piece.Error is not null)
            return new SummarizedPiece(section.Order, section.Heading, piece.SubHeading,
                $"_this call was unable to complete: {piece.Error}_", Failed: true);

        using var cts = Timeout(section.TimeoutSeconds);
        try
        {
            var pair = _summarizers.For(section, piece);
            var body = string.IsNullOrEmpty(piece.Text)
                ? await pair.Chunk(string.Empty, cts.Token).ConfigureAwait(false)
                : await _chunked.SummarizeAsync(piece.Text, pair.Chunk, pair.Final, cts.Token).ConfigureAwait(false);
            return new SummarizedPiece(section.Order, section.Heading, piece.SubHeading, body, Failed: false);
        }
        catch (Exception ex)
        {
            return new SummarizedPiece(section.Order, section.Heading, piece.SubHeading,
                $"_this call was unable to complete: {ex.Message}_", Failed: true);
        }
    }

    [Function(nameof(DeliverActivity))]
    public async Task DeliverActivity([ActivityTrigger] DeliverWork work)
    {
        var digest = _app.Digest(work.DigestName)
            ?? throw new KeyNotFoundException($"No digest named '{work.DigestName}' in app.json.");

        var rendered = _renderer.Render(work.Document);

        foreach (var name in digest.Delivery.ResolvedChannels())
        {
            if (!_delivery.TryGetValue(name, out var channel)) continue;
            try
            {
                await channel.DeliverAsync(rendered, digest.Delivery, work.DigestName, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Delivery channel '{name}' failed: {ex.Message}");
            }
        }
    }

    private static CancellationTokenSource Timeout(int seconds)
    {
        var cts = new CancellationTokenSource();
        if (seconds > 0) cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }
}
