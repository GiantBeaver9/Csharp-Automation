namespace AutomationFunctions.Options;

/// <summary>
/// Settings for a local, OpenAI-compatible chat completions endpoint
/// (Ollama, LM Studio, llama.cpp server, vLLM, etc.).
/// </summary>
public class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Base URL up to and including "/v1". Ollama default shown.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";

    public string Model { get; set; } = "llama3.1";

    /// <summary>Most local servers ignore this, but some require a non-empty value.</summary>
    public string ApiKey { get; set; } = "not-needed";

    public int MaxTokens { get; set; } = 1024;

    public double Temperature { get; set; } = 0.3;

    /// <summary>Input text is truncated to this many characters to fit local context windows.</summary>
    public int MaxInputChars { get; set; } = 12000;

    public int TimeoutSeconds { get; set; } = 180;
}
