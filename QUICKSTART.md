# DailySummary — Quickstart

A config-driven "morning newspaper" orchestrator. Timer-triggered Azure Functions
(Dockerized) assemble daily digests from pluggable sections (weather, web, RSS,
calendar, email, questions, podcasts, and MCP prompt sections). Full design in
[`SPEC.md`](./SPEC.md).

> This is the redo (`DailySummary.sln`, under `src/`). The older
> `AutomationFunctions` project is still present and can be removed once this is green.

## Layout

```
src/DailySummary.Core        interfaces, models, pipeline, summarization, rendering (no external deps)
src/DailySummary.Providers   gatherers, summarizers, fetcher, search, delivery
src/DailySummary.Functions   Azure Functions host (Morning/Evening timer triggers), app.json, Dockerfile
tests/DailySummary.Tests     xUnit: chunker, renderer, pipeline (with fakes)
```

## Configure

Everything lives in `src/DailySummary.Functions/app.json`: shared infra
(`concurrency`, `browser`, `summarizers`) plus a `digests[]` array. Each digest has
its own `schedule`, `delivery`, and `sections[]`. A section's `type` selects a
gatherer; `settings` is a type-specific blob. Secrets come from **env vars**
(see `local.settings.json.example`) — never commit them.

## Build & test

```bash
dotnet build DailySummary.sln
dotnet test  DailySummary.sln
```

## Run locally

```bash
cd src/DailySummary.Functions
cp local.settings.json.example local.settings.json   # then fill in any secrets
func start                                            # requires Azure Functions Core Tools
```

Trigger a digest immediately via the admin endpoint:

```bash
curl -X POST http://localhost:7071/admin/functions/MorningDigestFunction
curl -X POST http://localhost:7071/admin/functions/EveningDigestFunction
```

The `markdown` channel writes `./out/<date>.md`; `console` prints to logs.

## Run in Docker

```bash
docker compose up --build
docker compose exec ollama ollama pull llama3.1   # first time, for the local LLM
```

## Implementation status

Fully implemented: Core (pipeline, summarization, rendering, orchestrator),
summarizers (Ollama, Claude, passthrough), gatherers (Weather, RSS, Prompt,
Web, Question, Email, GoogleCalendar), delivery (console, markdown, email).

Stubbed (return an "unavailable" note or throw a clear message; see `SPEC.md §6`):
`SqlGatherer`, `PodcastGatherer`, `WhisperTranscriber`, and the DuckDuckGo result
parsing / Playwright browser install in Docker. These are marked with TODOs.

> Built without a local .NET SDK, so not yet compile-verified — run `dotnet build`
> and address any provider-SDK signature nits (Playwright / MailKit / Ical.Net).
