using System.Net;
using DailySummary.Core.Abstractions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace DailySummary.Functions;

/// <summary>
/// Manual run endpoint for testing: GET/POST /api/run/{digest}. Runs the digest synchronously
/// (avoids the timer-binding quirk of invoking a TimerTrigger via the admin endpoint) and returns
/// when it finishes. Handy for kicking off "morning" or "evening" on demand while watching the logs.
/// </summary>
public sealed class RunDigestHttp
{
    private readonly ISummaryOrchestrator _orchestrator;
    private readonly ILogger<RunDigestHttp> _log;

    public RunDigestHttp(ISummaryOrchestrator orchestrator, ILogger<RunDigestHttp> log)
    {
        _orchestrator = orchestrator;
        _log = log;
    }

    [Function("RunDigest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "run/{digest}")] HttpRequestData req,
        string digest)
    {
        _log.LogInformation("Manual run of digest '{Digest}' requested", digest);
        try
        {
            // CancellationToken.None so a client disconnect (long runs) doesn't abort the digest;
            // functionTimeout still bounds it.
            await _orchestrator.RunAsync(digest, CancellationToken.None);
            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteStringAsync($"Digest '{digest}' completed — check the output folder / logs.");
            return ok;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Digest '{Digest}' failed", digest);
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            // Anonymous endpoint — don't echo ex.Message (can carry SMTP errors, paths, connection strings).
            await err.WriteStringAsync($"Digest '{digest}' failed — check the logs.");
            return err;
        }
    }
}
