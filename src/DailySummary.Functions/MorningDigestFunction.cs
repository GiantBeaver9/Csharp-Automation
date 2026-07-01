using DailySummary.Core.Abstractions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DailySummary.Functions;

/// <summary>6am "newspaper" digest. Schedule bound from the MORNING_SCHEDULE app setting.</summary>
public sealed class MorningDigestFunction
{
    private readonly ISummaryOrchestrator _orchestrator;
    private readonly ILogger<MorningDigestFunction> _log;

    public MorningDigestFunction(ISummaryOrchestrator orchestrator, ILogger<MorningDigestFunction> log)
    {
        _orchestrator = orchestrator;
        _log = log;
    }

    [Function(nameof(MorningDigestFunction))]
    public async Task Run([TimerTrigger("%MORNING_SCHEDULE%")] TimerInfo timer, CancellationToken ct)
    {
        _log.LogInformation("Running morning digest at {Time}", DateTimeOffset.UtcNow);
        await _orchestrator.RunAsync("morning", ct);
    }
}
