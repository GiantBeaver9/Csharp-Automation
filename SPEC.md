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
              ├─ IWeatherProvider        → weather section
              ├─ INewsProvider           → fetch news sites ─┐
              ├─ IMiscUpdatesProvider    → fetch misc sites ─┤→ ISummarizer (Claude | Ollama)
              ├─ ITodoSource (SQL|GCal)  → to-do list        ┘
              └─ ISummaryRenderer        → builds the "newspaper" document
                    └─ IDeliveryChannel  (Markdown/console | Email | Slack)
```

Core principle: the **orchestrator depends only on interfaces**; concrete
implementations are registered in DI and selected by `app.json`. Each section is
independently toggleable and **failure-isolated** — a dead news site must not sink
the weather section.

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
    Abstractions/                  ← ISummaryOrchestrator, ISummarizer, IWeatherProvider,
                                     INewsProvider, IMiscUpdatesProvider, ITodoSource,
                                     IDeliveryChannel, ISummaryRenderer
    Models/                        ← AppConfig, DailySummary, NewsItem, TodoItem, WeatherReport
    SummaryOrchestrator.cs
  DailySummary.Providers/          ← concrete implementations
    Weather/      OpenMeteoWeatherProvider.cs       (free, no API key)
    News/         HttpNewsProvider.cs               (fetch + text extract)
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

## 6. Build steps (when we implement)

1. **Solution scaffold** — `DailySummary.sln` with four projects + test project; .NET 8 isolated worker.
2. **Core abstractions & models** — interfaces + DTOs; `SummaryOrchestrator` (runs each enabled section, isolates failures, aggregates a `DailySummary`).
3. **Config** — `AppConfig` model, `app.json` binding + validation, sample config.
4. **Providers** — Open-Meteo weather; HTTP news/misc fetch→summarize; `ClaudeSummarizer` + `OllamaSummarizer` (factory keyed by config); `SqlTodoSource` + `GoogleCalendarTodoSource` stub; Markdown renderer + Markdown/console delivery.
5. **Function host** — `DailySummaryFunction` TimerTrigger; `Program.cs` DI wiring selecting implementations from `app.json`.
6. **Dockerization** — `Dockerfile` on azure-functions dotnet-isolated base; `docker-compose.yml` (function + optional `ollama` sidecar).
7. **Tests** — xUnit orchestrator test with in-memory fakes for every interface (wiring + failure isolation, no network); renderer/config-binding unit tests.
8. **README** — configure `app.json`, run locally (`func start` / `docker compose up`), deploy to Azure.

## 7. Verification

- **Unit/integration:** `dotnet test` — orchestrator produces a complete `DailySummary` from fakes; one failing section doesn't abort the run.
- **Local run (no cloud):** `delivery.channel = "console"`, weather enabled (Open-Meteo, no key), summarizer = `ollama` (or a `fake` summarizer for offline). Manually trigger via the Functions admin endpoint (`POST http://localhost:7071/admin/functions/DailySummaryFunction`) and confirm a rendered newspaper in console / `./out/*.md`.
- **Docker:** `docker compose up`, same manual trigger, confirm output inside the container.
- **Claude path (optional):** set `ANTHROPIC_API_KEY`, switch news summarizer to `claude`, re-trigger, confirm a real summary.

## 8. Out of scope (future)

- Email / Slack delivery channels (interface ready, not implemented initially).
- Real Google Calendar OAuth flow (stub only initially).
- Azure deployment automation (IaC / Bicep) — README instructions only.
