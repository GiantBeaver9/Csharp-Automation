# Daily Summary Orchestrator — Design Spec

> "Morning newspaper" delivered automatically every day.

## 1. Purpose

A C# application that, once a day, assembles a personalized **daily summary**
("morning newspaper") and delivers it. It runs as a **timer-triggered Azure
Function packaged in a Docker container** — chosen over a plain Docker worker
because the Azure Functions model is the more transferable, production-shaped
experience for a real workplace, while still running locally for development.

The summary is built from a **config-driven list of sections** (`app.json`
`sections[]`) — sections are data, not code. Each section declares a `type`, and
the orchestrator dispatches by that type to a gatherer. Ship-day types:

1. **Weather** — Open-Meteo for the day (location in the section's `settings`).
2. **Web** — fetch configured sites (Playwright/Firefox) and have an LLM summarize
   them. Used for news, misc updates, changelogs — any number of `web` sections.
3. **Sql / GoogleCalendar** — pull today's events/tasks into a to-do list. Calendar
   (secret iCal URL, no OAuth) supports **today** and **week** (per-day) views.
4. **Question** — answer free-form questions ("search for X", "what's the forecast
   in Y?") by web-searching, fetching the top results, and having the LLM answer
   from them. Questions are a JSON array in the section's `settings`.
5. **Email** — digest a Gmail inbox (IMAP + app password, no OAuth) into key items
   and action items.
6. **RSS** — digest RSS/Atom feeds (structured XML, no browser) into per-feed summaries.

New section *instances* are pure JSON; new *types* (e.g. **Email**, RSS) are one
small class each. Everything external (LLM, page fetch, gather source, delivery)
sits behind an interface selected via config, so the whole app runs locally with
**zero cloud credentials**.

## 2. Key decisions

| Concern   | Decision | Rationale |
|-----------|----------|-----------|
| Hosting   | Timer-triggered (CRON) **Azure Function**, containerized via Docker | Production-shaped, transferable workplace experience; runs locally too. *(confirmed)* |
| Sections  | **Config-driven** `sections[]`; `SectionType` enum → `ISectionGatherer` registry | Add instances by JSON, add types by one class — no orchestrator changes |
| Concurrency | **Fan-out fetch → single LLM lane** via bounded `Channel`; two-stage (per-source + section fold) | Hide fetch latency; never overwhelm a local model; flat memory at scale |
| Page fetch | **Playwright Firefox, headed** (under Xvfb in Docker) | Evades bot detection far better than headless Chromium |
| LLM       | Pluggable `ISummarizer` — **Claude** + **Ollama**, chosen per section in `app.json` | "LLM summarize" for news, "local llm" for misc; both behind one seam |
| Delivery  | Pluggable `IDeliveryChannel` — **Markdown / console / email** ship; Slack later | Markdown/console testable with zero accounts; email = the rendered triple |
| Timeouts  | **Per-item `timeoutSeconds`**, skip-on-fail | Long batch run; a few dead items never abort or stall it |
| Weather   | **Open-Meteo** (free, no API key) | Happy path needs no secrets |

> Defaults (Claude vs Ollama, markdown delivery) are config — changing one is a
> JSON edit, not a code change.

## 3. Architecture

```
Azure Functions Host (Docker)
  └─ DailySummaryFunction        ← [TimerTrigger(schedule from app.json)]  (06:00 daily)
        └─ ISummaryOrchestrator.RunAsync()
              foreach section in app.json `sections[]`  (data, not code):
                 SectionGathererRegistry[section.Type]  → ISectionGatherer
              │
              │  GatherSummarizePipeline (bounded Channel):
              │   ┌ producers (concurrency.fetch) ┐        ┌ consumer(s) (concurrency.summarize) ┐
              │   │ WeatherGatherer  → RawPiece    │        │  ChunkedSummarizer per piece         │
        RawPiece │ WebGatherer(Firefox) → RawPiece │──Ch──▶ │  → partial summaries by section      │
              │   │ SqlGatherer      → RawPiece    │        │  → section FOLD (Final delegate)     │
              │   └ EmailGatherer(future)→RawPiece ┘        └──────────────────────────────────────┘
              │
              └─ SummaryRenderer (FormatSection delegate) → RenderedSummary triple
                    └─ IDeliveryChannel  (markdown/console | email | …)
```

Web gatherers do **not** fetch HTML with `HttpClient` — they use `IPageFetcher`
(Playwright / **Firefox, headed**) so JS-rendered pages are captured as real text.

Core principle: the **orchestrator depends only on interfaces and iterates over
config**. It has no per-section code — `SectionType` selects an `ISectionGatherer`
from a registry. Every section is independently toggleable (remove it from JSON)
and **failure-isolated** (per-item timeout → "section unavailable" note, run continues).

### Orchestration flow (`SummaryOrchestrator.RunAsync`)

Driven by `app.json` `sections[]`; phases overlap via a bounded channel (§6).

1. **Gather (concurrent producers)** — for each configured section, the registry
   resolves an `ISectionGatherer` by `SectionType`; gatherers run in parallel
   (bounded by `concurrency.fetch`) and write `RawPiece`s into the channel **as
   they finish**. A section with many sources (e.g. 500 URLs) emits one `RawPiece`
   per source.
2. **Summarize (serial consumer)** — one LLM lane (`concurrency.summarize`, default
   1 to protect local models) drains the channel as pieces arrive, summarizing each
   via `ChunkedSummarizer`. After the channel drains, each section's per-source
   summaries are **folded** (the section's `Final` delegate) into one section entry.
3. **Render & deliver** — `SummaryRenderer` assembles the folded sections (in
   `order`) into the document; `IDeliveryChannel` returns it (console) or writes
   the Markdown file (`delivery.outputDir`) or emails the triple.

Per-item failures/timeouts degrade to a "this call was unable to complete" note
rather than aborting the run.

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
                                     ISectionGatherer, IDeliveryChannel, ISummaryRenderer
    Constants/                     ← Prompts.cs (default prompt per type), SummarizationLimits.cs
    Models/                        ← AppConfig, SectionConfig, SectionType (enum),
                                     DailySummary, SummarySection, RenderedSummary,
                                     RawPiece, GatherResult, TodoItem, WeatherReport, PageContent
    Pipeline/                      ← GatherSummarizePipeline.cs (bounded Channel producer→consumer),
                                     SectionGathererRegistry.cs (SectionType → ISectionGatherer)
    Summarization/                 ← SummarizeFunc.cs (delegate), TextChunker.cs (sliding-window split),
                                     ChunkedSummarizer.cs (map-reduce over delegates),
                                     SectionSummarizers.cs (delegate per section, from prompt+ISummarizer)
    Rendering/                     ← FormatSection.cs (delegate), SectionFormats.cs (Markdown/Html),
                                     SummaryRenderer.cs (structured model → formatted string)
    SummaryOrchestrator.cs
  DailySummary.Providers/          ← concrete implementations
    Gatherers/    WeatherGatherer.cs (Open-Meteo), WebGatherer.cs (IPageFetcher),
                  RssGatherer.cs (HttpClient + SyndicationFeed, no browser),
                  SqlGatherer.cs, GoogleCalendarGatherer.cs (secret iCal URL + Ical.Net),
                  QuestionGatherer.cs, EmailGatherer.cs (Gmail IMAP via MailKit)
                  — each declares its SectionType + binds its own `settings` JSON
    Fetching/     PlaywrightPageFetcher.cs          (Firefox, headed, JS-rendered text)
    Search/       IWebSearch.cs, DuckDuckGoSearch.cs (keyless via fetcher), (future) BraveSearch.cs
    Summarizers/  ClaudeSummarizer.cs, OllamaSummarizer.cs, PassthroughSummarizer.cs ("none")
    Delivery/     MarkdownFileDelivery.cs, ConsoleDelivery.cs, EmailDelivery.cs
/tests
  DailySummary.Tests/              ← xUnit; orchestrator test with fakes for every interface
docker-compose.yml                 ← Functions container (+ optional ollama sidecar)
README.md
```

## 5. Configuration (`app.json`)

```jsonc
{
  "schedule": "0 0 6 * * *",
  "concurrency": { "fetch": 7, "summarize": 1 },   // fetch lanes; LLM lanes (keep 1 for local models)
  "channelCapacity": 16,                            // bounded backpressure (~ fetch count)
  "browser": { "engine": "firefox", "headed": true }, // Firefox headed evades bot detection better than headless
  "summarizers": {
    // a section's `"summarizer"` names one of these, or the built-in "none" (verbatim passthrough)
    "claude": { "model": "claude-haiku-4-5", "apiKeyEnv": "ANTHROPIC_API_KEY" },
    "ollama": { "endpoint": "http://ollama:11434", "model": "llama3.1" }
  },
  "delivery": {
    "channel": "markdown",            // "markdown" | "console" | "email"
    "outputDir": "./out",             // markdown channel: writes {outputDir}/{yyyy-MM-dd}.md
    "email": {                        // used only when channel == "email"
      "to": "adamnash19@gmail.com", "from": "brief@example.com",
      "smtpHost": "smtp.example.com", "smtpPort": 587,
      "passwordEnv": "SMTP_PASSWORD"  // secret via env var, never in app.json
    }
  },

  // Sections are data, not code. Add/remove/reorder by editing this array.
  // `type` selects the gatherer (SectionType enum → ISectionGatherer registry).
  // `settings` is a type-specific blob parsed by that gatherer ("parse by json specs").
  // `prompt` is optional — omitted = default from Constants/Prompts.cs for that type.
  "sections": [
    { "type": "weather", "heading": "Weather", "order": 0, "summarizer": "ollama",
      "timeoutSeconds": 30,
      "settings": { "latitude": 40.71, "longitude": -74.01, "units": "imperial" } },

    { "type": "web", "heading": "Headlines", "order": 1, "summarizer": "claude",
      "timeoutSeconds": 60,
      "settings": { "urls": ["https://news.ycombinator.com", "https://www.reuters.com"] } },

    { "type": "web", "heading": "Site Updates", "order": 2, "summarizer": "ollama",
      "timeoutSeconds": 60,
      "settings": { "urls": ["https://example.com/changelog"] } },

    { "type": "sql", "heading": "To-Dos", "order": 3, "summarizer": "ollama",
      "timeoutSeconds": 30,
      "settings": { "connectionStringEnv": "TODO_DB", "query": "SELECT title, due FROM tasks WHERE due::date = CURRENT_DATE" } },

    { "type": "question", "heading": "Q&A", "order": 4, "summarizer": "claude",
      "timeoutSeconds": 90,
      "settings": { "engine": "duckduckgo", "resultsPerQuestion": 3,
                    "questions": [ "What's the forecast for Tokyo this weekend?",
                                   "search for recent .NET 9 news" ] } },

    { "type": "email", "heading": "Inbox", "order": 5, "summarizer": "claude",
      "timeoutSeconds": 90,
      "settings": { "imapHost": "imap.gmail.com", "imapPort": 993, "username": "you@gmail.com",
                    "passwordEnv": "GMAIL_APP_PASSWORD", "mailbox": "INBOX",
                    "unreadOnly": true, "since": "today", "maxMessages": 50 } },

    { "type": "rss", "heading": "Feeds", "order": 6, "summarizer": "ollama",
      "timeoutSeconds": 45,
      "settings": { "feeds": ["https://hnrss.org/frontpage", "https://www.theverge.com/rss/index.xml"],
                    "maxItemsPerFeed": 10, "since": "1d", "fetchFullArticle": false } },

    { "type": "googleCalendar", "heading": "Calendar", "order": 7, "summarizer": "none",
      "timeoutSeconds": 30,
      "settings": { "icalUrlEnv": "GCAL_ICAL_URL", "views": ["today", "week"],
                    "weekDays": 7, "timezone": "America/New_York" } }
  ]
}
```

Bound to a strongly-typed `AppConfig` via `IOptions<AppConfig>`. Secrets (API
keys) come from **environment variables**, never committed in `app.json`.

## 6. Section internals (the parts that were open questions)

### Weather — Open-Meteo (free, no API key)

`WeatherGatherer` (type `weather`) issues one GET to:

```
https://api.open-meteo.com/v1/forecast
  ?latitude={lat}&longitude={lon}
  &current=temperature_2m,weather_code,wind_speed_10m
  &daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,weather_code
  &timezone=auto&temperature_unit={fahrenheit|celsius}
```

- `lat`/`lon`/`units` come from the `weather` section's `settings` blob.
- The integer `weather_code` (WMO codes) is mapped to human text via a static
  lookup (`0 → "Clear sky"`, `61 → "Light rain"`, …) in a `WmoWeatherCodes` helper.
- Result projected into a `WeatherReport { Current, High, Low, PrecipChance, Condition }`,
  then flattened into the `RawPiece` text the summarizer receives.
- No key, no auth, no sidecar — the happy path stays credential-free.

### Section model & extensibility — config-driven, enum-dispatched

Sections are **data, not code**. Each `sections[]` entry binds to a `SectionConfig`:

```csharp
public enum SectionType { Weather, Web, Rss, Sql, GoogleCalendar, Question, Email /* add a case per new type */ }

public record SectionConfig(
    SectionType Type, string Heading, int Order, string Summarizer,
    int TimeoutSeconds, string? Prompt, JsonElement Settings);   // Settings = type-specific blob
```

Every source type implements one interface:

```csharp
public interface ISectionGatherer
{
    SectionType Type { get; }                                    // self-declares its enum
    Task<IReadOnlyList<RawPiece>> GatherAsync(SectionConfig cfg, CancellationToken ct);
}
// RawPiece { int SectionOrder; string Heading; string? SubHeading; string? Instruction; string Text; }
//   - one per source (URL, row, email, search result…)
//   - SubHeading  groups pieces within a section (e.g. one per question) → folded separately
//   - Instruction injects per-piece prompt context (e.g. the question being answered); usually null
```

`GatherAsync` deserializes its own slice: `cfg.Settings.Deserialize<WebSettings>()` etc.
("parse by json specs"). DI registers all gatherers; `SectionGathererRegistry` is a
`Dictionary<SectionType, ISectionGatherer>` built from them — **dispatch is a registry
lookup keyed by the enum, not a hand-edited switch**.

- **Add an instance** of an existing type (another site list, another SQL query, a
  third weather location) → **pure JSON**, zero code.
- **Add a new type** (Email, RSS, Reddit) → implement one `ISectionGatherer`, add the
  enum case, register it once in `Program.cs`. That single new class is the *only*
  place that type's code exists — minimal surface, minimal liability.
- **Prompt**: `cfg.Prompt ?? Prompts.Default(cfg.Type)` — code carries no floating
  strings; JSON may override per section.

### Page capture — Playwright (Firefox, headed)

`PlaywrightPageFetcher : IPageFetcher` is the **only** way web pages are read, used
by `WebGatherer`. **Firefox headed**, not Chromium headless — it evades bot
detection markedly better.

- Launches **one** Firefox per run (`Firefox.LaunchAsync(new() { Headless = false })`);
  each fetch gets a fresh `IBrowserContext` (cheap isolation, no cookie/state bleed)
  — this is how `concurrency.fetch` parallel "browsers" run without N processes.
- Per page: `page.GotoAsync(url, WaitUntil = NetworkIdle)` under the section's
  `timeoutSeconds`, then `page.InnerTextAsync("body")` for rendered visible text.
- The fetcher is `IAsyncDisposable`; the browser is disposed at end of run.
- **Headed needs a display.** Locally it opens real windows. In Docker it runs under
  **Xvfb** (virtual framebuffer); the image installs Firefox + OS deps via
  `playwright install --with-deps firefox` and `xvfb` (see Dockerization).

### Question / Search sections — ask, search the web, answer

A `question` section answers free-form questions ("What's the forecast for Tokyo
this weekend?", "search for the .NET 9 release date"). Questions live in a JSON
array; each one is searched, the top results are fetched, and the LLM answers
**from those sources**.

```jsonc
{ "type": "question", "heading": "Q&A", "order": 5, "summarizer": "claude",
  "timeoutSeconds": 90,
  "settings": {
    "engine": "duckduckgo",        // keyless default (scraped via the Firefox fetcher)
    "resultsPerQuestion": 3,       // top-N result pages fed to the LLM per question
    "questions": [
      "What's the weather forecast for Tokyo this weekend?",
      "search for recent news on the .NET 9 release",
      "When is the next SpaceX launch?"
    ]
  } }
```

End-to-end flow (per question):

**Gather** (`QuestionGatherer`, runs as a concurrent producer):
1. **Search for X** → `IWebSearch.SearchAsync(question, resultsPerQuestion)` returns
   the **top result links**. Default `DuckDuckGoSearch` scrapes the keyless HTML
   endpoint **through the same `IPageFetcher`** — no API key. (A keyed `BraveSearch`
   can drop in later behind the same interface, `apiKeyEnv` in settings.)
2. **Iterate the links** → fetch each result page via `IPageFetcher` → text.
3. Emit one `RawPiece` **per result page**, tagged `SubHeading = question` and
   `Instruction = question`.

**Summarize** (pipeline consumer) — a **two-level fold**:
4. **Per page (chunks):** each result `RawPiece` goes through `ChunkedSummarizer`
   → split into 3k chunks → summarize each → fold into that page's summary.
   "Summarize each one by chunks."
5. **Per question (list → answer):** all of a question's page summaries are gathered
   (grouped by `SubHeading`) and sent to the LLM in **one final call** — the section's
   `Final` delegate, carrying `Prompts.Question` + the question — producing the answer.
   "Send the entire list of summaries to the LLM."

**Dynamic prompt.** This is the one section whose prompt isn't static: the
summarize delegate uses `Prompts.Question` ("Answer the question using only the
provided sources; if they don't answer it, say so.") **plus the piece's
`Instruction`** (the question text). Pieces with a non-null `Instruction` get the
question injected; everything else uses the section's static prompt.

**Rendering.** Each question's answer renders as a `### {question}` sub-heading +
its answer, in order — so one `question` section with 5 questions → 5 Q&A entries
under the one section heading.

### RSS / Atom feeds — structured XML, no browser

An `rss` section digests feeds. Unlike `web`, feeds are **structured XML**, so this
uses a plain `HttpClient` + `System.ServiceModel.Syndication.SyndicationFeed` (parses
RSS 2.0 **and** Atom) — **no Playwright/Firefox** (cheaper, faster, no bot-detection
concerns). It's the one web source that skips the browser.

```jsonc
{ "type": "rss", "heading": "Feeds", "order": 6, "summarizer": "ollama",
  "timeoutSeconds": 45,
  "settings": {
    "feeds": ["https://hnrss.org/frontpage", "https://www.theverge.com/rss/index.xml"],
    "maxItemsPerFeed": 10,
    "since": "1d",
    "fetchFullArticle": false   // true → pull each item's link via IPageFetcher (Firefox) for full text
  } }
```

Flow in `RssGatherer` (concurrent producer):
1. Per feed URL: `HttpClient` GET → `SyndicationFeed.Load(reader)`.
2. Keep items newer than `since`, capped at `maxItemsPerFeed`.
3. Item text = `Title` + `Summary`/`Content`; if `fetchFullArticle`, replace the body
   with `IPageFetcher(item.Link)` (the only time RSS touches the browser).
4. Emit one `RawPiece` per item, `SubHeading = feed title` (so each feed folds to its
   own digest), `Instruction = null`.

Fold: per feed (by `SubHeading`) → one digest per feed; renders `### {feed title}` +
digest. A dead feed degrades to "section unavailable" and the others proceed.

### Google Calendar — secret iCal URL (no OAuth), today + week views

Google Calendar exposes a **"Secret address in iCal format"** — a private `.ics`
URL fetchable **without OAuth** (the calendar parallel to Gmail's app password).
`GoogleCalendarGatherer` GETs it with `HttpClient` and parses with **Ical.Net**
(which expands recurring events). The secret URL lives in an env var.

```jsonc
{ "type": "googleCalendar", "heading": "Calendar", "order": 7, "summarizer": "none",
  "timeoutSeconds": 30,
  "settings": {
    "icalUrlEnv": "GCAL_ICAL_URL",     // the secret .ics address, via env var
    "views": ["today", "week"],        // either or both
    "weekDays": 7,
    "timezone": "America/New_York"
  } }
```

Two views, each a sub-group rendered under its own `###` header:
- **today** → events starting today, chronological. `SubHeading = "To do today"`.
- **week** → events in the next `weekDays`, **grouped by day**; each day is a
  `SubHeading` like `"Mon, Jun 30"` and renders as a `### Mon, Jun 30` header with
  that day's events listed in chronological order.

`GoogleCalendarGatherer` emits one `RawPiece` per group (today, then each upcoming
day) with events **already sorted**; the per-`SubHeading` fold keeps them ordered, so
the section reads "To do today" then each day in sequence.

**Why `summarizer: "none"`.** Calendar data is factual — exact times must not drift.
`"none"` is a **built-in passthrough summarizer** (no entry needed in `summarizers`)
that emits the gathered text verbatim: no LLM call, no risk of altered times. Point
it at a real summarizer if you'd rather have it prose-ified.

> Scope: this covers calendar **events**. Google **Tasks** (the separate to-do
> product) needs the Tasks API/OAuth — out of scope; use a `sql` section for tasks
> meanwhile.

### Email (Gmail) — IMAP + app password, no OAuth

An `email` section digests a mailbox. **Gmail over IMAP** is the first backend:
with 2-Step Verification on and a generated **App Password**, IMAP authenticates
with plain username + password — **no interactive OAuth** (the reason Outlook/Graph
are deferred). `EmailGatherer` uses `MailKit`.

```jsonc
{ "type": "email", "heading": "Inbox", "order": 5, "summarizer": "claude",
  "timeoutSeconds": 90,
  "settings": {
    "imapHost": "imap.gmail.com", "imapPort": 993,
    "username": "you@gmail.com", "passwordEnv": "GMAIL_APP_PASSWORD",  // app password via env var
    "mailbox": "INBOX", "unreadOnly": true, "since": "today", "maxMessages": 50
  } }
```

Flow in `EmailGatherer` (concurrent producer like any other):
1. `MailKit.ImapClient` connects TLS to `imapHost:imapPort`, authenticates with
   `username` + the app password from `passwordEnv`.
2. Search the `mailbox`: `since` (e.g. midnight today) and/or `unreadOnly`, capped at
   `maxMessages`.
3. Per message: pull `From`, `Subject`, and the text body (prefer `text/plain`; else
   convert the `text/html` part to text). Emit **one `RawPiece` per email**
   (`Heading = "Inbox"`, `SubHeading = null`).
4. Section fold: all emails → **one inbox digest** via `Prompts.Email` ("Summarize the
   inbox into key items and any action items.").

Setup note: Gmail needs **2-Step Verification enabled** and an **App Password**
generated; store it in `GMAIL_APP_PASSWORD`, never in `app.json`. (Outlook/Graph and
full OAuth2 XOAUTH2 stay out of scope — same `ISectionGatherer`, added later.)

### Concurrency pipeline — bounded Channel, fan-out fetch → single LLM lane

`GatherSummarizePipeline` is a producer→consumer over `System.Threading.Channels`:

```csharp
var channel = Channel.CreateBounded<RawPiece>(cfg.ChannelCapacity);   // backpressure
// PRODUCERS — concurrency.fetch lanes; write RawPieces as they finish, fail-soft
await Parallel.ForEachAsync(allGathers, new(){MaxDegreeOfParallelism = cfg.Fetch}, async (g, ct) => {
    try   { foreach (var p in await WithTimeout(g, ct)) await channel.Writer.WriteAsync(p, ct); }
    catch { await channel.Writer.WriteAsync(Unavailable(g), ct); }     // "could not complete" marker
});
channel.Writer.Complete();
// CONSUMER(S) — concurrency.summarize lanes (default 1); drain as items arrive
await foreach (var piece in channel.Reader.ReadAllAsync(ct)) {
    var fn = summarizers.For(piece);   // section prompt (+ piece.Instruction if present)
    partials[(piece.SectionOrder, piece.SubHeading)].Add(
        await chunked.SummarizeAsync(piece.Text, fn.Chunk, fn.Final, ct));
}
```

- **Overlap:** the LLM starts on the first arrival while later fetches are still
  running — fetch latency hides behind LLM work.
- **Two-stage summarization for scale:** per-source summaries accumulate per
  `(SectionOrder, SubHeading)`; after the drain, each group is **folded** via its
  `Final` delegate into one entry. `SubHeading == null` → one group per section
  (the common case); a `question` section → one group per question. So "Headlines"
  with 500 URLs → 500 page summaries → one folded section; a `question` section with
  5 questions → 5 folded Q&A entries. The newspaper never balloons to 500 blurbs.
- **Dynamic prompt:** `summarizers.For(piece)` builds the delegate from the section
  prompt and, when `piece.Instruction` is set, injects it (the question being answered).
- **500-page behavior:** memory stays **flat** (bounded channel blocks producers when
  the LLM lane lags); 500 URLs queue through `fetch` lanes (not 500 browsers); the
  single LLM lane is the throughput ceiling (fine for a 6am batch; raise
  `concurrency.summarize` for cloud). `summarize > 1` = that many consumer loops on
  the same reader — channels hand each piece to exactly one reader.
- **Ordering:** pieces finish out of order but carry `SectionOrder`; render reads
  sections in `order`, so layout is deterministic.

### Per-item timeouts — configurable, skip-on-fail

No short global deadline (the run isn't interactive). Each gather and each LLM call
runs under a linked `CancellationTokenSource` seeded from the section's
`timeoutSeconds`. On timeout or null response, that item is dropped and the renderer
inserts *"this call was unable to complete"* — the rest of the run proceeds.

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

### Rendering — structured model + format delegate (Markdown / HTML)

The orchestrator builds a **structured, format-agnostic** document; formatting is
a **delegate** so the same document renders as Markdown headers or HTML tags
depending on the delivery channel.

**Model** (`Models/`)
```csharp
public record SummarySection(string Heading, string Body);   // Body = markdown text from the LLM
public record DailySummary(string Title, IReadOnlyList<SummarySection> Sections);
public record RenderedSummary(string Subject, string Markdown, string Html); // the "triple"
```

Fixed title + section headings (Constants, not floating strings):
`Daily Brief — {ddd, MMM d, yyyy}` · `Weather` · `Headlines` · `Site Updates` · `To-Dos`.
Disabled/failed sections are simply omitted (or rendered as "_section unavailable_").

**Format delegate** (`Rendering/`)
```csharp
public delegate string FormatSection(string heading, string body);

static class SectionFormats
{
    public static string Markdown(string h, string b) => $"## {h}\n\n{b}\n";
    public static string Html(string h, string b)     => $"<h2>{h}</h2>\n{Markdig.Markdown.ToHtml(b)}";
}
```

**`SummaryRenderer`** walks `DailySummary.Sections`, applies the chosen
`FormatSection` delegate, and prepends the title (`# …` for Markdown, `<h1>…` for
HTML). It exposes both representations and packages them as the triple:

```csharp
public RenderedSummary Render(DailySummary s) => new(
    Subject:  s.Title,                                  // email subject / file title
    Markdown: RenderWith(s, SectionFormats.Markdown),   // file + console
    Html:     RenderWith(s, SectionFormats.Html));      // email body
```

Because section bodies are markdown, the **HTML path runs each body through
Markdig** so weather's morning/afternoon/evening bullets and news lists become
real `<ul>`/`<p>` — no manual tag juggling.

### Delivery — each channel pulls what it needs from the triple

`IDeliveryChannel.DeliverAsync(RenderedSummary doc, ...)`:

| Channel | Pulls from triple | Behavior |
|---------|-------------------|----------|
| `markdown` | `doc.Markdown` | writes `{outputDir}/{yyyy-MM-dd}.md` |
| `console`  | `doc.Markdown` | prints to stdout / function logs |
| `email`    | **all three** — `doc.Subject`, `doc.Html`, `doc.Markdown` | sends multipart email: HTML body + plaintext (Markdown) fallback |

So "if email is the channel, we pull the HTML tags" = email uses `doc.Html` for
the body and keeps `doc.Markdown` as the text/plain alternative; the subject is
`doc.Title`. Markdown/console channels just ignore the `Html` field. Adding a new
channel never changes the renderer — it only chooses which fields of the triple
to use.

## 7. Build steps (when we implement)

1. **Solution scaffold** — `DailySummary.sln` (Functions + Core + Providers + test); .NET 8 isolated worker.
2. **Core abstractions & models** — `ISectionGatherer`, `ISummarizer`, `IPageFetcher`, `IDeliveryChannel`, `ISummaryRenderer`; `SectionType` enum, `SectionConfig`, `RawPiece`, `DailySummary`/`SummarySection`/`RenderedSummary`; `Constants/Prompts.cs` (default per type) + `SummarizationLimits.cs`; `Summarization/` (`SummarizeFunc` + `TextChunker` + `ChunkedSummarizer` + `SectionSummarizers`).
3. **Config** — `AppConfig` (`sections[]`, `concurrency`, `channelCapacity`, `browser`, `delivery`), `app.json` binding + validation; per-type `Settings` bound via `JsonElement`.
4. **Pipeline + registry** — `SectionGathererRegistry` (enum→gatherer); `GatherSummarizePipeline` (bounded Channel: concurrent producers → serial consumer + section fold); `SummaryOrchestrator` iterating `sections[]`.
5. **Gatherers + adapters** — `WeatherGatherer` (Open-Meteo); `WebGatherer` over `PlaywrightPageFetcher` (**Firefox, headed**); `RssGatherer` (HttpClient + `SyndicationFeed`, no browser); `SqlGatherer`; `GoogleCalendarGatherer` (secret iCal URL + Ical.Net, today/week views); `QuestionGatherer` over `IWebSearch`/`DuckDuckGoSearch` (keyless) + fetcher, with dynamic `Instruction` prompt; `EmailGatherer` (Gmail IMAP via MailKit, app password); `ClaudeSummarizer` + `OllamaSummarizer` + `PassthroughSummarizer` ("none"); `SummaryRenderer` (Markdown + HTML triple); markdown/console/email delivery.
6. **Function host** — `DailySummaryFunction` TimerTrigger; `Program.cs` DI registering every `ISectionGatherer` (→ registry) + selecting summarizer/delivery from `app.json`.
7. **Dockerization** — `Dockerfile` on azure-functions dotnet-isolated base **plus Firefox + Xvfb + OS deps** (`playwright install --with-deps firefox`, `xvfb`); run the host under `xvfb-run`; `docker-compose.yml` (function + optional `ollama` sidecar).
8. **Tests** — xUnit: pipeline test (concurrent gather + serial summarize + section fold + ordering) with a fake gatherer/summarizer; failure/timeout → "unavailable"; renderer Markdown/HTML/triple asserts; config + `Settings` binding.
9. **README** — `app.json` sections model, run locally (`func start` / `docker compose up`), deploy to Azure.

## 8. Verification

- **Unit/integration:** `dotnet test` — `GatherSummarizePipeline` with a fake gatherer (emits N pieces, some that throw/time out) + fake summarizer: assert concurrent gather, serial summarize (consumer never overlaps), section fold, deterministic order, and that failures/timeouts render "unavailable". Renderer test asserts Markdown (`##`) + HTML (`<h2>`) of the same `DailySummary` and the email triple (subject/html/text). Config test binds a `sections[]` `app.json` incl. per-type `Settings`.
- **Local run (no cloud):** `delivery.channel = "console"`, a `weather` section (Open-Meteo, no key) + a `web` section, summarizer = `ollama` (or a `fake` for offline). Real Firefox (headed) fetch hits the sites. Trigger via the admin endpoint (`POST http://localhost:7071/admin/functions/DailySummaryFunction`) and confirm a rendered newspaper in console / `./out/*.md`.
- **Docker:** `docker compose up` (Firefox runs under Xvfb), same trigger, confirm output inside the container.
- **Scale check:** a `web` section with a large `urls` list — confirm flat memory (bounded channel), serial LLM lane, and one folded section in the output.
- **Question section:** a `question` section with 2–3 questions — confirm each is searched (DuckDuckGo, keyless), top results fetched, and rendered as `### {question}` + an answer; a question with no usable results renders "couldn't answer from sources" rather than failing the run.
- **Email section:** with `GMAIL_APP_PASSWORD` set, an `email` section connects to Gmail IMAP, pulls today's/unread messages (capped), and renders one inbox digest; a bad/missing password degrades to "section unavailable", not a crash.
- **RSS section:** an `rss` section with 2 feeds — confirm XML parsing (no browser launched for it), per-feed `### {feed title}` digests, `since`/`maxItemsPerFeed` honored, and a dead feed degrades without sinking the others.
- **Google Calendar section:** with `GCAL_ICAL_URL` set, a `googleCalendar` section with `views: ["today","week"]` and `summarizer: "none"` — confirm today's events list, the next 7 days grouped under `### {date}` headers in chronological order, recurring events expanded, and verbatim times (no LLM drift).
- **Claude path (optional):** set `ANTHROPIC_API_KEY`, switch a section's summarizer to `claude`, re-trigger, confirm a real summary.

## 9. Out of scope (future)

- New section *types* beyond Weather/Web/Rss/Sql/GoogleCalendar/Question/Email — e.g. Reddit, podcasts. The model supports them; each is one `ISectionGatherer` + enum case + registration when wanted.
- **Outlook / Microsoft Graph** email and Gmail **OAuth2 (XOAUTH2)** — same `EmailGatherer` interface; Gmail IMAP + app password ships first to avoid the OAuth flow.
- **Google Calendar API (OAuth2)** and **Google Tasks** — same `GoogleCalendarGatherer` interface; secret iCal URL (events only, no OAuth) ships first.
- Keyed search backends for `question` sections (Brave/Google CSE) behind `IWebSearch`; DuckDuckGo (keyless) ships first.
- Slack / Teams / Discord delivery channels (interface ready; email + markdown + console ship initially).
- Real Google Calendar OAuth flow (stub only initially).
- Azure deployment automation (IaC / Bicep) — README instructions only.
