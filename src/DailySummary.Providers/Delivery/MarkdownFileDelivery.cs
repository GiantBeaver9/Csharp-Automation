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
        Directory.CreateDirectory(dir); // creates the folder (and parents) if missing
        // Filename derived from the subject (which includes the digest name + date), so the
        // morning and evening digests don't overwrite each other.
        var path = Path.Combine(dir, $"{Slug(doc.Subject)}.md");
        await File.WriteAllTextAsync(path, doc.Markdown, ct).ConfigureAwait(false);
        Console.WriteLine($"Wrote digest to {path}");
    }

    private static string Slug(string text)
    {
        var chars = text.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
