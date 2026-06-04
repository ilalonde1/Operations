# Independent Agents

Standalone Sonnet prompts you can fire under **any** Claude account, in parallel with the main orchestration session. Each agent:

- Is self-contained (single paste-in file, knows what to read and write)
- Produces JSON to a known path in `KOR-Data-Honing/outputs/`
- Does NOT touch the database
- Is resumable (skips already-written batch files)
- Includes an autonomous-operation block (no per-step confirmation)

The main Claude account ingests the JSON when it's back online.

## Available agents

| Agent | Prompt | Inputs | Outputs | Runs in parallel with |
|---|---|---|---|---|
| A — Polish gathered evidence | `AgentA-PolishEvidence.md` | `gathered-evidence-<date>/evidence/evidence-*.json` (Step 2 output) | `outputs/polished-batch-NN.json` + summary | Anything after Step 2 finishes |
| B — Architect-Pipelines deep research | `AgentB-ArchitectDeepResearch.md` | `discovered-websites.csv` (Step 1 output) | `outputs/architects-deep-batch-NN.json` + summary | Step 2, Agent A, anything |

## How to fire

In the second Claude account's terminal:

```powershell
cd "C:\VIsual Studio Projects\KOR-Data-Honing"
claude --model sonnet --dangerously-skip-permissions
```

Then paste the full contents of the chosen agent prompt. Don't add anything before or after — the prompts are self-contained.

## Ingestion (back on main account)

When you return to the main session:

- **Agent A outputs** → run `C:\Users\ilalonde\AppData\Local\Temp\ingest_bare_orgs.ps1` (or its updated equivalent) pointing at the concatenated batch files; then `BdCanonicalDedup` post-audit.
- **Agent B outputs** → I generate an `import-fixed.sql` from the batches (mirrors `KOR-Architect-Pipelines/import-fixed.sql` shape), you run in SSMS, then `BdCanonicalDedup` post-audit.

## Why this works

- Sonnet's expensive web-search work was already done by Step 1 (URL discovery) and Step 2 (evidence gathering). The agents just polish the raw text into structured payloads — much cheaper.
- Agent B is the exception: it does original deep research on architects specifically, but bounded to 226 verified architects with confirmed URLs. The URL discovery is already done so the agent skips Tier 1.
- Both agents are stateless across batches: a crash mid-run loses only the in-flight batch, not the whole run.
