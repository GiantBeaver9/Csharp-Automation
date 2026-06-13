using System.Net;
using System.Text;
using AutomationFunctions.Options;
using AutomationFunctions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutomationFunctions.Functions;

/// <summary>
/// Fetches the configured pages, summarizes each with the local LLM, and emails a digest.
/// Runs on a timer and can also be triggered on demand (GET/POST /api/run/summary).
/// </summary>
public class WebpageSummaryFunction
{
    private readonly IWebPageFetcher _fetcher;
    private readonly ILlmService _llm;
    private readonly IEmailService _email;
    private readonly SummaryOptions _options;
    private readonly ILogger<WebpageSummaryFunction> _logger;

    public WebpageSummaryFunction(
        IWebPageFetcher fetcher,
        ILlmService llm,
        IEmailService email,
        IOptions<SummaryOptions> options,
        ILogger<WebpageSummaryFunction> logger)
    {
        _fetcher = fetcher;
        _llm = llm;
        _email = email;
        _options = options.Value;
        _logger = logger;
    }

    // Daily at 07:00 (see UTC/timezone note in WeatherReportFunction).
    [Function("WebpageSummaryTimer")]
    public async Task RunTimer(
        [TimerTrigger("0 0 7 * * *")] TimerInfo timer,
        CancellationToken ct)
    {
        await RunAsync(ct);
    }

    [Function("WebpageSummaryHttp")]
    public async Task<HttpResponseData> RunHttp(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "run/summary")] HttpRequestData req,
        CancellationToken ct)
    {
        var html = await RunAsync(ct);
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        await response.WriteStringAsync(html, ct);
        return response;
    }

    private async Task<string> RunAsync(CancellationToken ct)
    {
        var urls = _options.UrlList;
        if (urls.Count == 0)
        {
            _logger.LogWarning("No URLs configured (Summary__Urls is empty). Nothing to summarize.");
            return "<p>No URLs configured. Set <code>Summary__Urls</code>.</p>";
        }

        var body = new StringBuilder("<h2>Daily Web Summary</h2>");

        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var text = await _fetcher.FetchTextAsync(url, ct);
                var summary = await _llm.SummarizeAsync(text, ct: ct);

                body.Append($"<h3>{WebUtility.HtmlEncode(url)}</h3>");
                body.Append($"<div>{ToHtml(summary)}</div>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to summarize {Url}", url);
                body.Append($"<h3>{WebUtility.HtmlEncode(url)}</h3>");
                body.Append($"<p style=\"color:#b00\">Could not summarize: {WebUtility.HtmlEncode(ex.Message)}</p>");
            }
        }

        body.Append($"<p style=\"color:#888;font-size:12px\">Generated {DateTimeOffset.Now:f}</p>");
        var html = body.ToString();

        await _email.SendAsync($"Daily Web Summary — {DateTime.Now:MMM d}", html, ct);
        _logger.LogInformation("Summary digest sent for {Count} page(s)", urls.Count);
        return html;
    }

    /// <summary>Renders plain/markdown-ish model output as minimal HTML (newlines -> breaks).</summary>
    private static string ToHtml(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        return "<p>" + encoded.Replace("\n", "<br/>") + "</p>";
    }
}
