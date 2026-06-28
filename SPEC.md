# Daily Summary Orchestrator — Design Spec

> "Morning newspaper" delivered automatically every day.

## 1. Purpose

A C# application that, once a day, assembles a personalized **daily summary**
("morning newspaper") and delivers it. It runs as a **timer-triggered Azure
Function packaged in a Docker container** — chosen over a plain Docker worker
because the Azure Functions model is the more transferable, production-shaped
experience for a real workplace, while still running locally for development.

The summary is built from four configurable sections:

1. **Weather** for the day (location configured in `app.json`).
2. **News** — fetch configured news sites and have an LLM summarize them.
3. **Misc site updates** — fetch configured misc sites and have a (local) LLM summarize.
4. **To-dos / calendar** — pull today's events/tasks (SQL or Google Calendar) into a to-do list.

Everything external (LLM, delivery, to-do source) sits behind a small interface
selected via config, so the whole app runs locally with **zero cloud credentials**
and each section can be upgraded to a real service independently.

## 2. Key decisions

| Concern   | Decision | Rationale |
|-----------|----------|-----------|
| Hosting   | Timer-triggered (CRON) **Azure Function**, containerized via Docker | Production-shaped, transferable workplace experience; runs locally too. *(confirmed)* |
| LLM       | Pluggable `ISummarizer` — **Claude** (Anthropic API) + **Ollama** (local), chosen in `app.json` | Matches "LLM summarize" for news and "local llm" for misc; both behind one seam |
| Delivery  | Pluggable `IDeliveryChannel` — **Markdown file + console** first; Email/Slack later | Testable end-to-end with zero external accounts |
| To-dos    | Pluggable `ITodoSource` — **local SQL** first, **Google Calendar** behind the same interface | Self-contained first; real calendar later without core changes |
| Weather   | **Open-Meteo** (free, no API key) | Happy path needs no secrets |

> The LLM / delivery / to-do defaults are recommendations. All three are
> pluggable, so changing a default later is a one-line config swap.

## 3. Architecture

```
Azure Functions Host (Docker)
  └─ DailySummaryFunction        ← [TimerTrigger("0 0 6 * * *")]  (06:00 daily)
        └─ ISummaryOrchestrator.RunAsync()
              ├─ IWeatherProvider        → Open-Meteo HTTP API → weather section
              ├─ INewsProvider           → IPageFetcher (Playwright) ─┐
              ├─ IMiscUpdatesProvider    → IPageFetcher (Playwright) ─┤→ ISummarizer (Claude | Ollama)
              ├─ ITodoSource (SQL|GCal)  → to-do list                 ┘
              └─ ISummaryRenderer        → builds the "newspaper" document
                    └─ IDeliveryChannel  (Markdown/console | Email | Slack)
```

The news and misc providers do **not** fetch HTML with `HttpClient` — they take an
`IPageFetcher` (Playwright / headless Chromium) so JS-rendered home pages are
captured as real rendered text, then pass that text to the `ISummarizer`.

Core principle: the **orchestrator depends only on interfaces**; concrete
implementations are registered in DI and selected by `app.json`. Each section is
independently toggleable and **failure-isolated** — a dead news site must not sink
the weather section.

### Orchestration flow (`SummaryOrchestrator.RunAsync`)

Three explicit phases, run in order:

1. **Gather** — collect all raw inputs for every enabled section:
   weather (Open-Meteo), news + misc page text (Playwright `IPageFetcher`),
   and to-dos (SQL / Google Calendar). Raw, un-summarized.
2. **Summarize** — cycle each gathered part through the `ISummarizer` one at a
   time (weather phrased into a blurb, each news/misc page condensed, to-dos
   tidied into a list). The summarizer used per section is the one named in that
   section's config.
3. **Render & deliver** — `ISummaryRenderer` assembles the summarized parts into
   one "newspaper" document, then `IDeliveryChannel` either **returns** it
   (console / function output) or **writes the Markdown file** to the path from
   `app.json` (`delivery.outputDir`, dated filename).

