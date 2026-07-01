using System.Net.Http.Json;
using System.Text.Json;
using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Summarizers;

/// <summary>Local LLM via the Ollama chat API. Tool/MCP use (for prompt sections) is configured in the Ollama runtime.</summary>
public sealed class OllamaSummarizer : ISummarizer
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _endpoint;

    public OllamaSummarizer(HttpClient http, SummarizerConfig config)
    {
        _http = http;
        _model = config.Model ?? "llama3.1";
        _endpoint = (config.Endpoint ?? "http://localhost:11434").TrimEnd('/');
    }

    public string Name => "ollama";

    public async Task<string> SummarizeAsync(string prompt, string input, CancellationToken ct)
    {
        var request = new
        {
            model = _model,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = prompt },
                new { role = "user", content = input }
            }
        };

        using var resp = await _http.PostAsJsonAsync($"{_endpoint}/api/chat", request, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }
}
