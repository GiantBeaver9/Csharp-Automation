namespace AutomationFunctions.Options;

/// <summary>
/// Settings for a local, OpenAI-compatible chat completions endpoint
/// (Ollama, LM Studio, llama.cpp server, vLLM, etc.).
/// </summary>
public class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Base URL up to and including "/v1". Ollama default shown.</summary>
    public string BaseUrl { get; set; } = Constants.Llm.BaseUrl;

    public string Model { get; set; } = Constants.Llm.Model;

    /// <summary>Most local servers ignore this, but some require a non-empty value.</summary>
    public string ApiKey { get; set; } = Constants.Llm.ApiKey;

    public int MaxTokens { get; set; } = Constants.Llm.MaxTokens;

    public double Temperature { get; set; } = Constants.Llm.Temperature;

    /// <summary>Input text is truncated to this many characters to fit local context windows.</summary>
    public int MaxInputChars { get; set; } = Constants.Llm.MaxInputChars;

    public int TimeoutSeconds { get; set; } = Constants.Llm.TimeoutSeconds;
}