Each section's gather + summarize is wrapped so a failure degrades to a
"section unavailable" note rather than aborting the whole run.

> **Summarization boundary.** The LLM only ever summarizes **individual pieces**
> (a weather blurb, a news summary, a misc summary, a todo list). The **final
> document is built by the orchestrator/renderer** — it stitches the already-
> summarized pieces together mechanically. There is **no LLM pass over the whole
> newspaper**. (The "final summarizer" delegate in §6 folds *chunks within one
> section*, not the whole paper.)

## 4. Project layout

```
/src
  DailySummary.Functions/          ← Azure Functions host (isolated worker, .NET 8)
    DailySummaryFunction.cs        ← TimerTrigger entry point
    Program.cs                     ← host builder + DI registration
    Dockerfile                     ← mcr.microsoft.com/azure-functions/dotnet-isolated base
    host.json, local.settings.json
    app.json                       ← user-facing config (location, sites, providers)
  DailySummary.Core/               ← orchestration + interfaces (no Azure dependency)
    Abstractions/                  ← ISummaryOrchestrator, ISummarizer, IPageFetcher,
                                     IWeatherProvider, INewsProvider, IMiscUpdatesProvider,
                                     ITodoSource, IDeliveryChannel, ISummaryRenderer
    Constants/                     ← Prompts.cs (all prompt text), SummarizationLimits.cs (chunk sizes)
    Models/                        ← AppConfig, DailySummary, NewsItem, TodoItem, WeatherReport, PageContent
    Summarization/                 ← SummarizeFunc.cs (delegate), TextChunker.cs (sliding-window split),
                                     ChunkedSummarizer.cs (map-reduce over delegates),
                                     SectionSummarizers.cs (named delegates per section)
    SummaryOrchestrator.cs
  DailySummary.Providers/          ← concrete implementations
    Weather/      OpenMeteoWeatherProvider.cs       (free, no API key)
    Fetching/     PlaywrightPageFetcher.cs          (headless Chromium, JS-rendered text)
    News/         WebNewsProvider.cs                (IPageFetcher → ISummarizer)
    Misc/         WebMiscUpdatesProvider.cs         (IPageFetcher → ISummarizer)
    Summarizers/  ClaudeSummarizer.cs, OllamaSummarizer.cs
    Todos/        SqlTodoSource.cs, GoogleCalendarTodoSource.cs
    Delivery/     MarkdownFileDelivery.cs, ConsoleDelivery.cs
    Rendering/    MarkdownSummaryRenderer.cs
/tests
  DailySummary.Tests/              ← xUnit; orchestrator test with fakes for every interface
docker-compose.yml                 ← Functions container (+ optional ollama sidecar)
README.md
```

## 5. Configuration (`app.json`)

```jsonc
{
  "schedule": "0 0 6 * * *",
  "weather":  { "enabled": true,  "latitude": 40.71, "longitude": -74.01, "units": "imperial" },
  "news":     { "enabled": true,  "summarizer": "claude",
                "sites": ["https://news.ycombinator.com", "https://www.reuters.com"] },
  "misc":     { "enabled": true,  "summarizer": "ollama",
                "sites": ["https://example.com/changelog"] },
  "todos":    { "enabled": true,  "source": "sql" },          // "sql" | "google"
  "summarizers": {
    "claude": { "model": "claude-haiku-4-5", "apiKeyEnv": "ANTHROPIC_API_KEY" },
    "ollama": { "endpoint": "http://ollama:11434", "model": "llama3.1" }
  },
  "delivery": { "channel": "markdown", "outputDir": "./out" }  // "markdown" | "console" | (later) "email"/"slack"
}
```

Bound to a strongly-typed `AppConfig` via `IOptions<AppConfig>`. Secrets (API
keys) come from **environment variables**, never committed in `app.json`.

