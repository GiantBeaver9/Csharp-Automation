using System.Net;
using AutomationFunctions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AutomationFunctions.Functions;

/// <summary>
/// The main cron pipeline: get weather → summarize the configured websites →
/// (optionally) scan the inbox → email one combined digest to yourself.
/// Also exposed at GET/POST /api/run/digest for on-demand runs.
/// </summary>
public class DailyDigestFunction
{
    private readonly IDigestService _digest;
    private readonly IEmailService _email;
    private readonly ILogger<DailyDigestFunction> _logger;

    public DailyDigestFunction(
        IDigestService digest,
        IEmailService email,
        ILogger<DailyDigestFunction> logger)
    {
        _digest = digest;
        _email = email;
        _logger = logger;
    }

    // Schedule comes from the DigestSchedule app setting (%...%), defaulting to daily 07:00
    // via local.settings.json. NCRONTAB: {sec} {min} {hour} {day} {month} {day-of-week}.
    // Times are UTC unless WEBSITE_TIME_ZONE (Windows) / TZ (Linux) is set.
    [Function("DailyDigestTimer")]
    public async Task RunTimer(
        [TimerTrigger("%DigestSchedule%")] TimerInfo timer,
        CancellationToken ct)
    {
        await RunAsync(ct);
    }

    [Function("DailyDigestHttp")]
    public async Task<HttpResponseData> RunHttp(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "run/digest")] HttpRequestData req,
        CancellationToken ct)
    {
        var digest = await RunAsync(ct);
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        await response.WriteStringAsync(
            $"<p><strong>Sent:</strong> {WebUtility.HtmlEncode(digest.Subject)}</p>{digest.HtmlBody}", ct);
        return response;
    }

    private async Task<Digest> RunAsync(CancellationToken ct)
    {
        var digest = await _digest.BuildAsync(ct);
        await _email.SendAsync(digest.Subject, digest.HtmlBody, ct);
        _logger.LogInformation("Digest sent: {Subject}", digest.Subject);
        return digest;
    }
}
