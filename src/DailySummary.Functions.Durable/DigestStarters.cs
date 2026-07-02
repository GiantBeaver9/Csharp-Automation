using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace DailySummary.Functions.Durable;

/// <summary>
/// Thin timer triggers that only KICK OFF the orchestration and return immediately.
/// One per digest; the schedule binds from the same app settings as the timer host.
/// </summary>
public sealed class DigestStarters
{
    [Function("MorningDigestStarter")]
    public async Task Morning(
        [TimerTrigger("%MORNING_SCHEDULE%")] TimerInfo timer,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        var id = await client.ScheduleNewOrchestrationInstanceAsync(nameof(DigestOrchestrator), "morning");
        ctx.GetLogger<DigestStarters>().LogInformation("Started morning digest orchestration {Id}", id);
    }

    [Function("EveningDigestStarter")]
    public async Task Evening(
        [TimerTrigger("%EVENING_SCHEDULE%")] TimerInfo timer,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        var id = await client.ScheduleNewOrchestrationInstanceAsync(nameof(DigestOrchestrator), "evening");
        ctx.GetLogger<DigestStarters>().LogInformation("Started evening digest orchestration {Id}", id);
    }
}
