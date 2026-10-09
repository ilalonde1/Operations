# Big-ticket view → running App

The same `opportunities.vIslandApplications` view now classifies all application
sources. Migration 338 was applied live on 2026-10-09. The local Debug App was
rebuilt, launched, navigated to Signals, and exercised through Windows UI Automation.
These are screenshots of the running App, not a mock or a rendered test fixture:

- [Before](evidence/signals-before.png)
- [Smaller work collapsed](evidence/signals-collapsed.png)
- [After one reveal click](evidence/signals-revealed.png)

## Default and reachability

Smaller work is collapsed by default: the count and **Show** button stay visible.
This clears the default wall of residential permits while keeping recall one click
away. The Work size column and filter also expose **All work** and **Smaller work
only**. Unassessed applications and Other items remain visible by default. Bids
and Mine keep their existing membership and ordering. To grab retains its existing
eligibility rules and applies the same collapse/reveal to eligible rows. Counts
track the current search/kind filters; deep links reveal a matching hidden item.

There are 5,795 applications and 176 Other items in Signals. The default shows
3,584; the visible button states **2,387 smaller items hidden · Show**; one click
shows all 5,971. For applications alone, 3,408 + 2,387 = 5,795.

## The single authority

Migration 338 alters the existing view in place, retaining its name, columns,
latest-observation selection, `IsDated`, and dismissal exclusion from 331. The
small-work conditions from 316 are preserved verbatim and guarded by a test.
The source-name whitelist becomes the existing application-kind source types and
`bd.kind` mapping. Disabled sources' existing rows remain classified. The historical
view name no longer implies Island-only coverage; regional consumers must filter
its existing SourceName/Municipality columns.

One explicit precedence decision is added **inside this view**: Calgary's 864
land-use redesignations are retained as big-ticket signals because they change
permitted development form. This is a relationship signal, not a dollar-value
claim or planning approval. It also prevents the inherited broad `%SIGN%` test
from collapsing titles containing “Redesignation.” The feed is not accidentally
classified from an empty default, and no C# or parallel SQL classifier was added.

The hub reads `IsBigTicket` directly from the view as another result set in each
existing page request. It does not remove rows in SQL. Cross-posts retain one hub
row; nullable classification means unassessed. No scoring, gate, source config,
segment assignment, or pursuit data was changed.

## Counts

| Application cohort | Before big | Before small | Before unreached | After big | After small | After unreached |
|---|---:|---:|---:|---:|---:|---:|
| Existing Island cohort | 1,279 | 2,024 | 0 | 1,279 | 2,024 | 0 |
| Calgary | 0 | 0 | 864 | 864 | 0 | 0 |
| Surrey | 0 | 0 | 910 | 616 | 294 | 0 |
| Coquitlam | 0 | 0 | 404 | 365 | 39 | 0 |
| Maple Ridge | 0 | 0 | 122 | 114 | 8 | 0 |
| Vancouver | 0 | 0 | 100 | 88 | 12 | 0 |
| Kamloops | 0 | 0 | 46 | 42 | 4 | 0 |
| Esquimalt | 0 | 0 | 29 | 24 | 5 | 0 |
| Port Moody | 0 | 0 | 17 | 16 | 1 | 0 |
| **Total** | **1,279** | **2,024** | **2,492** | **3,408** | **2,387** | **0** |

