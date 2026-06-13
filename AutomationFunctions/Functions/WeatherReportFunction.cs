using System.Net;
using AutomationFunctions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AutomationFunctions.Functions;

/// <summary>
/// Emails a daily weather report. Runs on a timer and can also be triggered on demand
/// (GET/POST /api/run/weather) for testing.
/// </summary>
public class WeatherReportFunction
{
    private readonly IWeatherService _weather;
    private readonly IEmailService _email;
    private readonly ILogger<WeatherReportFunction> _logger;

    public WeatherReportFunction(
        IWeatherService weather,
        IEmailService email,
        ILogger<WeatherReportFunction> logger)
    {
        _weather = weather;
        _email = email;
        _logger = logger;
    }

    // Daily at 06:30. NCRONTAB: {sec} {min} {hour} {day} {month} {day-of-week}. Times are UTC
    // unless WEBSITE_TIME_ZONE (Windows) / TZ (Linux) is configured.
    [Function("WeatherReportTimer")]
    public async Task RunTimer(
        [TimerTrigger("0 30 6 * * *")] TimerInfo timer,
        CancellationToken ct)
    {
        await RunAsync(ct);
    }

    [Function("WeatherReportHttp")]
    public async Task<HttpResponseData> RunHttp(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "run/weather")] HttpRequestData req,
        CancellationToken ct)
    {
        var (subject, html) = await RunAsync(ct);
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        await response.WriteStringAsync($"<p><strong>Sent:</strong> {subject}</p>{html}", ct);
        return response;
    }

    private async Task<(string Subject, string Html)> RunAsync(CancellationToken ct)
    {
        var report = await _weather.GetCurrentAsync(ct);

        var subject = $"Weather for {report.Location}: {report.Description}, " +
                      $"{Math.Round(report.Temperature)}{report.TemperatureUnit}";

        var html =
            $"""
            <h2>Weather — {WebUtility.HtmlEncode(report.Location)}</h2>
            <ul>
              <li><strong>Now:</strong> {report.Temperature:0.#}{report.TemperatureUnit} ({WebUtility.HtmlEncode(report.Description)})</li>
              <li><strong>Feels like:</strong> {report.ApparentTemperature:0.#}{report.TemperatureUnit}</li>
              <li><strong>High / Low:</strong> {report.TempMax:0.#}{report.TemperatureUnit} / {report.TempMin:0.#}{report.TemperatureUnit}</li>
              <li><strong>Humidity:</strong> {report.Humidity}%</li>
              <li><strong>Wind:</strong> {report.WindSpeed:0.#} {WebUtility.HtmlEncode(report.WindSpeedUnit)}</li>
              <li><strong>Precipitation:</strong> {report.Precipitation:0.#} mm</li>
            </ul>
            <p style="color:#888;font-size:12px">Generated {DateTimeOffset.Now:f} · data from Open-Meteo</p>
            """;

        await _email.SendAsync(subject, html, ct);
        _logger.LogInformation("Weather report sent for {Location}", report.Location);
        return (subject, html);
    }
}
