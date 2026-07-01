# Csharp-Automation — DailySummary

A C# **Azure Functions** app (isolated worker, .NET 8, Dockerized) that assembles a
daily "morning newspaper" and delivers it. Timer-triggered digests are built from a
**config-driven list of section types** — everything external sits behind an interface
selected in `app.json`, so it runs locally with zero cloud credentials and each source
upgrades to a real service independently.

- **Full design:** [`SPEC.md`](./SPEC.md)
- **Build & run:** [`QUICKSTART.md`](./QUICKSTART.md)

## What it does

Each **digest** is one scheduled run (its own cron + delivery + sections). Ship config
has a **6am newspaper** (Markdown) and a **10pm dev recap** (console/email). Sections are
data, not code:

| Type | Source |
| --- | --- |
| `weather` | Open-Meteo (free, no key) |
| `web` | fetched pages via Playwright (Firefox, headed) → LLM summary |
| `rss` | RSS/Atom feeds (structured XML, no browser); **Reddit** is just RSS URLs |
| `podcast` | download audio → local Whisper transcription → summary *(stub)* |
| `sql` | a to-do query *(stub)* |
| `googleCalendar` | secret iCal URL (no OAuth), today + week views |
| `question` | web-search → fetch top results → LLM answers from them |
| `email` | Gmail IMAP + app password (no OAuth) → inbox digest |
| `prompt` | hand an instruction to a tool/MCP-enabled LLM (`enumerate` mode lists items, then summarizes each one-by-one) |

Summarization is a bounded-channel pipeline: concurrent gather → a single LLM lane
(protects local models) → per-section fold → a structured document rendered to a
Markdown/HTML/subject triple, delivered by the digest's channel.

## Layout

```
src/DailySummary.Core        interfaces, models, pipeline, summarization, rendering (no external deps)
src/DailySummary.Providers   gatherers, summarizers, Playwright fetcher, search, delivery
src/DailySummary.Functions   Functions host (Morning/Evening timer triggers), app.json, Dockerfile
tests/DailySummary.Tests     xUnit: chunker, renderer, pipeline (with fakes)
```

The single solution is **`DailySummary.sln`**. Regenerate it any time with
`scripts/regen-sln.sh` (wraps `dotnet sln`).

## Build & test

```bash
dotnet build DailySummary.sln
dotnet test  DailySummary.sln
```

See [`QUICKSTART.md`](./QUICKSTART.md) for running locally (`func start`), Docker
(`docker compose up`), configuration, and secrets.

## Status

Core, the pipeline, and most gatherers/summarizers/delivery are implemented.
`sql` + `podcast` gatherers and the Whisper transcriber are honest stubs (they return an
"unavailable" note or throw a clear message) — see `SPEC.md §6`.
