# Csharp-Automation

A C# **Azure Functions** app (isolated worker, .NET 8) for personal automation on a
cron schedule. One timer runs the whole pipeline:

> **get weather → aviation weather (METAR/TAF) → summarize the websites you list →
> scan your inbox (optional) → email yourself one digest.**

| Function | Trigger | What it does |
| --- | --- | --- |
| `DailyDigestTimer` | Daily 07:00 | Builds the digest (weather + aviation + website summaries + inbox summary) and emails it to you |

It also has an HTTP twin so you can run it on demand without waiting for the clock:

| Endpoint | Runs |
| --- | --- |
| `GET/POST /api/run/digest` | the full digest pipeline |

Under the hood it's composable building blocks — **keyless weather**, **aviation
weather** (FAA/NOAA METAR/TAF by ICAO code), a **local LLM** summarizer, an **IMAP inbox
scanner**, and **SMTP email** — assembled by `DigestService`.
The digest *builds content*; *delivery* is a separate step (email today), so swapping in
an app/push channel later is just a new sender.

## Architecture

```
AutomationFunctions/
├── Program.cs                 # DI registration + host
├── Constants.cs              # default values, grouped by area (Llm, Email, Weather, ...)
├── host.json                  # Functions host config
├── local.settings.json        # your secrets/config (gitignored; copy from .example)
├── Functions/                 # thin triggers
│   └── DailyDigestFunction.cs       # timer + HTTP; builds digest, then sends it
├── Services/                  # the reusable automation logic
│   ├── DigestService.cs             # orchestrates the pipeline into one report
│   ├── WebPageFetcher.cs            # download + strip HTML to text
│   ├── OpenAiCompatibleLlmService.cs# local LLM via /chat/completions
│   ├── ImapMailScanner.cs           # read recent mail over IMAP (MailKit)
│   ├── SmtpEmailService.cs          # send mail over SMTP (MailKit)
│   ├── OpenMeteoWeatherService.cs   # current weather, no API key
│   └── AviationWeatherService.cs    # METAR/TAF by ICAO code, no API key
└── Options/                   # strongly-typed config sections
```

**Adding a step** = add a service under `Services/`, register it in `Program.cs`, and
call it from `DigestService.BuildAsync`. **Adding a separate job on its own schedule** =
add another `[TimerTrigger]` function under `Functions/`.

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
   cd AutomationFunctions
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
   - **Aviation (optional)** — set `Aviation__Airports` to comma-separated ICAO codes
     (e.g. `KJFK,KBOS`); empty skips the section. `Aviation__IncludeTaf` controls whether
     the forecast is fetched too. Best set via User Secrets (below) so your airports stay
     out of source.
   - **Summary** — set `Summary__Urls` to a comma-separated list of pages.
   - **Inbox scan (optional)** — set `MailScan__Enabled` to `true` and fill in the
     `MailScan__Accounts__0__*` (Gmail) and `__1__*` (Outlook) blocks. Use IMAP host
     `imap.gmail.com` / `outlook.office365.com`, port `993`, and an **App Password**.
     Gmail also needs IMAP enabled (Settings → Forwarding and POP/IMAP). Leave
     `MailScan__Enabled=false` to skip the inbox section entirely.

     \*Heads up: Microsoft is phasing out basic-auth IMAP on personal Outlook accounts in
     favor of OAuth2 — if Outlook rejects the app password, Gmail is the reliable option,
     and OAuth2 support can be added later.

   > Config note: nested keys use the double-underscore convention (`Email__SmtpHost`),
   > which binds to `EmailOptions.SmtpHost`. List items are indexed
   > (`MailScan__Accounts__0__ImapHost`).

### Where configuration lives

Azure Functions doesn't use `appsettings.json`. Config comes from three layers, each
overriding the one before:

| Layer | Scope | Use for |
| --- | --- | --- |
| `Constants.cs` (in code) | fallback | non-secret defaults like ports, timeouts, units, prompts — grouped by area |
| `local.settings.json` → env vars | local dev | everything; the `.example` lists every key |
| **User Secrets** (`secrets.json`) | local dev | secrets, kept out of the project folder entirely |
| **Application settings** | Azure | everything, including secrets, in the cloud |

The `Options` classes seed their properties from `Constants.cs`, so every non-secret
default lives in one place (e.g. `Constants.Email.SmtpPort`) and config still overrides it.

**Keep secrets out of files with User Secrets.** Instead of putting your Gmail/IMAP
passwords in `local.settings.json`, store them in the per-user secret store
(`~/.microsoft/usersecrets/...`, never in the repo). Use `:` as the separator here:

```bash
cd AutomationFunctions
dotnet user-secrets set "Email:Password" "your-app-password"
dotnet user-secrets set "MailScan:Accounts:0:Password" "your-app-password"
dotnet user-secrets set "Aviation:Airports" "KJFK,KBOS"   # your airports, ICAO codes
```

`Program.cs` loads these automatically in local dev and ignores them in Azure (where you
use Application settings instead).

3. **Run it locally:**

   ```bash
   azurite &                 # start the storage emulator (separate terminal is fine)
   cd AutomationFunctions
   func start
   ```

4. **Test a job immediately** (instead of waiting for the timer):

   ```bash
   curl http://localhost:7071/api/run/digest
   ```

   This runs the full pipeline immediately and returns the rendered digest (and emails
   it). Locally the HTTP function key is not enforced, so it works as-is.

## Email formatting & highlights

The digest is HTML, so it uses **bold, colors, highlights, and bullet lists** (inline CSS
only — that's all email clients reliably render). Highlighting is rule-based and centralized:

- **Temperatures** at/above the hot threshold render red, at/below the cold threshold blue
  (`Constants.Weather.HotF/ColdF/HotC/ColdC`).
- **Raw METAR/TAF** tokens are color-coded: freezing precip / icing and snow **blue**,
  thunderstorms / hail **red**, wind gusts **orange** (rules in
  `WeatherHighlighter` in `Services/HtmlFormat.cs`).
- **Flight categories** are colored VFR/MVFR/IFR/LIFR.

Tweak the palette in one place (`Constants.Colors`), thresholds in `Constants.Weather`, and
add new token rules in `WeatherHighlighter.ClassifyToken`. The `HtmlFormat` helper
(`Bold`, `Color`, `ColorBold`, `Highlight`, `Bullets`) is reusable for any new section.

## Changing the schedule

The schedule is the `DigestSchedule` app setting (the timer binds to it via
`%DigestSchedule%`), so you change *when* it runs without touching code. It's an
NCRONTAB expression — `{second} {minute} {hour} {day} {month} {day-of-week}`:

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

Then add every key from `local.settings.json` (LLM, Email, Weather, Aviation, Summary,
MailScan) to the Function App's **Application settings**. Note: a cloud-hosted Function App cannot reach
a `localhost` LLM — for cloud runs, expose your model at a reachable URL (tunnel, VM,
or hosted endpoint) and update `Llm__BaseUrl`. For a purely local LLM, run the Functions
host on your own machine/VM instead.

## Upgrading to .NET 10

Change `<TargetFramework>net8.0</TargetFramework>` to `net10.0` in
`AutomationFunctions.csproj`, then `dotnet restore` (it will pull compatible package
versions). Everything else stays the same.
