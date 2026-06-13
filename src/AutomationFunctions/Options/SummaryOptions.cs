namespace AutomationFunctions.Options;

/// <summary>Which pages the summary job should fetch and summarize.</summary>
public class SummaryOptions
{
    public const string SectionName = "Summary";

    /// <summary>Comma-separated list of URLs (kept as a string for easy env-var config).</summary>
    public string Urls { get; set; } = "";

    public IReadOnlyList<string> UrlList =>
        Urls.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
