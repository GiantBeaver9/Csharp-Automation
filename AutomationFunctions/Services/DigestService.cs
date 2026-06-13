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
    /// <summary>Runs the full pipeline (weather → aviation → website summaries → inbox scan) and builds one report.</summary>
    Task<Digest> BuildAsync(CancellationToken ct = default);
}

/// <summary>
/// Orchestrates the automation flow into a single report. It produces content only;
/// how that content is delivered (email today, app/push later) is left to the caller.
/// </summary>
public class DigestService : IDigestService
{
    private readonly IWeatherService _weather;
    private readonly IAviationWeatherService _aviation;
    private readonly IWebPageFetcher _fetcher;
    private readonly ILlmService _llm;
    private readonly IMailScanner _mailScanner;
    private readonly SummaryOptions _summaryOptions;
    private readonly ILogger<DigestService> _logger;

    public DigestService(
        IWeatherService weather,
        IAviationWeatherService aviation,
        IWebPageFetcher fetcher,
        ILlmService llm,
        IMailScanner mailScanner,
        IOptions<SummaryOptions> summaryOptions,
        ILogger<DigestService> logger)
    {
        _weather = weather;
        _aviation = aviation;
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
        await AppendAviationAsync(body, ct);
        await AppendWebsiteSummariesAsync(body, ct);
        await AppendInboxSummaryAsync(body, ct);

        body.Append($"<p style=\"color:{Constants.Colors.Muted};font-size:12px\">Generated {DateTimeOffset.Now:f}</p>");

        // Wrap in a base style — email clients only honor inline CSS.
        var html =
            "<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.45;color:#222\">"
            + body + "</div>";

        return new Digest($"{Constants.Digest.SubjectPrefix} — {DateTime.Now:MMM d}", html);
    }

    private async Task AppendWeatherAsync(StringBuilder body, CancellationToken ct)
    {
        try
        {
            var w = await _weather.GetCurrentAsync(ct);
            var isCelsius = w.TemperatureUnit.Contains('C');
            string Temp(double t) => WeatherHighlighter.Temperature(t, w.TemperatureUnit, isCelsius);

            body.Append($"""
                <h2>Weather — {Enc(w.Location)}</h2>
                <ul>
                  <li><strong>Now:</strong> {Temp(w.Temperature)} ({HtmlFormat.Bold(w.Description)})</li>
                  <li><strong>Feels like:</strong> {Temp(w.ApparentTemperature)}</li>
                  <li><strong>High / Low:</strong> {Temp(w.TempMax)} / {Temp(w.TempMin)}</li>
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

    private async Task AppendAviationAsync(StringBuilder body, CancellationToken ct)
    {
        IReadOnlyList<AirportWeather> airports;
        try
        {
            airports = await _aviation.GetAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Aviation weather step failed");
            body.Append("<h2>Aviation Weather</h2>").Append(Error(ex));
            return;
        }

        if (airports.Count == 0)
        {
            return; // none configured
        }

        body.Append("<h2>Aviation Weather</h2>");
        foreach (var a in airports)
        {
            var heading = string.IsNullOrWhiteSpace(a.Name) ? a.Icao : $"{a.Icao} — {a.Name}";
            body.Append($"<h3>{Enc(heading)}</h3>");

            if (!string.IsNullOrWhiteSpace(a.FlightCategory))
            {
                body.Append(
                    $"<p><strong style=\"color:{FlightCategory.Color(a.FlightCategory)}\">{Enc(a.FlightCategory)}</strong></p>");
            }

            if (!string.IsNullOrWhiteSpace(a.RawMetar))
            {
                body.Append(
                    $"<p style=\"margin:2px 0\"><strong>METAR:</strong> <code>{WeatherHighlighter.Metar(a.RawMetar)}</code></p>");
            }
            else
            {
                body.Append($"<p style=\"color:{Constants.Colors.Storm}\">No current METAR available.</p>");
            }

            if (!string.IsNullOrWhiteSpace(a.RawTaf))
            {
                body.Append(
                    $"<p style=\"margin:2px 0\"><strong>TAF:</strong> <code>{WeatherHighlighter.Metar(a.RawTaf)}</code></p>");
            }
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
                    summary = await _llm.SummarizeAsync(mail.Body, Constants.Llm.EmailSummaryPrompt, ct);
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