## 6. Section internals (the parts that were open questions)

### Weather — Open-Meteo (free, no API key)

`OpenMeteoWeatherProvider` issues one GET to:

```
https://api.open-meteo.com/v1/forecast
  ?latitude={lat}&longitude={lon}
  &current=temperature_2m,weather_code,wind_speed_10m
  &daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,weather_code
  &timezone=auto&temperature_unit={fahrenheit|celsius}
```

- `lat`/`lon`/`units` come from `app.json` → `weather` block.
- The integer `weather_code` (WMO codes) is mapped to human text via a static
  lookup (`0 → "Clear sky"`, `61 → "Light rain"`, …) in a `WmoWeatherCodes` helper.
- Result projected into a `WeatherReport { Current, High, Low, PrecipChance, Condition }`.
- No key, no auth, no sidecar — the happy path stays credential-free.

### Page capture — Playwright (headless Chromium)

`PlaywrightPageFetcher : IPageFetcher` is the **only** way news/misc pages are read.

- On the first call per run it launches **one** Chromium instance
  (`Playwright.CreateAsync()` → `Chromium.LaunchAsync(headless: true)`); each site
  gets a fresh `IPage` (or browser context) so cookies/state don't leak between sites.
- Per site: `page.GotoAsync(url, WaitUntil = NetworkIdle)` with a timeout, then
  `page.InnerTextAsync("body")` to grab the fully rendered visible text.
- Returns `PageContent { Url, Title, Text, FetchedAt }`; the text is what the
  `ISummarizer` receives. Truncated to a max char budget before summarizing.
- The fetcher is `IAsyncDisposable` — the browser is disposed at the end of the run.
- Failures per site (timeout, nav error) are caught and logged; that site is
  skipped, the rest of the newspaper still renders.
- **Browser binary:** this environment ships Chromium at `/opt/pw-browsers`
  (`PLAYWRIGHT_BROWSERS_PATH` set), so no download is needed locally. The Docker
  image installs the browser + OS deps at build time (see Dockerization).

### Summarization — prompts + chunking (map-reduce)

No floating strings: every prompt and every magic number lives in
`DailySummary.Core/Constants/`.

**`SummarizationLimits.cs`**
```csharp
public const int MaxChunkChars = 3000;   // most we send to the LLM at once
public const int ChunkOverlapChars = 100; // chars each chunk shares with the previous
```

**`Prompts.cs`** (verbatim text, referenced by every summarizer — kept simple)
```csharp
public const string Weather =
    "You are preparing a daily weather brief. Here is the data. " +
    "Please brief for morning, afternoon, and evening.";

public const string News =
    "You are creating a summary for an end user on the daily headlines. " +
    "Please take the information and provide a summary.";

public const string Misc =
    "You are summarizing miscellaneous site updates for an end user. " +
    "Please provide a short summary of what changed.";

public const string Todos =
    "You are tidying a list of calendar items into a clean to-do list for the day.";

public const string ReduceSuffix =     // appended for the final big pass
    " The following are partial summaries; combine them into one final summary.";
```

