namespace AutomationFunctions.Options;

/// <summary>Open-Meteo location/unit settings (no API key required).</summary>
public class WeatherOptions
{
    public const string SectionName = "Weather";

    public double Latitude { get; set; } = Constants.Weather.Latitude;

    public double Longitude { get; set; } = Constants.Weather.Longitude;

    public string LocationName { get; set; } = Constants.Weather.LocationName;

    /// <summary>"fahrenheit" or "celsius".</summary>
    public string TemperatureUnit { get; set; } = Constants.Weather.TemperatureUnit;

    /// <summary>"mph", "kmh", "ms", or "kn".</summary>
    public string WindSpeedUnit { get; set; } = Constants.Weather.WindSpeedUnit;
}
