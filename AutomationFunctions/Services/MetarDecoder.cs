using System.Globalization;
using System.Text.RegularExpressions;

namespace AutomationFunctions.Services;

/// <summary>Human-readable fields decoded from a raw METAR, plus numeric wind for calculations.</summary>
public record DecodedMetar(
    string? Wind,
    string? Visibility,
    string? Ceiling,
    string? Temperature,
    string? Altimeter,
    int? WindDirDeg = null,
    int? WindSpeedKt = null,
    int? WindGustKt = null)
{
    public bool HasAny =>
        Wind is not null || Visibility is not null || Ceiling is not null
        || Temperature is not null || Altimeter is not null;
}

/// <summary>
/// Decodes wind / visibility / ceiling / temperature / altimeter from a raw METAR. Parsing
/// the standardized raw text is more robust than depending on the API's individual JSON
/// fields (which can be numbers or strings like "VRB").
/// </summary>
public static partial class MetarDecoder
{
    public static DecodedMetar Decode(string raw)
    {
        var (windText, dir, spd, gust) = ParseWind(raw);
        return new DecodedMetar(
            Wind: windText,
            Visibility: DecodeVisibility(raw),
            Ceiling: DecodeCeiling(raw),
            Temperature: DecodeTemp(raw),
            Altimeter: DecodeAltimeter(raw),
            WindDirDeg: dir,
            WindSpeedKt: spd,
            WindGustKt: gust);
    }

    private static (string? Text, int? Dir, int? Spd, int? Gust) ParseWind(string raw)
    {
        var m = WindRegex().Match(raw);
        if (!m.Success) return (null, null, null, null);

        var spd = Int(m.Groups["spd"].Value);
        var gust = m.Groups["gust"].Success ? Int(m.Groups["gust"].Value) : (int?)null;
        if (spd == 0) return ("Calm", null, 0, null);

        var dirRaw = m.Groups["dir"].Value;
        int? dir = dirRaw == "VRB" ? null : Int(dirRaw);
        var dirText = dir is null ? "Variable" : $"{dir}°";
        var text = $"{dirText} at {spd} kt" + (gust is int g ? $", gusting {g} kt" : "");
        return (text, dir, spd, gust);
    }

    private static string? DecodeVisibility(string raw)
    {
        var m = VisRegex().Match(raw);
        if (!m.Success) return null;
        var vis = m.Groups["vis"].Value.Replace("SM", " SM");
        return vis.StartsWith('M') ? "less than " + vis[1..] : vis;
    }

    private static string? DecodeCeiling(string raw)
    {
        int? lowest = null;
        var cover = "";
        foreach (Match m in CeilingRegex().Matches(raw))
        {
            var h = Int(m.Groups["h"].Value) * 100;
            if (lowest is null || h < lowest)
            {
                lowest = h;
                cover = m.Groups["cov"].Value;
            }
        }

        if (lowest is int ft)
        {
            var word = cover switch
            {
                "OVC" => "overcast",
                "BKN" => "broken",
                "VV" => "indefinite",
                _ => cover,
            };
            return $"{ft:N0} ft {word}";
        }

        return SkyClearRegex().IsMatch(raw) ? "Unlimited" : null;
    }

    private static string? DecodeTemp(string raw)
    {
        var m = TempRegex().Match(raw);
        if (!m.Success) return null;
        return $"{Celsius(m.Groups["t"].Value)}°C / dew {Celsius(m.Groups["d"].Value)}°C";
    }

    private static string? DecodeAltimeter(string raw)
    {
        var a = AltInHgRegex().Match(raw);
        if (a.Success) return $"{Int(a.Groups["a"].Value) / 100.0:0.00} inHg";

        var q = AltHpaRegex().Match(raw);
        return q.Success ? $"{Int(q.Groups["q"].Value)} hPa" : null;
    }

    private static int Int(string v) => int.Parse(v, CultureInfo.InvariantCulture);

    private static int Celsius(string v) => v.StartsWith('M') ? -Int(v[1..]) : Int(v);

    [GeneratedRegex(@"(?<dir>\d{3}|VRB)(?<spd>\d{2,3})(G(?<gust>\d{2,3}))?KT")]
    private static partial Regex WindRegex();

    [GeneratedRegex(@"\b(?<vis>M?(?:\d+\s+)?\d+(?:/\d+)?SM)\b")]
    private static partial Regex VisRegex();

    [GeneratedRegex(@"\b(?<cov>BKN|OVC|VV)(?<h>\d{3})\b")]
    private static partial Regex CeilingRegex();

    [GeneratedRegex(@"\b(SKC|CLR|NSC|NCD|CAVOK|FEW\d{3}|SCT\d{3})\b")]
    private static partial Regex SkyClearRegex();

    [GeneratedRegex(@"\b(?<t>M?\d{2})/(?<d>M?\d{2})\b")]
    private static partial Regex TempRegex();

    [GeneratedRegex(@"\bA(?<a>\d{4})\b")]
    private static partial Regex AltInHgRegex();

    [GeneratedRegex(@"\bQ(?<q>\d{4})\b")]
    private static partial Regex AltHpaRegex();
}
