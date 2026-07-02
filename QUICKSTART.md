# DailySummary — Quickstart

A config-driven "morning newspaper" orchestrator. Timer-triggered Azure Functions
(Dockerized) assemble daily digests from pluggable sections (weather, web, RSS,
calendar, email, questions, podcasts, and MCP prompt sections). Full design in
[`SPEC.md`](./SPEC.md).

> The single solution is `DailySummary.sln` (projects under `src/` and `tests/`).
> Regenerate it any time with `scripts/regen-sln.sh` (wraps `dotnet sln`).

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

Trigger a digest immediately via the **manual run endpoint** (an HTTP trigger — no
timer-binding quirks, just open it):

```
http://localhost:7071/api/run/morning
http://localhost:7071/api/run/evening
```

(GET works in a browser; it runs synchronously and returns when done — a big digest
takes a while, so watch the `func` console for progress and the output folder for the file.)

The `markdown` channel writes `./out/<date>.md`; `console` prints to logs.

## Playwright browser (for `web` / `question` sections)

Those sections render pages with **headed Firefox**, which needs the Playwright
browser binary installed once. Two ways:

**Automatic (no PowerShell needed).** The app installs the matching Firefox on first
use if it's missing. The very first run downloads ~80 MB *during* the run, so a `web`
section may time out that once — just run the digest a **second time** and it's cached.

**Manual (front-loads the download).** Requires **PowerShell 7** (`pwsh`, not Windows
PowerShell 5 — install with `winget install Microsoft.PowerShell`, then a new terminal):

```powershell
# use THIS project's script so the browser revision matches the app's Playwright version
pwsh src\DailySummary.Providers\bin\Debug\net8.0\playwright.ps1 install firefox
```

Verify it landed:

```powershell
dir "$env:LOCALAPPDATA\ms-playwright\firefox-*\firefox\firefox.exe"
```

> Common failure: `Executable doesn't exist at …\firefox-####\…` means the browser for
> the app's Playwright version isn't installed (or a *different* version's script
> installed a different revision). Always install via the script under
> `DailySummary.Providers\bin\...`. In Docker the browser is baked into the image, so
> none of this applies there.

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
