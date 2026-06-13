namespace AutomationFunctions.Options;

/// <summary>Open-Meteo location/unit settings (no API key required).</summary>
public class WeatherOptions
{
    public const string SectionName = "Weather";

    public double Latitude { get; set; } = 40.7128;

    public double Longitude { get; set; } = -74.0060;

    public string LocationName { get; set; } = "New York";

    /// <summary>"fahrenheit" or "celsius".</summary>
    public string TemperatureUnit { get; set; } = "fahrenheit";

    /// <summary>"mph", "kmh", "ms", or "kn".</summary>
    public string WindSpeedUnit { get; set; } = "mph";
}
