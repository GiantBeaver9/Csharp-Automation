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

        var channelNames = digest.Delivery.ResolvedChannels();

        // Validate all up front so a typo fails fast (before any partial delivery).
        foreach (var name in channelNames)
            if (!_delivery.ContainsKey(name))
                throw new KeyNotFoundException(
                    $"No delivery channel '{name}'. Available: {string.Join(", ", _delivery.Keys)}.");

        // Deliver to each; isolate per-channel failures (e.g. SMTP down must not block the markdown file).
        foreach (var name in channelNames)
        {
            try
            {
                await _delivery[name].DeliverAsync(rendered, digest.Delivery, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Delivery channel '{name}' failed: {ex.Message}");
            }
        }
    }
}
