# Project notes for Claude

## Local LLM: LM Studio (default)

The user runs **LM Studio** as their local LLM — an **OpenAI-compatible** server at
`http://localhost:1234` (`/v1/chat/completions`), **not** Ollama.

- Default summarizer for this project is **`openai`** (`OpenAiCompatibleSummarizer`),
  configured in `app.json` → `summarizers.openai`.
- Do **not** default sample config or examples to `ollama` — LM Studio does not speak
  Ollama's `/api/chat`.
- The `ollama` and `claude` summarizers still exist as options; leave them, but `openai`
  is the default choice.

## Solution

- Single solution: `DailySummary.sln` (projects under `src/` and `tests/`).
- Regenerate with `scripts/regen-sln.sh`.

## Running

- Isolated-worker Azure Functions: launch via the Functions host (`func start` in
  `src/DailySummary.Functions`), **not** `dotnet run` on the bare exe (that gives the
  gRPC `http://:` error).
