namespace AutomationFunctions.Options;

/// <summary>
/// Aviation weather settings. The airport list is intentionally configurable (e.g. via
/// User Secrets — <c>dotnet user-secrets set "Aviation:Airports" "KJFK,KBOS"</c>) so your
/// specific airports don't live in source.
/// </summary>
public class AviationOptions
{
    public const string SectionName = "Aviation";

    /// <summary>Comma-separated ICAO airport identifiers, e.g. "KJFK, KBOS, EGLL".</summary>
    public string Airports { get; set; } = "";

    /// <summary>Fetch the TAF (forecast) in addition to the current METAR.</summary>
    public bool IncludeTaf { get; set; } = Constants.Aviation.IncludeTaf;

    /// <summary>
    /// ICAO → comma-separated runway identifiers, e.g. <c>"KJFK" : "04,13,22,31"</c>. Used to
    /// compute crosswind. Set via config/secrets, e.g.
    /// <c>dotnet user-secrets set "Aviation:Runways:KJFK" "04,13,22,31"</c>.
    /// </summary>
    public Dictionary<string, string> Runways { get; set; } = new();

    /// <summary>
    /// Optional ICAO → magnetic variation in degrees (east positive). When set, the true METAR
    /// wind is corrected to magnetic so it lines up with runway numbers.
    /// </summary>
    public Dictionary<string, double> MagneticVariation { get; set; } = new();

    /// <summary>Parsed, upper-cased, de-duplicated ICAO codes.</summary>
    public IReadOnlyList<string> AirportList =>
        Airports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => code.ToUpperInvariant())
            .Distinct()
            .ToList();

    /// <summary>Configured runways for an airport (case-insensitive ICAO lookup).</summary>
    public IReadOnlyList<string> RunwaysFor(string icao)
    {
        var entry = Runways.FirstOrDefault(kv => string.Equals(kv.Key, icao, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(entry.Value)
            ? Array.Empty<string>()
            : entry.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Magnetic variation for an airport (east positive), or 0 if unset.</summary>
    public double VariationFor(string icao) =>
        MagneticVariation.FirstOrDefault(kv => string.Equals(kv.Key, icao, StringComparison.OrdinalIgnoreCase)).Value;
}
