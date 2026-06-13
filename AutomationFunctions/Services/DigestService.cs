using System.Net;
using System.Text;
using AutomationFunctions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutomationFunctions.Services;

/// <summary>The assembled digest: a subject line and an HTML body, delivery-agnostic.</summary>
public record Digest(string Subject, string HtmlBody);

public interface IDigestService
{
    /// <summary>Runs the full pipeline (weather → website summaries → inbox scan) and builds one report.</summary>
    Task<Digest> BuildAsync(CancellationToken ct = default);
}

/// <summary>
/// Orchestrates the automation flow into a single report. It produces content only;
/// how that content is delivered (email today, app/push later) is left to the caller.
/// </summary>
public class DigestService : IDigestService
{
    private readonly IWeatherService _weather;
    private readonly IWebPageFetcher _fetcher;
    private readonly ILlmService _llm;
    private readonly IMailScanner _mailScanner;
    private readonly SummaryOptions _summaryOptions;
    private readonly ILogger<DigestService> _logger;

    public DigestService(
        IWeatherService weather,
        IWebPageFetcher fetcher,
        ILlmService llm,
        IMailScanner mailScanner,
        IOptions<SummaryOptions> summaryOptions,
        ILogger<DigestService> logger)
    {
        _weather = weather;
        _fetcher = fetcher;
        _llm = llm;
        _mailScanner = mailScanner;
        _summaryOptions = summaryOptions.Value;
        _logger = logger;
    }

    public async Task<Digest> BuildAsync(CancellationToken ct = default)
    {
        var body = new StringBuilder();

        await AppendWeatherAsync(body, ct);
        await AppendWebsiteSummariesAsync(body, ct);
        await AppendInboxSummaryAsync(body, ct);

        body.Append($"<p style=\"color:#888;font-size:12px\">Generated {DateTimeOffset.Now:f}</p>");

        return new Digest($"Daily Digest — {DateTime.Now:MMM d}", body.ToString());
    }

    private async Task AppendWeatherAsync(StringBuilder body, CancellationToken ct)
    {
        try
        {
            var w = await _weather.GetCurrentAsync(ct);
            body.Append($"""
                <h2>Weather — {Enc(w.Location)}</h2>
                <ul>
                  <li><strong>Now:</strong> {w.Temperature:0.#}{w.TemperatureUnit} ({Enc(w.Description)})</li>
                  <li><strong>Feels like:</strong> {w.ApparentTemperature:0.#}{w.TemperatureUnit}</li>
                  <li><strong>High / Low:</strong> {w.TempMax:0.#}{w.TemperatureUnit} / {w.TempMin:0.#}{w.TemperatureUnit}</li>
                  <li><strong>Humidity:</strong> {w.Humidity}% &middot; <strong>Wind:</strong> {w.WindSpeed:0.#} {Enc(w.WindSpeedUnit)}</li>
                </ul>
                """);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Weather step failed");
            body.Append("<h2>Weather</h2>").Append(Error(ex));
        }
    }

    private async Task AppendWebsiteSummariesAsync(StringBuilder body, CancellationToken ct)
    {
        body.Append("<h2>Website Summaries</h2>");

        var urls = _summaryOptions.UrlList;
        if (urls.Count == 0)
        {
            body.Append("<p>No URLs configured (set <code>Summary__Urls</code>).</p>");
            return;
        }

        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            body.Append($"<h3>{Enc(url)}</h3>");
            try
            {
                var text = await _fetcher.FetchTextAsync(url, ct);
                var summary = await _llm.SummarizeAsync(text, ct: ct);
                body.Append(ToHtml(summary));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed summarizing {Url}", url);
                body.Append(Error(ex));
            }
        }
    }

    private async Task AppendInboxSummaryAsync(StringBuilder body, CancellationToken ct)
    {
        var messages = await _mailScanner.FetchRecentAsync(ct);
        if (messages.Count == 0)
        {
            return;
        }

        body.Append("<h2>Inbox Summary</h2>");
        foreach (var mail in messages)
        {
            ct.ThrowIfCancellationRequested();

            string summary;
            if (string.IsNullOrWhiteSpace(mail.Body))
            {
                summary = "(no readable body)";
            }
            else
            {
                try
                {
                    summary = await _llm.SummarizeAsync(
                        mail.Body,
                        "Summarize this email in 1-2 sentences. Be specific and call out any action the reader needs to take.",
                        ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed summarizing email {Subject}", mail.Subject);
                    summary = "Could not summarize: " + ex.Message;
                }
            }

            body.Append($"""
                <div style="margin:0 0 12px">
                  <div><strong>{Enc(mail.Subject)}</strong></div>
                  <div style="color:#666;font-size:12px">{Enc(mail.Account)} &middot; {Enc(mail.From)} &middot; {mail.Date:MMM d, h:mm tt}</div>
                  <div>{Enc(summary)}</div>
                </div>
                """);
        }
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    private static string Error(Exception ex) =>
        $"<p style=\"color:#b00\">Failed: {WebUtility.HtmlEncode(ex.Message)}</p>";

    /// <summary>Renders plain/markdown-ish model output as minimal HTML (newlines -> breaks).</summary>
    private static string ToHtml(string text) =>
        "<p>" + WebUtility.HtmlEncode(text).Replace("\n", "<br/>") + "</p>";
}
