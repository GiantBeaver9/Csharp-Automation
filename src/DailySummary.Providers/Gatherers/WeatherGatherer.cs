using System.Text;
using System.Text.Json;
using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Gatherers;

public sealed class WeatherSettings
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Units { get; set; } = "imperial";
}

/// <summary>Open-Meteo (free, no API key). Emits a data blob the summarizer turns into a brief.</summary>
public sealed class WeatherGatherer : ISectionGatherer
{
    private readonly HttpClient _http;

    public WeatherGatherer(HttpClient http) => _http = http;

    public SectionType Type => SectionType.Weather;

    public async Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig config, CancellationToken ct)
    {
        var s = config.SettingsAs<WeatherSettings>();
        var unit = s.Units.Equals("imperial", StringComparison.OrdinalIgnoreCase) ? "fahrenheit" : "celsius";
        var url =
            "https://api.open-meteo.com/v1/forecast" +
            $"?latitude={s.Latitude}&longitude={s.Longitude}" +
            "&current=temperature_2m,weather_code,wind_speed_10m" +
            "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,weather_code" +
            $"&timezone=auto&temperature_unit={unit}";

        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct).ConfigureAwait(false));
        var root = doc.RootElement;
        var current = root.GetProperty("current");
        var daily = root.GetProperty("daily");

        var sb = new StringBuilder();
        sb.AppendLine($"Current: {current.GetProperty("temperature_2m").GetDouble()}° " +
                      $"({Wmo(current.GetProperty("weather_code").GetInt32())}), " +
                      $"wind {current.GetProperty("wind_speed_10m").GetDouble()}.");
        sb.AppendLine($"High: {daily.GetProperty("temperature_2m_max")[0].GetDouble()}°, " +
                      $"Low: {daily.GetProperty("temperature_2m_min")[0].GetDouble()}°.");
        sb.AppendLine($"Precipitation chance: {daily.GetProperty("precipitation_probability_max")[0].GetInt32()}%.");
        sb.AppendLine($"Conditions: {Wmo(daily.GetProperty("weather_code")[0].GetInt32())}.");

        return new[] { new RawPiece(config.Order, config.Heading, null, null, sb.ToString()) };
    }

    // A trimmed WMO weather-code lookup; extend as needed.
    private static string Wmo(int code) => code switch
    {
        0 => "Clear sky",
        1 or 2 or 3 => "Partly cloudy",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        61 or 63 or 65 => "Rain",
        71 or 73 or 75 => "Snow",
        80 or 81 or 82 => "Rain showers",
        95 or 96 or 99 => "Thunderstorm",
        _ => $"Code {code}"
    };
}
