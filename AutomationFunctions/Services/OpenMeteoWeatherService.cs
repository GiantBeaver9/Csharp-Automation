using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AutomationFunctions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutomationFunctions.Services;

public record WeatherReport(
    string Location,
    double Temperature,
    double ApparentTemperature,
    int Humidity,
    double WindSpeed,
    double Precipitation,
    int WeatherCode,
    string Description,
    double TempMax,
    double TempMin,
    string TemperatureUnit,
    string WindSpeedUnit);

public interface IWeatherService
{
    Task<WeatherReport> GetCurrentAsync(CancellationToken ct = default);
}

/// <summary>Free, keyless current-weather lookup via the Open-Meteo API.</summary>
public class OpenMeteoWeatherService : IWeatherService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WeatherOptions _options;
    private readonly ILogger<OpenMeteoWeatherService> _logger;

    public OpenMeteoWeatherService(
        IHttpClientFactory httpClientFactory,
        IOptions<WeatherOptions> options,
        ILogger<OpenMeteoWeatherService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WeatherReport> GetCurrentAsync(CancellationToken ct = default)
    {
        var lat = _options.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = _options.Longitude.ToString(CultureInfo.InvariantCulture);
        var url =
            $"{Constants.Weather.ApiBaseUrl}?latitude={lat}&longitude={lon}" +
            $"&current={Constants.Weather.CurrentFields}" +
            $"&daily={Constants.Weather.DailyFields}" +
            $"&temperature_unit={_options.TemperatureUnit}&wind_speed_unit={_options.WindSpeedUnit}" +
            "&timezone=auto&forecast_days=1";

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(Constants.Http.RequestTimeoutSeconds);

        _logger.LogInformation("Fetching weather for {Location}", _options.LocationName);
        var data = await client.GetFromJsonAsync<OpenMeteoResponse>(url, ct)
            ?? throw new InvalidOperationException("Open-Meteo returned no data.");

        var current = data.Current ?? throw new InvalidOperationException("Open-Meteo response missing 'current'.");
        var daily = data.Daily;

        return new WeatherReport(
            Location: _options.LocationName,
            Temperature: current.Temperature,
            ApparentTemperature: current.ApparentTemperature,
            Humidity: current.Humidity,
            WindSpeed: current.WindSpeed,
            Precipitation: current.Precipitation,
            WeatherCode: current.WeatherCode,
            Description: WeatherCodes.Describe(current.WeatherCode),
            TempMax: daily?.TempMax?.FirstOrDefault() ?? current.Temperature,
            TempMin: daily?.TempMin?.FirstOrDefault() ?? current.Temperature,
            TemperatureUnit: _options.TemperatureUnit == "celsius" ? "°C" : "°F",
            WindSpeedUnit: _options.WindSpeedUnit);
    }

    private sealed class OpenMeteoResponse
    {
        [JsonPropertyName("current")] public CurrentBlock? Current { get; set; }
        [JsonPropertyName("daily")] public DailyBlock? Daily { get; set; }
    }

    private sealed class CurrentBlock
    {
        [JsonPropertyName("temperature_2m")] public double Temperature { get; set; }
        [JsonPropertyName("apparent_temperature")] public double ApparentTemperature { get; set; }
        [JsonPropertyName("relative_humidity_2m")] public int Humidity { get; set; }
        [JsonPropertyName("wind_speed_10m")] public double WindSpeed { get; set; }
        [JsonPropertyName("precipitation")] public double Precipitation { get; set; }
        [JsonPropertyName("weather_code")] public int WeatherCode { get; set; }
    }

    private sealed class DailyBlock
    {
        [JsonPropertyName("temperature_2m_max")] public List<double>? TempMax { get; set; }
        [JsonPropertyName("temperature_2m_min")] public List<double>? TempMin { get; set; }
    }
}

/// <summary>Maps WMO weather interpretation codes to human-readable text.</summary>
public static class WeatherCodes
{
    private static readonly Dictionary<int, string> Map = new()
    {
        [0] = "Clear sky",
        [1] = "Mainly clear",
        [2] = "Partly cloudy",
        [3] = "Overcast",
        [45] = "Fog",
        [48] = "Depositing rime fog",
        [51] = "Light drizzle",
        [53] = "Moderate drizzle",
        [55] = "Dense drizzle",
        [56] = "Light freezing drizzle",
        [57] = "Dense freezing drizzle",
        [61] = "Slight rain",
        [63] = "Moderate rain",
        [65] = "Heavy rain",
        [66] = "Light freezing rain",
        [67] = "Heavy freezing rain",
        [71] = "Slight snow fall",
        [73] = "Moderate snow fall",
        [75] = "Heavy snow fall",
        [77] = "Snow grains",
        [80] = "Slight rain showers",
        [81] = "Moderate rain showers",
        [82] = "Violent rain showers",
        [85] = "Slight snow showers",
        [86] = "Heavy snow showers",
        [95] = "Thunderstorm",
        [96] = "Thunderstorm with slight hail",
        [99] = "Thunderstorm with heavy hail",
    };

    public static string Describe(int code) => Map.TryGetValue(code, out var text) ? text : $"Unknown (code {code})";
}
