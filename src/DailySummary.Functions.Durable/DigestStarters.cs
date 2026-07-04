using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace DailySummary.Functions.Durable;

/// <summary>
/// Thin timer triggers that only KICK OFF the orchestration and return immediately.
/// Literal CRON + UseMonitor=false (the host resolves the attribute and can't read app.json;
/// the blob schedule monitor isn't needed) — keep in sync with app.json digests[].schedule.
/// </summary>
public sealed class DigestStarters
{
    [Function("MorningDigestStarter")]
    public async Task Morning(
        [TimerTrigger("0 0 6 * * *", UseMonitor = false)] TimerInfo timer,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        var id = await client.ScheduleNewOrchestrationInstanceAsync(nameof(DigestOrchestrator), "morning");
        ctx.GetLogger<DigestStarters>().LogInformation("Started morning digest orchestration {Id}", id);
    }

    [Function("EveningDigestStarter")]
    public async Task Evening(
        [TimerTrigger("0 0 22 * * *", UseMonitor = false)] TimerInfo timer,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        var id = await client.ScheduleNewOrchestrationInstanceAsync(nameof(DigestOrchestrator), "evening");
        ctx.GetLogger<DigestStarters>().LogInformation("Started evening digest orchestration {Id}", id);
    }
}
