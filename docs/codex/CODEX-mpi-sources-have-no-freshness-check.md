# CODEX — the weekly audit prints a green tick for sources it never checked

**One defect. Do not change `Q9SourceStalenessSql`. Do not wire any source. Do not touch
`tools/BdIntegrityCheck`. Do not fix San Diego's dated URL — that is a separate brief.**

## Start here: this is NOT the brief you already answered

`docs/codex/CODEX-source-staleness-blind-to-early-signal.md` and its RESPONSE (2026-10-08)
already settled Q9's coverage. **Read both first.** They established, and this brief accepts
without re-litigating:

- Q9 evaluates 115 of 125 enabled sources, including 15 of 15 early-signal sources.
- The ten it does not evaluate are excluded for stated reasons: 3 `IsHistorical` (archive
  tables), 1 type 7 BdOutreach (no polling), **6 type 18** (*"internal provider upserts record
  zero ingestion counters by design; documented Q9 exception"*).
- The `InsertedCount > 0` production definition shipped on 2026-10-08 and took alerts 22 → 41.
  That is current, correct, and **out of scope here.**

Re-verified live for this brief: `125 enabled = 115 evaluated + 3 historical + 1 type-7 +
6 type-18`. The arithmetic closes. **The exclusions are right and must stay.**

That response ends at *"every currently enabled row is accounted for."* Accounted for is not
monitored, and it also noted — correctly, in passing — *"The published report itself lacks this
denominator and exclusion inventory."* That sentence is the defect this brief is about.

## The defect

`DataHealthAuditJob.cs:465-467`:

```csharp
sb.AppendLine("## I. Source staleness sentinels");
if (staleSources.Count == 0)
{
    sb.AppendLine("All enabled sources produced within their window. ✔");
```

When Q9 returns no rows the report asserts a property of **all enabled sources** on evidence
covering **115 of 125**. Ten sources it never looked at are inside the word "all".

And for six of those ten, nothing else looks either. **0 of the 9 report sections (A–I) reads
freshness for a type-18 source.** `MajorProjectsInventory` appears twice in the whole file,
both inside Q3's bare-target org-reference counts (`DataHealthAuditJob.cs:42` and `:48`) —
neither is a freshness check.

`CLAUDE.md` rule 11 names this exact failure mode: *"A broad name on a narrow check is worse
than no check, because the next person reads the name and stops looking."*

## Who is behind that tick, measured 2026-10-08

The six unchecked sources are the **entire United States footprint** plus both forward
pipelines:

| Source | Province | Rows added, last 30 d | Days since a new row |
|---|---|---:|---:|
| `CA_CEQAnet`, `CA_SocrataSF`, `CA_SocrataSanDiego`, `CA_SanJoseCkan` | CA | **94** | 5 |
| `BC_MajorProjectsInventory` | BC | 3 | 26 |
| `AB_MajorProjectsInventory` | AB | **0** | **98** |

California is the layer Ian points at as proof the platform works. If CEQAnet stops, the
report keeps printing ✔.

⚠ **Alberta's 98 days is NOT a defect and the check must not treat it as one.** I pulled the
live `majorprojects.csv` (835 rows) and compared every row against all 1,466 AB rows we hold,
on project name: **0 of 835 missing**, including all 55 the publisher itself flags `Recently
Added = true`. The publisher is quiet; our read is complete. Likewise `308 live of 835` is
deliberate — the retirements are the June honing work removing energy, transportation and
civil infrastructure, which is correct for a structural practice. Treat a long gap as a
**review prompt**, exactly as the shipped Q9 comment already says of DEAD-GREEN.

## The change

Two parts, one defect — the report must not claim more coverage than it has.

**1. Make §I state its own denominator.** The zero-row line becomes a statement of what was
evaluated and what was not, naming the excluded sources and the reason, in the artifact. A
reader must be able to see the hole without already suspecting it. The non-zero branch should
carry the denominator too.

**2. Add §J — freshness for the type-18 sources**, so the hole is small rather than six wide.
`opportunities.MajorProjectsInventory` carries `FirstSeenAtUtc`, `LastSeenAtUtc` and
`UpdatedAtUtc`, so this is writable without touching a provider:

- `MAX(FirstSeenAtUtc)` is **production** — a genuinely new project appeared.
- `MAX(LastSeenAtUtc)` is **fetch** — the file was read and rows re-seen.

Those two diverging is the signal. Fresh `LastSeen` with old `FirstSeen` is the Coquitlam
shape: the fetch works, the publisher has gone quiet, and only the second number says so.
Pick the window from the cadence of each feed, not a copied 14 days — CA moves weekly, the
MPIs are quarterly. State the window you chose and why.

Follow §I's own precedent for the verdict vocabulary rather than inventing a new one.

## The rule that must hold afterwards

**No section of this report may assert a property of "all enabled sources" unless it evaluated
all enabled sources. Where a section excludes sources, the report names them and the reason, in
the report.**

And the generalisation, which is the point: a source added tomorrow under a type nobody
thought about must show up as *unevaluated* in the artifact, not vanish behind a tick. Prefer a
shape where the exclusion list is derived from the same source universe the query uses, so the
two cannot drift — a hand-maintained second list of exclusions is how this recurs.

## ⚠ Required in the answer: what the check covers and what it does not

Per `CLAUDE.md` rule 11, state in §J's own header comment and in your reply:

- the properties it compares,
- the properties it does not,
- **and at least one same-class fault it would NOT catch.**

A starting example, to be improved on, not copied: this check sees whether rows arrive. It
cannot see a feed that keeps arriving with its content silently truncated — San Diego's
`approvals_created_2026_datasd.csv` serving a frozen file would keep `LastSeenAtUtc` fresh and
`FirstSeenAtUtc` would simply stop, which this catches; but a feed that drops half its columns
while still inserting rows would pass.

## Shape of the expected answer

- The §I wording before and after, with the live denominator it now prints.
- §J's query, the window chosen per feed family and the reason.
- The report rendered once against live data — **paste §I and §J verbatim.** Not a description
  of what it would say.
- Confirmation that Alberta at 98 days renders as a review prompt and not an error.
- The coverage/non-coverage statement above.
- Test names. The repo convention is that a check ships with a test, not a claim.

## Verification bar

Read-only against the database except for the report file itself. `dotnet test` green before
you report — the Worker tests are in the fast filter, so `dotnet test --filter "Speed!=Slow"`
is sufficient and takes about 22 seconds; you do not owe the 7-minute geometry suite for this.

## Reproduce

```sql
-- the denominator the tick currently hides
SELECT COUNT_BIG(*) AS Enabled,
       SUM(CASE WHEN IsHistorical = 1 THEN 1 ELSE 0 END) AS Historical,
       SUM(CASE WHEN IsHistorical = 0 AND SourceType NOT IN (0,7,18,99) THEN 1 ELSE 0 END) AS EvaluatedByQ9,
       SUM(CASE WHEN IsHistorical = 0 AND SourceType = 18 THEN 1 ELSE 0 END) AS Type18Unchecked
FROM opportunities.OpportunitySources WHERE IsEnabled = 1;

-- production vs fetch, per feed family: the two numbers §J turns on
SELECT Province,
       COUNT_BIG(*) AS Rows_,
       SUM(CASE WHEN RetiredAtUtc IS NULL THEN 1 ELSE 0 END) AS Live,
       MAX(FirstSeenAtUtc) AS NewestRowAppeared,
       DATEDIFF(day, MAX(FirstSeenAtUtc), SYSDATETIMEOFFSET()) AS DaysSinceNewRow,
       DATEDIFF(day, MAX(LastSeenAtUtc),  SYSDATETIMEOFFSET()) AS DaysSinceSeen,
       SUM(CASE WHEN FirstSeenAtUtc >= DATEADD(day,-30,SYSDATETIMEOFFSET()) THEN 1 ELSE 0 END) AS AddedLast30d
FROM opportunities.MajorProjectsInventory
GROUP BY Province ORDER BY COUNT_BIG(*) DESC;
```

Last report to compare against:
`\\KOR-APP01\C$\ProgramData\KorOperations\DataHealthAudit\latest.md`

## Out of scope, explicitly

- **`Q9SourceStalenessSql`.** Its universe, its thresholds and its `InsertedCount > 0`
  definition are current and correct. Do not touch it.
- **The type-18 exclusion itself.** It is mechanically right: those providers write to
  `MajorProjectsInventory`, not `OpportunityObservations`, and record 0 in `IngestionRuns`.
  Verified: 6 of 6 have zero observations and zero inserting runs. Do not "fix" it by widening
  Q9 — that would flag all six permanently.
- **San Diego's dated URL.** `approvals_created_2026_datasd.csv` has the year as a literal in
  `BaseUrl` and nothing rewrites it; 2024/2025/2026 return 200 and 2027 returns 403, so it
  breaks on 1 January 2027. Real, verified, and a separate brief.
- **`tools/BdIntegrityCheck`.** The earlier RESPONSE showed its `source_went_silent` would miss
  these too. Reference only.
- **Nine AB rows retired with a `RetiredAtUtc` and no `RetiredReason`.** Noted, not yours.
- Wiring Calgary or Edmonton. Blocked behind nothing in this change; just not this.
