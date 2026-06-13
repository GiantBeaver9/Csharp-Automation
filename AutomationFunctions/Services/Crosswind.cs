namespace AutomationFunctions.Services;

/// <summary>Wind components for one runway, in knots.</summary>
public record RunwayWind(
    string Runway,
    int Heading,
    int Crosswind,
    string CrosswindSide,
    int? CrosswindGust,
    int Headwind);

/// <summary>
/// Crosswind / headwind components per runway. Wind direction and runway heading must be in
/// the same reference frame — pass a magnetic-corrected wind direction if you want magnetic
/// runway numbers to line up (METAR winds are referenced to true north).
/// </summary>
public static class Crosswind
{
    public static IReadOnlyList<RunwayWind> Components(
        int windDir, int windSpeed, int? windGust, IEnumerable<string> runways)
    {
        var list = new List<RunwayWind>();
        foreach (var runway in runways)
        {
            var heading = ParseHeading(runway);
            if (heading is null) continue;

            var rad = Normalize(windDir - heading.Value) * Math.PI / 180.0;
            var sin = Math.Sin(rad);

            var cross = (int)Math.Round(Math.Abs(windSpeed * sin));
            var gustCross = windGust is int g ? (int?)(int)Math.Round(Math.Abs(g * sin)) : null;
            var head = (int)Math.Round(windSpeed * Math.Cos(rad));
            var side = sin >= 0 ? "from the right" : "from the left";

            list.Add(new RunwayWind(
                runway.Trim().ToUpperInvariant(), heading.Value, cross, side, gustCross, head));
        }

        return list;
    }

    /// <summary>Runway identifier ("04", "13L", "31R") → magnetic heading in degrees, or null.</summary>
    private static int? ParseHeading(string runway)
    {
        var digits = new string(runway.Trim().TakeWhile(char.IsDigit).ToArray());
        if (!int.TryParse(digits, out var n) || n < 1 || n > 36) return null;
        return n * 10;
    }

    /// <summary>Wrap a signed angle into [-180, 180].</summary>
    private static int Normalize(int deg)
    {
        deg %= 360;
        if (deg > 180) deg -= 360;
        if (deg < -180) deg += 360;
        return deg;
    }
}
