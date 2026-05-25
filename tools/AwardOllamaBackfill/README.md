# Award Ollama Backfill

Drains the `OpportunityAward.AgentEnrichedAtUtc IS NULL` queue using local Ollama inference. No Anthropic API cost.

## Pre-flight

1. Install Ollama on Windows: `winget install Ollama.Ollama` or download it from ollama.com.
2. Pull a model: `ollama pull qwen2.5:14b` recommended for quality/speed balance. For faster smoke testing first try `ollama pull qwen2.5:7b`.
3. Verify: `curl http://localhost:11434/api/tags` returns JSON listing the pulled model.
4. Ollama runs as a background service after install; the default endpoint is `http://localhost:11434`.

With an Nvidia GPU, expect roughly 5-10 seconds per row. CPU-only is more like 30-60 seconds per row. For the 48K-row backlog, that means a few hours on GPU or a couple days on CPU.

## Usage

```powershell
cd tools/AwardOllamaBackfill
dotnet run -- --batch 10 --max 100 --model qwen2.5:14b
```

## First Smoke Test

```powershell
dotnet run -- --batch 5 --max 10 --model qwen2.5:7b
```

## CLI Flags

`--batch N` - Batch size per fetch. Default `10`.

`--max N` - Max rows this run. Default `0` means unlimited.

`--model NAME` - Ollama model. Default `qwen2.5:14b`.

`--ollama URL` - Ollama endpoint. Default `http://localhost:11434`.

`--sleep MS` - Sleep milliseconds between rows. Default `0`.

## Environment

`KOR_OPPORTUNITIES_OPPORTUNITIESDB` must be set to the same KorOpportunitiesDb connection string the Worker uses. `appsettings.json` can also provide `OpportunitiesDb` for local testing, but the environment variable wins.

## Resume-Safe

The tool uses the same `ListPendingAgentEnrichmentAsync` filter as the Worker job, so killing and restarting picks up where it left off. Rows that hit `MaxAttempts=3` across Anthropic and Ollama combined are skipped.
