using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AutomationFunctions.Services;

public interface IWebPageFetcher
{
    /// <summary>Downloads a page and returns its visible text with markup stripped.</summary>
    Task<string> FetchTextAsync(string url, CancellationToken ct = default);
}

public partial class WebPageFetcher : IWebPageFetcher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebPageFetcher> _logger;

    public WebPageFetcher(IHttpClientFactory httpClientFactory, ILogger<WebPageFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> FetchTextAsync(string url, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(Constants.Http.RequestTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.Http.UserAgent);

        _logger.LogInformation("Fetching {Url}", url);
        var html = await client.GetStringAsync(url, ct);
        return HtmlToText(html);
    }

    /// <summary>
    /// Lightweight HTML-to-text: drops script/style, strips tags, decodes entities,
    /// and collapses whitespace. Good enough to feed a summarizer without extra deps.
    /// </summary>
    private static string HtmlToText(string html)
    {
        var noScript = ScriptStyleRegex().Replace(html, " ");
        var noTags = TagRegex().Replace(noScript, " ");
        var decoded = WebUtility.HtmlDecode(noTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptStyleRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
