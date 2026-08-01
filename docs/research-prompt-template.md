# KOR Research Session Canonical PROMPT Template (R87)

> Every interactive Sonnet research session that writes JSON for
> BdResearchImport ingestion MUST emit canonical-schema records and
> self-validate before declaring done. This file is the contract.

## 1. Canonical record schema

Every record (single object or one element of an array file) MUST
have these top-level fields:

| Field | Required | Type | Notes |
|---|---|---|---|
| `_providerName` | yes | string | One of: FirmNarrative, ArchitectPipelineResearch, CompetitorProfile, PublicSectorResearch, ... (match the C# extractor's ProviderName) |
| `displayName` | yes | string | The org's brand name. NOT `orgDisplayName`, NOT `organizationName`, NOT `firmName`. Exact field name `displayName`. |
| `kind` | yes | string enum | One of: Architect, Developer, GC, Designer, Modular, Competitor, Buyer, Government, Subcontractor, Vendor, KorClient, Unknown |
| `_generatedAt` | optional | string | ISO date |
| `_confidence` | optional | enum | high \| medium \| low |
| `decisionMakers` | optional | array | Each item: `{name, title, email, phone, linkedinUrl, notes}` |
| `signals` | optional | array | Each item: `{signalType, subject, detail, occurredAtApprox, sourceUrl}` |
| `actions` | optional | array | Each item: `{actionType, recommendation, targetPersonName, timingNotes}` |
| `works` | optional | array | Each item: `{projectName, role, yearApprox, estimatedValueCad, estimatedValueText, notes}` |
| `risks` | optional | array | Each item: `{riskType, description, mitigationNotes}` |
| `narratives` | optional | array | Each item: `{narrativeType Current\|History\|Action\|Summary, paragraphText}` |

Files containing project rows for MajorProjectsInventory (NOT
canonical-org enrichments) are out of scope for `--ingest-canonical`
and must be ingested via a different tool path.

## 2. Self-validation workflow

After writing each output file (or batch of files), the Sonnet session
MUST run:

```powershell
cd "C:\VIsual Studio Projects\Operations"
dotnet run --project tools/BdResearchImport --configuration Release -- `
    --dry-run --strict --ingest-canonical "<absolute-output-folder>"
```

Exit code 0 = clean; the session may declare the file done.

Exit code 3 = strict-mode violations; the session MUST:

1. Read the `STRICT-ERROR` lines from the command output
2. Fix the offending fields in the JSON file
3. Re-run the validation command
4. Repeat until exit 0

Common violations + fixes:

- "missing 'kind' field" - add the canonical kind enum value
- "PROMPT used legacy alias 'orgDisplayName'" - rename field to `displayName`
- "kind value 'BuyerOrg' not in enum" - rewrite to one of the allowed enum values
- "missing _providerName" - add the matching extractor name as `_providerName`

## 3. Why this matters

Today (2026-06-05) interactive Sonnet research sessions drifted on
field names. Edmonton owners used `orgDisplayName`, Calgary owners
used `organizationName`, Edmonton pipeline used `sourceKey` + `province`,
Calgary pipeline did not. The drift required custom PowerShell
ingestion. R86 made the importer tolerant to legacy aliases (with a
warning); R87 makes the warnings hard errors in strict mode so future
PROMPT files lock the schema at write time.

The automated nightly BdResearchExecutor never had this problem
because Anthropic tool-use enforces the JSON schema at the API
level. The interactive Sonnet path now matches that guarantee via
this validate-then-ship contract.

## 4. Authoring a new PROMPT.md

Future PROMPT.md files SHOULD include:

- A "## Output schema" section that references this file:
  > "Outputs MUST conform to the canonical schema documented in
  > `docs/research-prompt-template.md`. The session validates itself
  > by running `--dry-run --strict --ingest-canonical` after writing."

- The exact validation command in the autonomous-operation block.

- The list of `_providerName` values the session will emit (so the
  importer can route correctly).