[Every source's before/after counts](evidence/counts-before-after.csv) includes the
twelve original sources individually. Every one of their 3,303 classifications
is unchanged. The 2,492 newly reached rows split into 2,129 big and 363 smaller.

## Scorer disagreements, without changing either signal

[Complete disagreement list](evidence/disagreements.csv), with IDs, titles, scope,
scores and comparison thresholds. [Means and counts](evidence/score-means.csv).

| Comparison cohort | Big mean | Small mean | Small above big mean | Big below small mean |
|---|---:|---:|---:|---:|
| Original Island cohort | 23.81 | 14.98 | 240 | 317 |
| All applications | 27.01 | 16.58 | 316 | 1,049 |
| Saanich | 27.12 | 15.83 | 135 | 0 |

These cohorts use the actual view bit, not a separate interpretation of
“multi-unit/rezoning.” Each per-source comparison is also in the CSV. An ID can
appear in its source and aggregate comparisons; those are not distinct-row totals.

For example, Victoria 23934 scores 46 but is small: its title is “Heritage
Alteration Permit,” while its scope describes a new mixed-use building retaining
heritage fabric. Victoria 23948 scores 1 but is big: its scope describes commercial
and residential redevelopment at Quebec/Montreal/Kingston streets. Neither side
was adjusted to force agreement.

## Verification

Before the first production edit, the real corpus and view definition were frozen
under `.codex_bigticket/`, the running App was captured and visually inspected,
the coverage gate failed with 2,492 unreached rows, and the UI gate failed because
no reveal/count control existed. [Recorded UI failure](evidence/app-before-failure.txt).
The raw capture remains local; the committed `before-classification.csv` preserves
every baseline ID/source/bit for replay without that local file, and its raw-capture
SHA-256 is recorded alongside it.
The migration then passed a transactional rollback rehearsal preserving every old
classification before it was committed.

After building the changed projects in Debug and opening the running App's
Opportunities screen, the command executed for all deliverables was:

```powershell
pwsh -NoProfile -File tools/BigTicketDiff/Verify.ps1 -AppProcessId 21908
```

Use the actual running App PID on a later run. The script checks SQL coverage and
old-cohort parity, runs the fast Data tests and targeted hub tests, then clicks
the real reveal button, checks count arithmetic, and captures both UI states.
It uses existing Debug test binaries (`--no-build`) because the App is running.
No full App test suite was run. [Gate output](evidence/verification.txt).

Tests: Data **246/246**, hub **18/18**, new live reachability **1/1**.
Relevant names:

- `GeneralisationRetainsTheExistingSmallWorkPredicateVerbatim`
- `WorkSizeCollapseAndRevealPartitionSignalsWithoutDroppingUnknowns`
- `SmallWorkCountTracksSearchAndDeepLinkRevealsCollapsedApplication`
- `ToGrabRevealPreservesEligibilityAndSmallWorkCount`
- `EveryApplicationUsesTheViewAndOneRevealReachesEverySignal`

The broader existing live comparison `PagedHubReachesEveryUndismissedRowExactlyOnce`
was also run. Its hub checks passed, then its legacy `ListAsync` comparison failed
with `SqlNullValueException` in `SqlOpportunityStore.ReadScoringContext` reading
column 55. That reader is unchanged from the preceding scorer commit and is outside
this brief. This task's gate is not a claim that that older comparison passed.

Before/after `BdIntegrityCheck`: all 98 invariant counts unchanged; both have one
existing sibling-source merge error, 39 warnings and zero query failures.
[Comparison](evidence/integrity-diff.txt).

Prior-art search: `tools/`, `docs/`, and `../KOR.Drafter/db/tools/`; reused the
ScoringDiff local-corpus pattern, OpportunityHubTests/OpportunityHubLiveTests, and
BdIntegrityCheck. The September Island build prompt already documented the shared
view and its intended UI default.

## Coverage and limits

The view covers existing application-kind sources and their latest scope text.
It cannot assess value, approval, recency, or scope missing from the publisher.
An empty/uninformative description can pass; a generic residential title can
collapse a real multi-unit proposal. A same-class miss is “replace the single-family
house with 80 apartments”: the inherited exclusion still calls it smaller work.

The harness compares per-source coverage/classification, existing-cohort parity,
score disagreements, exact live hub ID reachability, and the running UI's count
and reveal behavior. It does not establish business ground truth, detect missing
source records, or test deployment on other PCs. Wrong source metadata could make
the view and hub agree on the wrong population. Screenshots were inspected for
legibility; screenshot capture alone cannot prove that text is readable.

The SQL migration is live and the changed local Debug App is running. No wider
App distribution or Worker deployment was performed.
