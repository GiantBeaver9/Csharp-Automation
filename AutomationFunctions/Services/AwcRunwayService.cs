using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AutomationFunctions.Services;

/// <summary>Runway ends and magnetic variation for one airport.</summary>
public record AirportRunways(string Icao, double? MagneticVariation, IReadOnlyList<string> Runways);

public interface IRunwayService
{
    /// <summary>Looks up runways (and magnetic variation) for the given airports.</summary>
    Task<IReadOnlyDictionary<string, AirportRunways>> GetAsync(
        IEnumerable<string> icaos, CancellationToken ct = default);
}

/// <summary>
/// Fetches runway data from the keyless FAA/NOAA Aviation Weather Center "airport" endpoint and
/// caches it in memory (runway data is effectively static, so we only fetch each airport once).
///
/// NOTE: this endpoint could not be reached from the build environment, so the JSON field names
/// below follow the documented schema and may need a small tweak after a live test.
/// </summary>
public class AwcRunwayService : IRunwayService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AwcRunwayService> _logger;
    private readonly ConcurrentDictionary<string, AirportRunways> _cache = new(StringComparer.OrdinalIgnoreCase);

    public AwcRunwayService(IHttpClientFactory httpClientFactory, ILogger<AwcRunwayService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, AirportRunways>> GetAsync(
        IEnumerable<string> icaos, CancellationToken ct = default)
    {
        var codes = icaos.Select(c => c.ToUpperInvariant()).Distinct().ToList();
        var missing = codes.Where(c => !_cache.ContainsKey(c)).ToList();

        if (missing.Count > 0)
        {
            try
            {
                await FetchAndCacheAsync(missing, ct);
            }
            catch (Exception ex)
            {
                // Crosswind is a nice-to-have; never let a lookup failure break the digest.
                _logger.LogError(ex, "Runway lookup failed for {Airports}", string.Join(",", missing));
            }
        }

        return codes
            .Where(_cache.ContainsKey)
            .ToDictionary(c => c, c => _cache[c], StringComparer.OrdinalIgnoreCase);
    }

    private async Task FetchAndCacheAsync(List<string> missing, CancellationToken ct)
    {
        var ids = string.Join(",", missing);
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(Constants.Http.RequestTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.Http.UserAgent);

        var url = $"{Constants.Aviation.ApiBaseUrl}/airport?ids={ids}&format=json";
        _logger.LogInformation("Fetching runway data for {Airports}", ids);

        var airports = await client.GetFromJsonAsync<List<AirportDto>>(url, ct) ?? new();

        foreach (var dto in airports)
        {
            if (string.IsNullOrWhiteSpace(dto.IcaoId))
            {
                continue;
            }

            var ends = (dto.Runways ?? new())
                .SelectMany(r => (r.Id ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _cache[dto.IcaoId] = new AirportRunways(dto.IcaoId, ParseMagdec(dto.Magdec), ends);
        }

        // Cache negatives so we don't re-query airports the API didn't return.
        foreach (var code in missing.Where(c => !_cache.ContainsKey(c)))
        {
            _cache[code] = new AirportRunways(code, null, Array.Empty<string>());
        }
    }

    /// <summary>Magnetic declination as east-positive degrees. Handles "13W"/"13E"/"-13"/number.</summary>
    private static double? ParseMagdec(JsonElement? element)
    {
        if (element is not JsonElement el)
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number)
        {
            return el.GetDouble();
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString()?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(s))
            {
                return null;
            }

            var number = new string(s.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                if (s.EndsWith('W')) return -Math.Abs(v);
                if (s.EndsWith('E')) return Math.Abs(v);
                return v;
            }
        }

        return null;
    }

    private sealed class AirportDto
    {
        [JsonPropertyName("icaoId")] public string? IcaoId { get; set; }
        [JsonPropertyName("magdec")] public JsonElement? Magdec { get; set; }
        [JsonPropertyName("runways")] public List<RunwayDto>? Runways { get; set; }
    }

    private sealed class RunwayDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }
}
