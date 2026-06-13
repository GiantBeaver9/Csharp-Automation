namespace AutomationFunctions;

/// <summary>
/// Default values and fixed operational constants, grouped by area. The <c>Options</c>
/// classes seed their properties from these (so config can still override them), and
/// services reference the non-configurable ones (API URLs, prompts, timeouts).
/// </summary>
public static class Constants
{
    /// <summary>Local, OpenAI-compatible LLM defaults and prompts.</summary>
    public static class Llm
    {
        public const string BaseUrl = "http://localhost:11434/v1";
        public const string Model = "llama3.1";
        public const string ApiKey = "not-needed";
        public const int MaxTokens = 1024;
        public const double Temperature = 0.3;
        public const int MaxInputChars = 12_000;
        public const int TimeoutSeconds = 180;

        /// <summary>Path appended to <see cref="BaseUrl"/> for chat completions.</summary>
        public const string ChatCompletionsPath = "chat/completions";

        public const string SummarySystemPrompt =
            "You are a concise assistant. Summarize the content the user provides in a few clear "
            + "bullet points, capturing the most important information. Avoid boilerplate and navigation text.";

        public const string EmailSummaryPrompt =
            "Summarize this email in 1-2 sentences. Be specific and call out any action the reader needs to take.";
    }

    /// <summary>SMTP send defaults (Gmail-oriented; overridable per provider).</summary>
    public static class Email
    {
        public const string SmtpHost = "smtp.gmail.com";
        public const int SmtpPort = 587;
        public const bool UseStartTls = true;
        public const string FromName = "Automation Bot";
    }

    /// <summary>Open-Meteo location/unit defaults and API shape.</summary>
    public static class Weather
    {
        public const double Latitude = 40.7128;
        public const double Longitude = -74.0060;
        public const string LocationName = "New York";
        public const string TemperatureUnit = "fahrenheit";
        public const string WindSpeedUnit = "mph";

        public const string ApiBaseUrl = "https://api.open-meteo.com/v1/forecast";
        public const string CurrentFields =
            "temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m";
        public const string DailyFields = "temperature_2m_max,temperature_2m_min";

        // Highlight thresholds: the digest colors temps at/above Hot red, at/below Cold blue.
        public const double HotF = 85;
        public const double ColdF = 32;
        public const double HotC = 29;
        public const double ColdC = 0;
    }

    /// <summary>Inline colors used to highlight the email (tweak in one place).</summary>
    public static class Colors
    {
        public const string Hot = "#cc1111";       // red — high temps
        public const string Cold = "#1144cc";      // blue — freezing temps
        public const string Ice = "#1144cc";       // blue — icing / freezing precip / snow
        public const string Storm = "#cc1111";     // red — thunderstorms / hail
        public const string Caution = "#cc7a00";   // orange — wind gusts
        public const string Highlight = "#fff3b0"; // yellow background — emphasis
        public const string Muted = "#666666";

        // Flight categories (standard aviation convention)
        public const string Vfr = "#118811";   // green
        public const string Mvfr = "#1144cc";  // blue
        public const string Ifr = "#cc1111";   // red
        public const string Lifr = "#cc11cc";  // magenta
    }

    /// <summary>Aviation weather (METAR/TAF) defaults — FAA/NOAA Aviation Weather Center.</summary>
    public static class Aviation
    {
        /// <summary>Whether to also fetch the TAF (terminal forecast) alongside the METAR.</summary>
        public const bool IncludeTaf = true;

        /// <summary>Base of the keyless AWC data API; "/metar" and "/taf" are appended.</summary>
        public const string ApiBaseUrl = "https://aviationweather.gov/api/data";
    }

    /// <summary>IMAP inbox-scan defaults.</summary>
    public static class MailScan
    {
        public const bool Enabled = false;
        public const int LookbackHours = 24;
        public const int MaxPerAccount = 15;
        public const int ImapPort = 993;
        public const bool UseSsl = true;
    }

    /// <summary>Shared HTTP client constants for outbound fetches.</summary>
    public static class Http
    {
        public const int RequestTimeoutSeconds = 30;
        public const string UserAgent = "Mozilla/5.0 (compatible; CsharpAutomationBot/1.0)";
    }

    /// <summary>Digest presentation defaults.</summary>
    public static class Digest
    {
        public const string SubjectPrefix = "Daily Digest";
    }
}
