using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Delivery;

/// <summary>Writes the Markdown rendering to {outputDir}/{yyyy-MM-dd}.md.</summary>
public sealed class MarkdownFileDelivery : IDeliveryChannel
{
    public string Channel => "markdown";

    public async Task DeliverAsync(RenderedSummary doc, DeliveryConfig config, string digestName, CancellationToken ct)
    {
        var dir = string.IsNullOrWhiteSpace(config.OutputDir) ? "./out" : config.OutputDir;
        Directory.CreateDirectory(dir); // creates the folder (and parents) if missing

        // Name the file {date}-{digest} ("2026-07-01-morning.md"); if it already exists, bump a
        // numeric suffix ("2026-07-01-morning-1.md", …) so nothing is overwritten.
        var baseName = $"{DateTime.Now:yyyy-MM-dd}-{Slug(digestName)}"; // local PC date
        var path = NextAvailablePath(dir, baseName);
        await File.WriteAllTextAsync(path, doc.Markdown, ct).ConfigureAwait(false);
        Console.WriteLine($"Wrote digest to {path}");
    }

    private static string NextAvailablePath(string dir, string baseName)
    {
        var path = Path.Combine(dir, $"{baseName}.md");
        for (var n = 1; File.Exists(path); n++)
            path = Path.Combine(dir, $"{baseName}-{n}.md");
        return path;
    }

    private static string Slug(string text)
    {
        var slug = new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-') is { Length: > 0 } s ? s : "digest";
    }
}
