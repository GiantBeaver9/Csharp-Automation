# Design note: Durable Functions refactor (sketch)

Status: **proposal / not yet implemented.** This captures how the timer-based
orchestrator would become a Durable Functions orchestration, so a long digest
(many sections, podcast transcription — 30–60 min) is resilient instead of one
fragile function. Implement as a **separate PR that keeps the timer version
intact** for comparison.

## Why

The current run is a single timer function. If it runs 30–60 minutes, a host
recycle, deploy, or scale-in kills it mid-run and the whole digest is lost.
Durable Functions splits the work into an **orchestrator** (coordination) plus
short **activities** (the actual work). The orchestration survives restarts by
replaying from history, retries failed activities, and is not bound by a single
function's timeout.

## The three function roles

| Role | Trigger | Job | Maps from |
|------|---------|-----|-----------|
| **Starter** | `[TimerTrigger]` / `[HttpTrigger]` | kick off an orchestration, return immediately | `MorningDigestFunction` / `EveningDigestFunction` |
| **Orchestrator** | `[OrchestrationTrigger]` | coordinate — call activities, fan out / fan in. **Deterministic, no I/O** | `SummaryOrchestrator.RunAsync` |
| **Activity** | `[ActivityTrigger]` | do the real work (fetch, LLM call, write) | the gatherers / summarizer / delivery |

**Everything in `DailySummary.Core` and `DailySummary.Providers` survives
unchanged** — the `ISectionGatherer`s, `ISummarizer`s, `SummarizerRegistry`,
`SectionGathererRegistry`, `ChunkedSummarizer`, and `SummaryRenderer`. Activities
are thin wrappers that call them. Only the *host wiring* (Functions project)
changes.

## Skeleton

```csharp
// 1) STARTER — the timer just schedules the orchestration and returns
[Function("MorningStarter")]
public async Task Run(
    [TimerTrigger("%MORNING_SCHEDULE%")] TimerInfo timer,
    [DurableClient] DurableTaskClient client)
{
    await client.ScheduleNewOrchestrationInstanceAsync(nameof(DigestOrchestrator), "morning");
}

// 2) ORCHESTRATOR — deterministic coordination only (no HttpClient, no DateTime.Now)
[Function(nameof(DigestOrchestrator))]
public static async Task DigestOrchestrator([OrchestrationTrigger] TaskOrchestrationContext ctx)
{
    var digestName = ctx.GetInput<string>()!;
    var sections   = await ctx.CallActivityAsync<SectionConfig[]>(nameof(LoadSectionsActivity), digestName);

    // FAN-OUT: gather every section in parallel (I/O-bound, safe to parallelize)
    var gatherTasks = sections.Select(s => ctx.CallActivityAsync<RawPiece[]>(nameof(GatherSectionActivity), s));
    var pieces      = (await Task.WhenAll(gatherTasks)).SelectMany(p => p).ToArray();

    // SINGLE LLM LANE: summarize one piece at a time (sequential await → never overwhelms the local model)
    var summarized = new List<SummarizedPiece>();
    foreach (var piece in pieces)
        summarized.Add(await ctx.CallActivityAsync<SummarizedPiece>(nameof(SummarizePieceActivity), piece));

    // fold + render are pure string work → fine inside the orchestrator
    var doc = FoldAndRender(digestName, summarized, ctx.CurrentUtcDateTime);

    // DELIVER (I/O) → activity
    await ctx.CallActivityAsync(nameof(DeliverActivity), new DeliverInput(digestName, doc));
}

// 3) ACTIVITIES — real work; inject the existing services via DI
[Function(nameof(GatherSectionActivity))]
public async Task<RawPiece[]> Gather([ActivityTrigger] SectionConfig cfg)
    => (await _registry.Resolve(cfg.Type).GatherAsync(cfg, CancellationToken.None)).ToArray();

[Function(nameof(SummarizePieceActivity))]
public async Task<SummarizedPiece> Summarize([ActivityTrigger] RawPiece piece)
{
    // resolve summarizer by NAME here (delegates can't cross the activity boundary), then ChunkedSummarizer
    ...
}
```

The current concurrency model survives, expressed differently: **fan-out gather**
= `Task.WhenAll` (parallel); **single LLM lane** = a sequential `foreach` of
`await` (throttle to one at a time). Batches of N are a `Task.WhenAll` over a
window if you later want bounded-parallel summaries.

## The three rules that make it more than a method-swap

1. **Orchestrators must be deterministic.** They are **replayed** repeatedly as
   activities complete, so inside the orchestrator: no `HttpClient`, no
   `DateTime.Now` (use `ctx.CurrentUtcDateTime`), no `Guid.NewGuid()`, no
   `SemaphoreSlim`/threads. All impure work moves into activities — which our
   gatherers already encapsulate, so they slot in cleanly.
2. **Activity inputs/outputs must be JSON-serializable.** `SectionConfig`,
   `RawPiece`, etc. (records) are fine. The `SummarizeFunc` **delegate cannot
   cross an activity boundary**, so `SummarizePieceActivity` resolves the
   summarizer by name from `SummarizerRegistry` internally instead of receiving a
   delegate. The delegate stays an in-activity detail.
3. **Concurrency control changes shape.** The bounded `Channel` single-consumer
   lane becomes "await summaries sequentially" (or in batches). Same effect
   (protect the local model), different mechanism. Gather still fans out.

## What you add / change

- **Package:** `Microsoft.Azure.Functions.Worker.Extensions.DurableTask`.
- **Storage:** Durable uses **Azure Storage as its state/history backend** — the
  same reason the timer already needed Azurite locally. No new infra.
- **New host wiring:** a `Starters` file (thin timer/HTTP triggers), a
  `DigestOrchestrator`, and an `Activities` file wrapping the existing services.
  Suggested layout: keep the timer app as-is and add these alongside, or a new
  `DailySummary.Functions.Durable` project so both run for comparison.
- **Local time / filename:** the `{date}-{digest}.md` naming lives in
  `MarkdownFileDelivery` (an activity), which may use `DateTime.Now` — activities
  are not replayed, so local time is fine there. The document *title* built in the
  orchestrator must use `ctx.CurrentUtcDateTime` (or be passed in from the starter).

## The honest trade-off

What gets **more** complex: the elegant streaming `Channel` pipeline (summarize as
each fetch lands) becomes stage-gated fan-out/fan-in (all gathers, then all
summaries), so you lose the fetch/LLM overlap. For a once-daily batch, durability
is worth that. The `Channel` version stays in the repo as the "simple/streaming"
variant.

## Incremental migration plan

1. Add the DurableTask package; new `Starters` (timer → `ScheduleNewOrchestrationInstanceAsync`).
2. `DigestOrchestrator` + `LoadSectionsActivity` (reads the digest's sections from `AppConfig`).
3. `GatherSectionActivity` wrapping `SectionGathererRegistry` + the gatherers (unchanged).
4. `SummarizePieceActivity` wrapping `SummarizerRegistry` + `ChunkedSummarizer` (resolve by name).
5. Fold + `SummaryRenderer` in the orchestrator; `DeliverActivity` wrapping the delivery channels.
6. Add activity **retry policies** (`TaskOptions` with `RetryPolicy`) — free resilience per section.
7. Keep the timer/`Channel` version; gate which host runs via config or separate project.

## Résumé talking points this exercises

Durable orchestration, fan-out/fan-in, deterministic replay, activity retry
policies, external state/history in Azure Storage, and the trade-off between a
streaming in-process pipeline and a durable staged one.
