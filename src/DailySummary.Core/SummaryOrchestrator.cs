using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;
using DailySummary.Core.Pipeline;

namespace DailySummary.Core;

/// <summary>Runs a named digest: gather + summarize (pipeline) → render → deliver.</summary>
public sealed class SummaryOrchestrator : ISummaryOrchestrator
{
    private readonly AppConfig _app;
    private readonly GatherSummarizePipeline _pipeline;
    private readonly ISummaryRenderer _renderer;
    private readonly IReadOnlyDictionary<string, IDeliveryChannel> _delivery;

    public SummaryOrchestrator(
        AppConfig app,
        GatherSummarizePipeline pipeline,
        ISummaryRenderer renderer,
        IEnumerable<IDeliveryChannel> deliveryChannels)
    {
        _app = app;
        _pipeline = pipeline;
        _renderer = renderer;
        _delivery = deliveryChannels.ToDictionary(d => d.Channel, StringComparer.OrdinalIgnoreCase);
    }

    public async Task RunAsync(string digestName, CancellationToken ct)
    {
        var digest = _app.Digest(digestName)
            ?? throw new KeyNotFoundException($"No digest named '{digestName}' in app.json.");

        var summary = await _pipeline.RunAsync(digest, _app, ct).ConfigureAwait(false);
        var rendered = _renderer.Render(summary);

        if (!_delivery.TryGetValue(digest.Delivery.Channel, out var channel))
            throw new KeyNotFoundException(
                $"No delivery channel '{digest.Delivery.Channel}'. Available: {string.Join(", ", _delivery.Keys)}.");

        await channel.DeliverAsync(rendered, digest.Delivery, ct).ConfigureAwait(false);
    }
}
