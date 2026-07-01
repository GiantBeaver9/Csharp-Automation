using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Delivery;

/// <summary>Writes the Markdown rendering to {outputDir}/{yyyy-MM-dd}.md.</summary>
public sealed class MarkdownFileDelivery : IDeliveryChannel
{
    public string Channel => "markdown";

    public async Task DeliverAsync(RenderedSummary doc, DeliveryConfig config, CancellationToken ct)
    {
        var dir = string.IsNullOrWhiteSpace(config.OutputDir) ? "./out" : config.OutputDir;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{DateTimeOffset.UtcNow:yyyy-MM-dd}.md");
        await File.WriteAllTextAsync(path, doc.Markdown, ct).ConfigureAwait(false);
        Console.WriteLine($"Wrote digest to {path}");
    }
}
