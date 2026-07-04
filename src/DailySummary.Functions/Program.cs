using System.Text.Json;
using System.Text.Json.Serialization;
using DailySummary.Core;
using DailySummary.Core.Abstractions;
using DailySummary.Core.Models;
using DailySummary.Core.Pipeline;
using DailySummary.Core.Rendering;
using DailySummary.Core.Summarization;
using DailySummary.Providers.Delivery;
using DailySummary.Providers.Fetching;
using DailySummary.Providers.Gatherers;
using DailySummary.Providers.Search;
using DailySummary.Providers.Summarizers;
using DailySummary.Providers.Transcription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        var app = LoadAppConfig();
        services.AddSingleton(app);
        services.AddSingleton(app.Browser);
        services.AddSingleton<HttpClient>();

        // Summarizers (named): claude, ollama, none.
        services.AddSingleton<ISummarizer>(sp =>
            new OllamaSummarizer(sp.GetRequiredService<HttpClient>(), Cfg(app, "ollama")));
        services.AddSingleton<ISummarizer>(sp =>
            new ClaudeSummarizer(sp.GetRequiredService<HttpClient>(), Cfg(app, "claude")));
        services.AddSingleton<ISummarizer>(sp =>
            new OpenAiCompatibleSummarizer(sp.GetRequiredService<HttpClient>(), Cfg(app, "openai")));
        services.AddSingleton<ISummarizer, PassthroughSummarizer>();
        services.AddSingleton<SummarizerRegistry>();
        services.AddSingleton<SectionSummarizers>();
        services.AddSingleton<ChunkedSummarizer>();

        // Fetching / search / transcription.
        services.AddSingleton<IPageFetcher, PlaywrightPageFetcher>();
        services.AddSingleton<IWebSearch, DuckDuckGoSearch>();
        services.AddSingleton<ITranscriber, WhisperTranscriber>();

        // Gatherers (one per SectionType) → registry.
        services.AddSingleton<ISectionGatherer, WeatherGatherer>();
        services.AddSingleton<ISectionGatherer, WebGatherer>();
        services.AddSingleton<ISectionGatherer, RssGatherer>();
        services.AddSingleton<ISectionGatherer, PodcastGatherer>();
        services.AddSingleton<ISectionGatherer, SqlGatherer>();
        services.AddSingleton<ISectionGatherer, GoogleCalendarGatherer>();
        services.AddSingleton<ISectionGatherer, QuestionGatherer>();
        services.AddSingleton<ISectionGatherer, EmailGatherer>();
        services.AddSingleton<ISectionGatherer, PromptGatherer>();
        services.AddSingleton<SectionGathererRegistry>();

        // Pipeline / render / deliver / orchestrate.
        services.AddSingleton<GatherSummarizePipeline>();
        services.AddSingleton<ISummaryRenderer, SummaryRenderer>();
        services.AddSingleton<IDeliveryChannel, ConsoleDelivery>();
        services.AddSingleton<IDeliveryChannel, MarkdownFileDelivery>();
        services.AddSingleton<IDeliveryChannel, EmailDelivery>();
        services.AddSingleton<IDeliveryChannel, TelegramDelivery>();
        services.AddSingleton<ISummaryOrchestrator, SummaryOrchestrator>();
    })
    .Build();

host.Run();

static SummarizerConfig Cfg(AppConfig app, string name) =>
    app.Summarizers.TryGetValue(name, out var c) ? c : new SummarizerConfig();

static AppConfig LoadAppConfig()
{
    var path = Path.Combine(AppContext.BaseDirectory, "app.json");
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<AppConfig>(json, options)
        ?? throw new InvalidOperationException("app.json could not be parsed.");
}
