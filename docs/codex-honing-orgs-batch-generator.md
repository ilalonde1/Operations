# Codex: Add `-Kind honing-orgs` to Generate-Batch.ps1

## Goal

Add `honing-orgs` as a new `-Kind` to `\\KOR-APP01\QueueDrain\Generate-Batch.ps1`.
When invoked, it generates a JSON batch file for the `honing-orgs` drain at
`\\KOR-APP01\QueueDrain\honing-orgs\inputs\batch-NNN.json`.

Each batch row must include the full relationship-graph context the honing-orgs
PROMPT.md requires — not just the org name.

## Read these files first

Before writing any code, read:
1. `\\KOR-APP01\QueueDrain\honing-orgs\PROMPT.md` — exact batch format, all required fields
2. `Kor.Opportunities.Data/Schema/64_IntelEntities.sql` — IntelSignal + IntelWork schema
3. `Kor.Opportunities.Data/Schema/38_MajorProjectsInventory.sql` — MPI column names

## Pattern to follow

Extend the existing `param` block, `drainFolder` switch, query block, and row
construction block in `Generate-Batch.ps1`. Mirror the `honing-people` case
(recently added): main query returns the org + its FirmNarrative JSON, then
per-org sub-queries (or FOR JSON PATH subselects) pull the relationship context.

## Required batch row format

```json
{
  "id": 12345,
  "displayName": "Henriquez Partners Architects",
  "kind": "Architect",
  "firstPassNarrative": { ...parsed FirmNarrative JSON from CanonicalOrgEnrichment.ResultJson... },
  "linkedActiveProjects": [
    {
      "mpiId": 678,
      "projectName": "Tower on Robson",
      "province": "BC",
      "city": "Vancouver",
      "role": "Architect",
      "verdict": "PURSUE"
    }
  ],
  "knownPeople": [
    { "name": "Richard Henriquez", "title": "Principal" }
  ],
  "recentSignals": [
    { "type": "Win", "subject": "...", "detail": "...", "occurredAt": "2026-03" }
  ]
}
```

## SQL approach

### Main query — eligible orgs

An org is eligible for `honing-orgs` when:
- Has an existing `FirmNarrative` enrichment (`CanonicalOrgEnrichment` WHERE
  `ProviderName = 'FirmNarrative'` AND `Status = 'ok'` AND `ResultJson IS NOT NULL`)
- Kind IN ('Architect','Buyer','GC','Competitor','Developer','KorClient')
- `RetiredAtUtc IS NULL`
- Not already honing-fresh: either no `FirmNarrativeHoning` enrichment exists
  OR its `NextRefreshAtUtc < SYSDATETIMEOFFSET()` (stale)
- Id not in already-emitted set (same pattern as people/orgs)

Order: by Kind ASC, then by number of linked active MPIs DESC (highest-signal
orgs first). If MPI count is complex, just ORDER BY co.Id ASC for simplicity.

### Sub-queries per org (in PowerShell, after the main read loop)

After reading each row, make additional SQL queries against the same connection:

**linkedActiveProjects** — up to 10 most recently updated MPIs linked to this org:
```sql
SELECT TOP 10
    m.Id AS mpiId,
    m.ProjectName AS projectName,
    m.Province AS province,
    m.City AS city,
    CASE
        WHEN m.ArchitectCanonicalOrgId = @orgId THEN 'Architect'
        WHEN m.GeneralContractorCanonicalOrgId = @orgId THEN 'GC'
        WHEN m.StructuralEngineerCanonicalOrgId = @orgId THEN 'StructuralEngineer'
        WHEN m.ProponentCanonicalOrgId = @orgId THEN 'Proponent'
        ELSE 'Other'
    END AS role,
    COALESCE(
        NULLIF(JSON_VALUE(e.ResultJson, '$.honingPass.verdict'), ''),
        NULLIF(JSON_VALUE(e.ResultJson, '$.verdict'), '')
    ) AS verdict
FROM opportunities.MajorProjectsInventory m
LEFT JOIN opportunities.CanonicalOrgEnrichment e
    ON e.CanonicalOrgId = m.Id   -- NOTE: MPI enrichment is stored with CanonicalOrgId = MPI.Id
    AND e.ProviderName IN (N'ProjectBriefHoning', N'ProjectBrief')
    AND e.Status = N'ok'
WHERE m.RetiredAtUtc IS NULL
  AND (m.ArchitectCanonicalOrgId = @orgId
    OR m.GeneralContractorCanonicalOrgId = @orgId
    OR m.StructuralEngineerCanonicalOrgId = @orgId
    OR m.ProponentCanonicalOrgId = @orgId)
ORDER BY m.UpdatedAtUtc DESC;
```

**knownPeople** — up to 8 current people at this org:
```sql
SELECT TOP 8
    p.DisplayName AS name,
    a.Title AS title
FROM opportunities.IntelPersonAffiliation a
INNER JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId
WHERE a.CanonicalOrgId = @orgId
  AND a.IsCurrent = 1
  AND a.RetiredAtUtc IS NULL
  AND p.RetiredAtUtc IS NULL
ORDER BY a.LastSeenAtUtc DESC;
```

**recentSignals** — up to 5 most recent IntelSignal rows for this org:
```sql
SELECT TOP 5
    s.Type AS type,
    s.Subject AS subject,
    s.Detail AS detail,
    FORMAT(s.OccurredAtUtc, 'yyyy-MM') AS occurredAt
FROM opportunities.IntelSignal s
WHERE s.CanonicalOrgId = @orgId
ORDER BY s.OccurredAtUtc DESC;
```

**IMPORTANT**: Verify the exact column names (Type, Subject, Detail, OccurredAtUtc)
against `64_IntelEntities.sql` before using them. If the schema differs, adjust.

## PowerShell row construction

For each org row from the main query:
1. Parse `ResultJson` → `ConvertFrom-Json -Depth 20` → store as `$firstPassNarrative`
2. Run the `linkedActiveProjects` sub-query with `@orgId`
3. Run the `knownPeople` sub-query
4. Run the `recentSignals` sub-query
5. Build the batch row PSCustomObject with all 6 fields:
   `id`, `displayName`, `kind`, `firstPassNarrative`, `linkedActiveProjects`,
   `knownPeople`, `recentSignals`

Use a SINGLE SqlConnection that stays open for all sub-queries on one org
(don't open/close for each sub-query — reuse the connection).

## Batch size

Default `-Take` for honing-orgs: 20 per batch (not 200 — each item gets 15-25
tool calls in the drain session; 20 items = ~400 tool calls ≈ one session).
The `-Take` param already defaults to 200 in the script; override the help
message to say 20 for this kind.

## Constraints

- Do NOT run dotnet build or dotnet test
- Do NOT change any other file
- Do NOT change the existing `people`, `orgs`, `unknown-orgs`, or
  `honing-people` cases — only add the new `honing-orgs` case
- Keep the `already-emitted` dedup logic (same pattern as other kinds)
- The output batch file goes to `\\KOR-APP01\QueueDrain\honing-orgs\inputs\batch-NNN.json`
  (the drainFolder mapping is `'honing-orgs' → 'honing-orgs'`)
- Sub-queries must use parameterized queries (`@orgId` param), NOT string
  interpolation into SQL
