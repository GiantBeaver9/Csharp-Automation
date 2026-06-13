using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AutomationFunctions.Services;

/// <summary>
/// Inline-CSS HTML helpers for emails. Email clients (Gmail/Outlook) only reliably honor
/// inline styles, so everything here emits <c>style="..."</c> attributes rather than CSS rules.
/// Leaf helpers HTML-encode their text argument; callers compose the returned fragments.
/// </summary>
public static class HtmlFormat
{
    public static string Enc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    public static string Bold(string s) => $"<strong>{Enc(s)}</strong>";

    public static string Color(string s, string color) => $"<span style=\"color:{color}\">{Enc(s)}</span>";

    public static string ColorBold(string s, string color) =>
        $"<strong style=\"color:{color}\">{Enc(s)}</strong>";

    /// <summary>Background highlight (the "marker pen" effect) for emphasis.</summary>
    public static string Highlight(string s, string background = Constants.Colors.Highlight) =>
        $"<span style=\"background-color:{background};padding:0 2px\">{Enc(s)}</span>";

    /// <summary>Wraps already-rendered HTML fragments in a bullet list.</summary>
    public static string Bullets(IEnumerable<string> itemsHtml) =>
        "<ul>" + string.Concat(itemsHtml.Select(i => $"<li>{i}</li>")) + "</ul>";
}

/// <summary>
/// Rule-based highlighting for weather content: temperature thresholds and significant-weather
/// tokens in raw METAR/TAF. Rules live here so they're easy to extend.
/// </summary>
public static partial class WeatherHighlighter
{
    /// <summary>Color for a temperature, or null if it's in the comfortable range.</summary>
    public static string? TempColor(double temp, bool isCelsius)
    {
        var hot = isCelsius ? Constants.Weather.HotC : Constants.Weather.HotF;
        var cold = isCelsius ? Constants.Weather.ColdC : Constants.Weather.ColdF;
        if (temp >= hot) return Constants.Colors.Hot;
        if (temp <= cold) return Constants.Colors.Cold;
        return null;
    }

    /// <summary>Renders "{temp}{unit}" colored + bold when hot/cold, plain otherwise.</summary>
    public static string Temperature(double temp, string unit, bool isCelsius)
    {
        var text = $"{temp:0.#}{unit}";
        var color = TempColor(temp, isCelsius);
        return color is null ? HtmlFormat.Enc(text) : HtmlFormat.ColorBold(text, color);
    }

    /// <summary>
    /// HTML-encodes a raw METAR/TAF and colors significant-weather tokens: freezing precip /
    /// icing and snow blue, thunderstorms/hail red, wind gusts orange.
    /// </summary>
    public static string Metar(string raw)
    {
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            var color = ClassifyToken(tokens[i]);
            sb.Append(color is null
                ? HtmlFormat.Enc(tokens[i])
                : HtmlFormat.ColorBold(tokens[i], color));
        }
        return sb.ToString();
    }

    private static string? ClassifyToken(string token)
    {
        var t = token.ToUpperInvariant();

        // Freezing precip (icing risk), ice pellets, and snow -> blue.
        if (FreezingRegex().IsMatch(t) || t.Contains("PL") || t.Contains("SN") || t.Contains("SG"))
        {
            return Constants.Colors.Ice;
        }

        // Thunderstorms, funnel clouds, hail -> red.
        if (t.Contains("TS") || t.Contains("FC") || t.Contains("GR"))
        {
            return Constants.Colors.Storm;
        }

        // Wind gusts (e.g. 26015G25KT) -> orange.
        if (GustRegex().IsMatch(t))
        {
            return Constants.Colors.Caution;
        }

        return null;
    }

    [GeneratedRegex(@"FZ(RA|DZ|FG)")] private static partial Regex FreezingRegex();
    [GeneratedRegex(@"G\d{2,3}KT")] private static partial Regex GustRegex();
}
