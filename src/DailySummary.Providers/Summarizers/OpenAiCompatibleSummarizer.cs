using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;

namespace DailySummary.Providers.Summarizers;

/// <summary>
/// Any OpenAI-compatible chat endpoint: LM Studio, llama.cpp server, vLLM, OpenAI itself.
/// POSTs to {endpoint}/v1/chat/completions. Registered under the name "openai".
/// For LM Studio: endpoint "http://localhost:1234", model = the model id loaded in LM Studio.
/// </summary>
public sealed class OpenAiCompatibleSummarizer : ISummarizer
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _endpoint;
    private readonly string? _apiKey;

    public OpenAiCompatibleSummarizer(HttpClient http, SummarizerConfig config)
    {
        _http = http;
        _model = config.Model ?? "local-model";
        _endpoint = (config.Endpoint ?? "http://localhost:1234").TrimEnd('/');
        _apiKey = config.ApiKeyEnv is null ? null : Environment.GetEnvironmentVariable(config.ApiKeyEnv);
    }

    public string Name => "openai";

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

        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/v1/chat/completions")
        {
            Content = JsonContent.Create(request)
        };
        if (!string.IsNullOrEmpty(_apiKey))
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var resp = await _http.SendAsync(msg, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        // Guard the array — some OpenAI-compatible servers return an empty choices[] (content filter,
        // empty completion). Mirrors ClaudeSummarizer's length guard rather than crashing on [0].
        var choices = doc.RootElement.GetProperty("choices");
        return choices.GetArrayLength() > 0
            ? choices[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty
            : string.Empty;
    }
}