**`TextChunker`** — splits a source string into windows of at most
`MaxChunkChars`, where each window after the first repeats the last
`ChunkOverlapChars` of the previous window (so context isn't lost at the seam).

**Delegate-based summarization.** The chunk loop does not hard-code "call the
LLM with a prompt". It takes a **delegate**, so each section passes its own
summarize function and we can add new ones (news, weather, websites, …) without
touching the loop:

```csharp
// the unit of summarization work — one passable function
public delegate Task<string> SummarizeFunc(string input, CancellationToken ct);
```

**`SectionSummarizers`** is a small factory that, given the configured
`ISummarizer`, exposes named delegates that each close over the right prompt:
`SummarizeWeather`, `SummarizeNews`, `SummarizeWebsites`/`SummarizeMisc`,
`SummarizeTodos`. Adding a future section = adding one more delegate here.

**`ChunkedSummarizer`** runs the map-reduce loop over **two delegates** — a
per-chunk one and an explicit **final summarizer** that runs once at the end:

```csharp
public async Task<string> SummarizeAsync(
    string source,
    SummarizeFunc chunkSummarizer,   // runs for each chunk  (e.g. SummarizeNews)
    SummarizeFunc finalSummarizer,   // runs ONCE at end to fold chunk summaries together
    CancellationToken ct)
{
    var chunks = TextChunker.Split(source);          // 3k windows, 100-char overlap
    if (chunks.Count == 1)
        return await chunkSummarizer(chunks[0], ct);  // nothing to fold

    var partials = new List<string>();
    foreach (var chunk in chunks)
        partials.Add(await chunkSummarizer(chunk, ct));     // map

    return await finalSummarizer(string.Join("\n\n", partials), ct);  // final summarizer
}
```

- The **final summarizer** is a real, separate delegate (not "whatever the last
  chunk produced") — typically `SectionSummarizers` builds it from the section
  prompt + `ReduceSuffix`, but any caller can pass a different fold function.
- Every section runs through `ChunkedSummarizer`, so the 3k-char limit and the
  final combined summary apply uniformly (weather, news, misc, todos), each just
  passing its own pair of delegates.

## 7. Build steps (when we implement)

1. **Solution scaffold** — `DailySummary.sln` with four projects + test project; .NET 8 isolated worker.
2. **Core abstractions & models** — interfaces + DTOs; `Constants/Prompts.cs` + `Constants/SummarizationLimits.cs`; `Summarization/` `SummarizeFunc` delegate + `TextChunker` + `ChunkedSummarizer` (map-reduce over delegates) + `SectionSummarizers` (named per-section delegates); `SummaryOrchestrator` (runs each enabled section, isolates failures, aggregates a `DailySummary`).
3. **Config** — `AppConfig` model, `app.json` binding + validation, sample config.
4. **Providers** — Open-Meteo weather; `PlaywrightPageFetcher` + Web news/misc providers (fetch→summarize); `ClaudeSummarizer` + `OllamaSummarizer` (factory keyed by config); `SqlTodoSource` + `GoogleCalendarTodoSource` stub; Markdown renderer + Markdown/console delivery.
5. **Function host** — `DailySummaryFunction` TimerTrigger; `Program.cs` DI wiring selecting implementations from `app.json`.
6. **Dockerization** — `Dockerfile` on azure-functions dotnet-isolated base **plus Playwright Chromium + OS deps** (`playwright install --with-deps chromium` during build); `docker-compose.yml` (function + optional `ollama` sidecar).
7. **Tests** — xUnit orchestrator test with in-memory fakes for every interface (wiring + failure isolation, no network); renderer/config-binding unit tests.
8. **README** — configure `app.json`, run locally (`func start` / `docker compose up`), deploy to Azure.

## 8. Verification

- **Unit/integration:** `dotnet test` — orchestrator produces a complete `DailySummary` from fakes; one failing section doesn't abort the run. `IPageFetcher` is faked here (no real browser in unit tests).
- **Local run (no cloud):** `delivery.channel = "console"`, weather enabled (Open-Meteo, no key), summarizer = `ollama` (or a `fake` summarizer for offline). Real Playwright fetch hits the configured sites. Manually trigger via the Functions admin endpoint (`POST http://localhost:7071/admin/functions/DailySummaryFunction`) and confirm a rendered newspaper in console / `./out/*.md`.
- **Docker:** `docker compose up`, same manual trigger, confirm output inside the container.
- **Claude path (optional):** set `ANTHROPIC_API_KEY`, switch news summarizer to `claude`, re-trigger, confirm a real summary.

## 9. Out of scope (future)

- Email / Slack delivery channels (interface ready, not implemented initially).
- Real Google Calendar OAuth flow (stub only initially).
- Azure deployment automation (IaC / Bicep) — README instructions only.
