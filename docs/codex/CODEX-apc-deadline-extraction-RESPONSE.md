# APC closing-date extraction — 2026-10-08

**Finding: (b), unsupported listing markup. Fixed locally; not deployed.**

The live [APC search listing](https://purchasing.alberta.ca/search) renders each
posting as an `apc-opportunity-search-result` component containing a
`div.search-result`. The old `tr` / `li` / `[role=row]` ancestor lookup matched
**0 of 10** posting links in both live captures. It returned null before reading
the date. Detail-page requests are unnecessary.

Inside the component, the closing field is a `.search-result__detail` whose
`.search-result__detail-header` contains `Closing Date`; its sibling
`.search-result__detail-value` contains, for example,
`Oct 23, 2026 • 4:00:00 pm (MT)` (AB-2026-06774).
The old date regex already matches the date portion of that value. Widening it
would not repair the ancestor failure.

The fix scopes extraction to that posting component and reads the labelled
closing value. This also avoids the outer `div.search-result` that wraps the
whole list, and avoids selecting the earlier Posting Date field. It parses the
observed full format and converts Alberta wall time using `America/Edmonton`,
including daylight/standard time and seconds. The legacy row parser is unchanged.
Unknown/missing/invalid closing values stay null; they cannot fall back to the
posting date in the card.

Missing deadlines now produce a warning log for each reference/URL and one
bounded `IngestionRunDiagnostics` warning with the missing/total count and up to
five sample references. The existing ingestion service writes it to
`ErrorSummary` as `DEGRADED: ...`, without failing the run or dropping candidates.

## Measured results

Used the production scraper via the existing `tools/ScraperProbe` project, with
the current source URL and mappings read from SQL: `/search`, maxPages 20,
buyer `Government of Alberta (APC)`. No SQL rows were changed or triggers queued.

| Check | Deadline coverage | Result |
|---|---:|---|
| Unchanged scraper, live | 0/10 | 3.55 seconds; no warnings |
| Fixed scraper, identical saved listing | 9/10 | All 9 labelled closing fields captured |
| Fixed scraper, fresh live run | 9/10 | 3.77 seconds; all 9 labelled closing fields captured |

The initial listing's remaining null is **AB-2026-06749**, a Notice of Planned
Procurement without a closing field. During this session the newest ten changed;
the fresh live run's remaining null is **AB-2026-06779**, a Notice of Award without
a closing field. Both are now diagnosed. The configured search includes notices
as well as open solicitations; no source filtering or awards scraper was changed.

For the identical snapshot, **10/10 candidates retain every non-deadline field**,
with unchanged ordering and count. Local evidence (HTML, screenshots, JSON):
`output/apc-deadline-20261008/{before,replay-after,after}/`. The rendered live
listing was inspected. The two-card regression fixture is included at
`Kor.Opportunities.Data.Tests/Fixtures/apc-listing-20261008.html`.

Scope limit: these are the **ten postings returned by the existing scraper**,
not a remeasurement/backfill of the 383 database opportunities or proof of portal
pagination completeness. No pagination changes were made. There are **zero added
detail-page requests**. Deployment and a production coverage recount remain separate.

## Verification and reproduction

The five regression cases fail on the pre-fix scraper and pass with the fix:
actual card selection/MT conversion across DST, absent closing field, invalid
closing value, unsupported container, and a successful ingestion run whose
recorded ErrorSummary contains the missing-date warning. The last test uses the
real scraper and ingestion service with an in-memory run store; it skips SQL
candidate persistence. These tests do not establish historical coverage or detect
postings omitted by pagination.

Commands run successfully in Debug:

```powershell
dotnet test Kor.Opportunities.Data.Tests/Kor.Opportunities.Data.Tests.csproj -c Debug --filter FullyQualifiedName~AlbertaPurchasingScraperTests -nologo
dotnet run --project tools/ScraperProbe/ScraperProbe.csproj -c Debug -- --apc-deadlines output/apc-deadline-20261008/after
dotnet run --project tools/ScraperProbe/ScraperProbe.csproj -c Debug -- --apc-deadlines output/apc-deadline-20261008/replay-after output/apc-deadline-20261008/before/listing.html
```

Validation stayed within the Opportunities data/tests and probe projects. No full solution build, publish,
deployment, source-config change, or database backfill was performed.

Prior-art search covered `tools/`, `docs/`, and `../KOR.Drafter/db/tools/`.
Reused `DetailPageProbe` for initial live inspection, extended `ScraperProbe`
for reproducible capture/replay, and followed the existing ingestion test pattern
for verifying ErrorSummary. Unrelated pre-existing untracked briefs were retained.
