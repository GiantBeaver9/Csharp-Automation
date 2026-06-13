# Csharp-Automation

A C# **Azure Functions** app (isolated worker, .NET 8) for personal automation on a
cron schedule. It ships with three composable building blocks — a **local LLM**
summarizer, **SMTP email**, and **keyless weather** — wired into two example
timer jobs:

| Function | Trigger | What it does |
| --- | --- | --- |
| `WeatherReportTimer` | Daily 06:30 | Fetches weather (Open-Meteo) and emails you a report |
| `WebpageSummaryTimer` | Daily 07:00 | Fetches configured pages, summarizes each with your local LLM, emails a digest |

Each job also has an HTTP twin so you can run it on demand without waiting for the clock:

| Endpoint | Runs |
| --- | --- |
| `GET/POST /api/run/weather` | the weather job |
| `GET/POST /api/run/summary` | the summary job |

## Architecture

```
src/AutomationFunctions/
├── Program.cs                 # DI registration + host
├── host.json                  # Functions host config
├── local.settings.json        # your secrets/config (gitignored; copy from .example)
├── Functions/                 # timer + HTTP triggers (thin; they compose services)
│   ├── WeatherReportFunction.cs
│   └── WebpageSummaryFunction.cs
├── Services/                  # the reusable automation logic
│   ├── WebPageFetcher.cs            # download + strip HTML to text
│   ├── OpenAiCompatibleLlmService.cs# local LLM via /chat/completions
│   ├── SmtpEmailService.cs          # send mail over SMTP
│   └── OpenMeteoWeatherService.cs   # current weather, no API key
└── Options/                   # strongly-typed config sections
```

**Adding a new job** = add a service (if needed) under `Services/`, register it in
`Program.cs`, and add a `[TimerTrigger]` function under `Functions/` that calls it.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local) (`func`)
- [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) — the
  local storage emulator that the timer runtime needs. Install with `npm i -g azurite`.
- A running **local LLM** with an OpenAI-compatible API (e.g.
  [Ollama](https://ollama.com): `ollama run llama3.1`, or LM Studio).

## Setup

1. **Create your settings file** (it's gitignored so secrets stay local):

   ```bash
   cd src/AutomationFunctions
   cp local.settings.json.example local.settings.json
   ```

2. **Fill in `local.settings.json`:**

   - **LLM** — point `Llm__BaseUrl` at your server (`http://localhost:11434/v1` for
     Ollama) and set `Llm__Model` to a model you've pulled.
   - **Email** — Gmail example below. Use a 16-character **App Password**
     ([create one here](https://myaccount.google.com/apppasswords); requires 2FA),
     *not* your normal password.

     | Provider | `Email__SmtpHost` | `Email__SmtpPort` |
     | --- | --- | --- |
     | Gmail | `smtp.gmail.com` | `587` |
     | Outlook/Office365* | `smtp.office365.com` | `587` |

     \*Microsoft has been disabling SMTP basic-auth on personal Outlook accounts; if it
     rejects you, Gmail is the reliable free option.
   - **Weather** — set `Weather__Latitude` / `Weather__Longitude` to your location.
   - **Summary** — set `Summary__Urls` to a comma-separated list of pages.

   > Config note: nested keys use the double-underscore convention (`Email__SmtpHost`),
   > which binds to `EmailOptions.SmtpHost`.

3. **Run it locally:**

   ```bash
   azurite &                 # start the storage emulator (separate terminal is fine)
   cd src/AutomationFunctions
   func start
   ```

4. **Test a job immediately** (instead of waiting for the timer):

   ```bash
   curl http://localhost:7071/api/run/weather
   curl http://localhost:7071/api/run/summary
   ```

   Locally the HTTP function key is not enforced, so these work as-is.

## Changing the schedules

Edit the `[TimerTrigger("...")]` NCRONTAB expressions. Format is
`{second} {minute} {hour} {day} {month} {day-of-week}`:

- `0 0 7 * * *` — every day at 07:00
- `0 0 * * * *` — top of every hour
- `0 */15 * * * *` — every 15 minutes

Schedules run in **UTC** by default. To use local time, set `WEBSITE_TIME_ZONE`
(e.g. `America/New_York`) in app settings (`TZ` on Linux).

## Deploying to Azure

```bash
# one-time: create a Function App (Linux, .NET 8 isolated) + storage in the portal or CLI
func azure functionapp publish <YourFunctionAppName>
```

Then add every key from `local.settings.json` (LLM, Email, Weather, Summary) to the
Function App's **Application settings**. Note: a cloud-hosted Function App cannot reach
a `localhost` LLM — for cloud runs, expose your model at a reachable URL (tunnel, VM,
or hosted endpoint) and update `Llm__BaseUrl`. For a purely local LLM, run the Functions
host on your own machine/VM instead.

## Upgrading to .NET 10

Change `<TargetFramework>net8.0</TargetFramework>` to `net10.0` in
`AutomationFunctions.csproj`, then `dotnet restore` (it will pull compatible package
versions). Everything else stays the same.
