using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutomationFunctions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutomationFunctions.Services;

public interface ILlmService
{
    /// <summary>Sends a system + user prompt to the local model and returns the reply text.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

    /// <summary>Convenience wrapper that summarizes a block of text.</summary>
    Task<string> SummarizeAsync(string text, string? instruction = null, CancellationToken ct = default);
}

/// <summary>
/// Talks to any OpenAI-compatible /chat/completions endpoint (Ollama, LM Studio, vLLM, ...).
/// </summary>
public class OpenAiCompatibleLlmService : ILlmService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmOptions _options;
    private readonly ILogger<OpenAiCompatibleLlmService> _logger;

    public OpenAiCompatibleLlmService(
        IHttpClientFactory httpClientFactory,
        IOptions<LlmOptions> options,
        ILogger<OpenAiCompatibleLlmService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public Task<string> SummarizeAsync(string text, string? instruction = null, CancellationToken ct = default)
    {
        var system = instruction
            ?? "You are a concise assistant. Summarize the content the user provides in a few clear "
             + "bullet points, capturing the most important information. Avoid boilerplate and navigation text.";
        return CompleteAsync(system, Truncate(text), ct);
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        var request = new ChatRequest
        {
            Model = _options.Model,
            Temperature = _options.Temperature,
            MaxTokens = _options.MaxTokens,
            Stream = false,
            Messages =
            [
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = userPrompt },
            ],
        };

        _logger.LogInformation("Calling local model {Model} at {BaseUrl}", _options.Model, _options.BaseUrl);

        using var response = await client.PostAsJsonAsync("chat/completions", request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"LLM request failed ({(int)response.StatusCode} {response.StatusCode}): {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
        var content = result?.Choices?.FirstOrDefault()?.Message?.Content;
        return string.IsNullOrWhiteSpace(content) ? "(model returned no content)" : content.Trim();
    }

    private string Truncate(string text) =>
        text.Length <= _options.MaxInputChars ? text : text[.._options.MaxInputChars];

    // ----- OpenAI chat completions DTOs -----

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = [];
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("stream")] public bool Stream { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }
}
