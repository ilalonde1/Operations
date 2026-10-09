# CODEX — Alberta Purchasing Connection drops the closing date on 94% of postings

**One defect. Do not fix anything else. Do not refactor the scraper.**

## The symptom, measured 2026-10-08

`APC_AllBuyers` (SourceType 11, `AlbertaPurchasingScraper`) persists a
`SubmissionDeadlineUtc` on **23 of 383** active opportunities — **6%**. In the last 30 days
it is **12 of 89**.

Every other tender source is at 100%: BcBid 200/200, BcBid_Engineering 40/40, MERX-DCC
53/53. APC is the only tender portal in the platform that loses its closing dates.

**This is not a "no deadline exists" case.** APC is a tender portal; every open
solicitation on it has a closing date. The 0%-deadline sources in this platform
(development applications, building permits, awarded contracts, news) correctly have none
and are NOT in scope here.

## Why it matters more than the percentage suggests

Those 383 rows are the Alberta regional manager's entire pipeline — the Whitecourt prime
consultant RFP, Calgary K-5 school site investigations, the Royal Alberta Museum lighting
replacement, the Calgary 10-to-12 school modernisation programme. With a null deadline
they are invisible to every "closing soon" view, to deadline-based triage, and to the
`--include-same-source` gate in `tools/BdOpportunityDedup`, which can only collapse
duplicate postings when both copies carry the same date.

## What is NOT wrong — do not re-investigate

**The extraction is correct when it fires.** All 23 captured deadlines fall AFTER their
own `IdentifiedAtUtc` and read as genuine closing dates (2026-06-22 … 2026-11-03). The
obvious hypothesis — that it grabs the POSTED date because it takes the first date in the
row — was tested against the live data and is false. The bug is that it finds nothing,
not that it finds the wrong thing.

## The concrete failure path

`Kor.Opportunities.Data/Ingestion/Scraping/AlbertaPurchasingScraper.cs`

- `FindDeadlineAsync` (line 253) resolves
  `xpath=ancestor::*[self::tr or self::li or @role='row'][1]` from the result link, and
  **returns null immediately if no such ancestor exists** (lines 258-261). A result card
  that is a `div`/`article` rather than a table row yields nothing.
- When a container is found, it scans its inner text with `DateLikeRegex` (line 31):
  `\b(?:\d{4}-\d{1,2}-\d{1,2}|[A-Z][a-z]{2,8}\s+\d{1,2},?\s+\d{4}|\d{1,2}/\d{1,2}/\d{4})\b`
  and returns the **first parseable match**. A row that renders its closing date in any
  other shape — `2026-10-30 14:00 MST`, `30-Oct-2026`, `Oct 30/26` — matches nothing.
- Either way it returns `null`, and `null` is persisted silently. No diagnostic, no
  `IngestionRunDiagnostics` warning, no failed run. The source has looked healthy
  throughout.

## The question to answer

**Where does APC actually publish the closing date for a posting, and why does
`FindDeadlineAsync` not see it for 94% of them?**

Establish which of these it is, with evidence from the live site:

- **(a)** the date is in the result row but in a format `DateLikeRegex` does not match;
- **(b)** the result markup is not `tr`/`li`/`[role=row]`, so the ancestor lookup returns
  null before any text is read;
- **(c)** the date is not in the listing at all and lives only on the posting's detail
  page, which this scraper never opens.

If **(c)**, say so plainly and scope what fetching the detail page would cost — 383 rows
is not a reason to fetch thoughtlessly, and the Worker's own guidance warns that many
small requests hit the run cancellation timeout (`few and large`).

## The rule that must hold afterwards

**An APC posting that yields no closing date must be visible, not silent.** At minimum it
records a diagnostic that reaches `ErrorSummary`, so a future regression shows up in the
run record rather than as a quiet null. The Worker's `CLAUDE.md` states the governing
principle: *"An ingestion job must assert FRESHNESS, not fetch success"* — the same logic
applies to a field that is mandatory for the source type.

Do not make a missing deadline throw. Unlike an incomplete ArcGIS read, a single missing
field is not evidence the fetch was incomplete, and failing the run would discard good
rows.

## Shape of the expected answer

Either the corrected selector/pattern with a before/after count of deadlines captured on a
live run, or a demonstration that the date is simply not in the listing, plus the cost of
getting it. State which. Do not widen the regex speculatively without showing the match
count move.

## Reproduce

```sql
-- coverage, APC vs every other tender source
WITH x AS (
  SELECT DISTINCT o.Id, o.SubmissionDeadlineUtc, s.Name AS Src
  FROM opportunities.Opportunities o
  JOIN opportunities.OpportunityObservations ob ON ob.OpportunityId = o.Id
  JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
  WHERE o.DismissedAtUtc IS NULL)
SELECT Src, COUNT(*) AS Rows_,
       SUM(CASE WHEN SubmissionDeadlineUtc IS NOT NULL THEN 1 ELSE 0 END) AS WithDeadline
FROM x WHERE Src IN ('APC_AllBuyers','BcBid','BcBid_Engineering','MERX-DCC')
GROUP BY Src;
```

Force a run; the poller picks it up within ~15 s:

```sql
INSERT INTO opportunities.IngestionTriggers (Id, OpportunitySourceId, Status, RequestedAtUtc, RequestedBy)
SELECT NEWID(), Id, N'Pending', sysdatetimeoffset(), N'codex-apc-deadline'
FROM opportunities.OpportunitySources WHERE Name = 'APC_AllBuyers';
```

⚠ `IngestionTriggers.Id` is a **uniqueidentifier**; `JobTriggerRequests.Id` is a **bigint
IDENTITY**. They are not the same shape — check `sys.columns` before writing to either.

## Out of scope

- The 174 duplicate groups that cannot be collapsed for want of a deadline. Most are
  development applications, which will never have one; they need a different
  discriminator (address or file number), not this fix.
- `AlbertaPurchasingAwardsScraper` — awards have no closing date by nature.
