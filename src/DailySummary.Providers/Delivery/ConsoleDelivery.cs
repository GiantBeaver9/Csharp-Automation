using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Delivery;

/// <summary>Prints the Markdown rendering to stdout / function logs.</summary>
public sealed class ConsoleDelivery : IDeliveryChannel
{
    public string Channel => "console";

    public Task DeliverAsync(RenderedSummary doc, DeliveryConfig config, CancellationToken ct)
    {
        Console.WriteLine(doc.Markdown);
        return Task.CompletedTask;
    }
}
