using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AutomationFunctions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutomationFunctions.Services;

/// <summary>Current observation + forecast for one airport, raw strings plus flight category.</summary>
public record AirportWeather(string Icao, string? Name, string? FlightCategory, string? RawMetar, string? RawTaf);

public interface IAviationWeatherService
{
    /// <summary>Fetches METAR (and optionally TAF) for the configured airports.</summary>
    Task<IReadOnlyList<AirportWeather>> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// Keyless aviation weather via the FAA/NOAA Aviation Weather Center data API.
/// Binds defensively to a few stable string fields and surfaces the raw METAR/TAF.
/// </summary>
public class AviationWeatherService : IAviationWeatherService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AviationOptions _options;
    private readonly ILogger<AviationWeatherService> _logger;

    public AviationWeatherService(
        IHttpClientFactory httpClientFactory,
        IOptions<AviationOptions> options,
        ILogger<AviationWeatherService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AirportWeather>> GetAsync(CancellationToken ct = default)
    {
        var airports = _options.AirportList;
        if (airports.Count == 0)
        {
            return Array.Empty<AirportWeather>();
        }

        var ids = string.Join(",", airports);
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(Constants.Http.RequestTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.Http.UserAgent);

        _logger.LogInformation("Fetching aviation weather for {Airports}", ids);

        var metars = await GetJsonAsync<List<MetarDto>>(
            client, $"{Constants.Aviation.ApiBaseUrl}/metar?ids={ids}&format=json", ct) ?? new();

        var tafs = _options.IncludeTaf
            ? await GetJsonAsync<List<TafDto>>(
                client, $"{Constants.Aviation.ApiBaseUrl}/taf?ids={ids}&format=json", ct) ?? new()
            : new();

        var results = new List<AirportWeather>();
        foreach (var code in airports)
        {
            var metar = metars.FirstOrDefault(m => string.Equals(m.IcaoId, code, StringComparison.OrdinalIgnoreCase));
            var taf = tafs.FirstOrDefault(t => string.Equals(t.IcaoId, code, StringComparison.OrdinalIgnoreCase));
            results.Add(new AirportWeather(code, metar?.Name, metar?.FltCat, metar?.RawOb, taf?.RawTaf));
        }

        return results;
    }

    private async Task<T?> GetJsonAsync<T>(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            return await client.GetFromJsonAsync<T>(url, ct);
        }
        catch (Exception ex)
        {
            // A bad airport code or an API hiccup shouldn't sink the digest.
            _logger.LogError(ex, "Aviation weather request failed: {Url}", url);
            return default;
        }
    }

    private sealed class MetarDto
    {
        [JsonPropertyName("icaoId")] public string? IcaoId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("rawOb")] public string? RawOb { get; set; }
        [JsonPropertyName("fltCat")] public string? FltCat { get; set; }
    }

    private sealed class TafDto
    {
        [JsonPropertyName("icaoId")] public string? IcaoId { get; set; }
        [JsonPropertyName("rawTAF")] public string? RawTaf { get; set; }
    }
}

/// <summary>Color coding for the standard flight categories (VFR/MVFR/IFR/LIFR).</summary>
public static class FlightCategory
{
    public static string Color(string? category) => category?.ToUpperInvariant() switch
    {
        "VFR" => Constants.Colors.Vfr,
        "MVFR" => Constants.Colors.Mvfr,
        "IFR" => Constants.Colors.Ifr,
        "LIFR" => Constants.Colors.Lifr,
        _ => Constants.Colors.Muted,
    };
}
