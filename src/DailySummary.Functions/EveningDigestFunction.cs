using DailySummary.Core.Abstractions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DailySummary.Functions;

/// <summary>10pm dev recap digest. Schedule bound from the EVENING_SCHEDULE app setting.</summary>
public sealed class EveningDigestFunction
{
    private readonly ISummaryOrchestrator _orchestrator;
    private readonly ILogger<EveningDigestFunction> _log;

    public EveningDigestFunction(ISummaryOrchestrator orchestrator, ILogger<EveningDigestFunction> log)
    {
        _orchestrator = orchestrator;
        _log = log;
    }

    // CRON is a literal (the host resolves the attribute; it can't read app.json). Keep in sync with
    // app.json digests[name=="evening"].schedule, which documents the intended schedule.
    [Function(nameof(EveningDigestFunction))]
    public async Task Run([TimerTrigger("0 0 22 * * *")] TimerInfo timer, CancellationToken ct)
    {
        _log.LogInformation("Running evening digest at {Time}", DateTimeOffset.UtcNow);
        await _orchestrator.RunAsync("evening", ct);
    }
}
