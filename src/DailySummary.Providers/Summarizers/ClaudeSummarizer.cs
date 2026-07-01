using System.Net.Http.Json;
using System.Text.Json;
using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Summarizers;

/// <summary>Anthropic Claude via the Messages API. API key comes from the env var named in config.</summary>
public sealed class ClaudeSummarizer : ISummarizer
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _apiKey;

    public ClaudeSummarizer(HttpClient http, SummarizerConfig config)
    {
        _http = http;
        _model = config.Model ?? "claude-haiku-4-5";
        _apiKey = Environment.GetEnvironmentVariable(config.ApiKeyEnv ?? "ANTHROPIC_API_KEY") ?? string.Empty;
    }

    public string Name => "claude";

    public async Task<string> SummarizeAsync(string prompt, string input, CancellationToken ct)
    {
        var request = new
        {
            model = _model,
            max_tokens = 1024,
            system = prompt,
            messages = new[] { new { role = "user", content = input } }
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(request) };
        msg.Headers.Add("x-api-key", _apiKey);
        msg.Headers.Add("anthropic-version", "2023-06-01");

        using var resp = await _http.SendAsync(msg, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var content = doc.RootElement.GetProperty("content");
        return content.GetArrayLength() > 0 ? content[0].GetProperty("text").GetString() ?? string.Empty : string.Empty;
    }
}
