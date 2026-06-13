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

    /// <summary>Parsed, upper-cased, de-duplicated ICAO codes.</summary>
    public IReadOnlyList<string> AirportList =>
        Airports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => code.ToUpperInvariant())
            .Distinct()
            .ToList();
}
