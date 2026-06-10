# Honing Output Contract (ProjectBriefHoning)

Canonical output contract for project honing-pass drains, defined per
BD-Audit-2026-06-09 finding M3 (honing output was write-only and
schema-fractured: 1,876 enrichment rows in three different JSON shapes
with no registered extractor). All NEW honing prompts and drain outputs
MUST emit this shape. The legacy shapes remain readable (see "Legacy
shapes" below) but must not be produced going forward.

## Envelope

Every drain output file is an envelope:

```json
{
  "schemaVersion": "1.0",
  "kind": "project-brief-refresh",
  "generatedAtUtc": "2026-06-09T18:30:00Z",
  "items": [ { ...item... } ]
}
```

- `kind` MUST be `project-brief-refresh` for the `ab-projects` ingest
  kind (`BdQueueDrainIngest --kind ab-projects` validates this).
- `items` carries exactly one item per output file
  (`refresh-project-<MpiId>.json`).

## Item

```json
{
  "_providerName": "ProjectBriefHoning",
  "id": 12345,
  "projectName": "Example Secondary School Replacement",
  "honingPass": {
    "verdict": "PURSUE",
    "overallConfidence": 0.8,
    "description": "...",
    "schedule": "...",
    "status": "...",
    "korAngle": "...",
    "keyPeople": [ { "name": "...", "title": "...", "side": "Owner" } ],
    "actions":   [ { "type": "...", "recommendation": "...", "targetPerson": "...", "timingNotes": "..." } ],
    "signals":   [ { "type": "...", "subject": "...", "detail": "...", "occurredAt": "...", "sourceUrl": "..." } ],
    "risks":     [ { "type": "...", "description": "...", "mitigation": "..." } ]
  }
}
```

Rules:

1. **`_providerName` is REQUIRED at the item root** and MUST be the
   string `"ProjectBriefHoning"`. This is the authoritative routing
   field. The in-band `[providerName: X]` description marker is
   **legacy/deprecated** — the ingest still recognizes it as a fallback,
   but new outputs must carry the root field.
2. **Provider resolution is whitelist-gated and refuse-on-miss**
   (`tools\BdQueueDrainIngest\Program.cs`, `ResolveDrainProvider`):
   - A present-but-empty/non-string `_providerName` REJECTS the file.
   - A `_providerName` (or marker) not in the project whitelist
     (`ProjectBrief`, `ProjectBriefHoning`, `PrimeConsultantResearch`)
     REJECTS the file.
   - Multiple DISTINCT `[providerName: X]` markers in one payload
     REJECT (ambiguous).
   - Only when NO field and NO marker exist does the kind's first-pass
     default apply — so an unmarked honing output would be mis-filed as
     `ProjectBrief` and overwrite the first-pass brief (the C1
     failure). Always set `_providerName`.
3. **`honingPass.verdict` is REQUIRED** and MUST be one of:
   `PURSUE`, `PURSUE_URGENT`, `MONITOR`, `DEAD`, `DISCOVER`,
   `DUPLICATE`.
4. **Intel arrays live at the `honingPass` level**: `keyPeople`,
   `actions`, `signals`, `risks` (same per-item field shapes as the
   first-pass ProjectBrief contract). `overallConfidence` (0..1) at the
   `honingPass` level drives intel confidence (< 0.6 => Low, else
   Medium), matching first-pass conventions.

## Extraction

`ProjectBriefHoningExtractor`
(`Kor.Opportunities.Data\Intel\Extractors\ProjectBriefHoningExtractor.cs`)
is registered for `ProviderName = "ProjectBriefHoning"` and decomposes
the stored ResultJson into `opportunities.IntelProject*` rows with
`SourceProviderName = 'ProjectBriefHoning'` (so honing intel coexists
with — never overwrites — first-pass `ProjectBrief` intel). The honing
verdict is surfaced through `IntelProject.Status` when the payload has
no explicit `status`.

## Legacy shapes (still readable, do not produce)

All three pre-contract shapes found in the 1,876 existing
`ProjectBriefHoning` enrichment rows remain readable by
`ProjectBriefHoningExtractor`:

- **(a) first-pass-like** (1,095 rows): top-level `{ overallConfidence,
  description, keyPeople, actions, signals, risks, korAngle, schedule,
  status }` — no verdict at all.
- **(b) top-level verdict** (621 rows): `{ "verdict": ..., ... }` with
  intel arrays (if any) at the root.
- **(c) nested honingPass** (160 rows): `{ id, projectName,
  proponentName, province, city, estimatedCost, honingPass: { verdict,
  overallConfidence?, keyPeople?, actions?, signals?, risks?,
  korAngle?, ... } }`.

The extractor reads from `honingPass` when present, falling back to the
root for any field or array `honingPass` lacks, and tolerates missing
fields everywhere.
