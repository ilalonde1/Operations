# BD Director 9i — the scoring loop

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-BD-DIRECTOR-9I-THE-SCORING-LOOP-RESPONSE.md` in the
9a–9h format.

## Why this exists

The Director's weekly brief (9f) makes recommendations and cites the claims they rest on. Nothing
records whether a recommendation was right, so the rule bank cannot learn and the brief cannot
improve. Ian's finish line for this window includes "eight weeks of scored recommendations
visible". Searched before writing (CLAUDE.md rule 1), and the scoring machinery already exists:

- `opportunities.BdDecision` (migration 348): `Kind` (Go, NoGo, Drop, Reassign, Price, Target,
  Other), `Decision` (500), `Rationale` (2000), `ExpectedOutcome` (500, NOT NULL), `ReviewAtUtc`,
  `DecidedBy`, `Outcome` (Pending, Right, Wrong, Unclear), `OutcomeNote`, `ScoredAtUtc`, `ScoredBy`,
  optional `EngagementId` and `CanonicalOrgId`.
- `IBdDirectorLedgerStore.RecordDecisionAsync` and `ScoreDecisionAsync(id, outcome, note, by)`;
  the Asks & decisions tab records and scores through `DecisionDialog`.
- `opportunities.DirectorBrief` (352) holds each section with its `Citations` JSON of claim ids,
  statements and sources; `knowledge.Claim` has `RetiredAtUtc` / `RetiredReason` and
  `IKnowledgeClaimStore.RetireAsync(claimId, by, reason)`; the Knowledge tab has Keep and Retire.
- The CLI `brief export` writes the week's facts and offered claims; `brief import` writes the
  five sections through `IDirectorBriefStore.ImportAsync` (9f).

So the loop is three small joins on what exists: a recommendation becomes a decision at import; the
outcome is scored where decisions are already scored; the score flows back to the claims it cited
and into the next week's export.

## The decisions, so you build the one thing

- **One decision per recommendation, created by the import, pointing at its brief row.** Kind
  `Brief` is added to the kinds. `Decision` is the section headline, `Rationale` is the
  recommendation text, `ExpectedOutcome` comes from the sections file (required when a
  recommendation is present), `ReviewAtUtc` is `weekOf` plus 28 days unless the section says
  otherwise. No new table: `BdDecision` gains `BriefRowId` referencing `DirectorBrief(Id)`.
- **Scoring stays where it is.** The Asks & decisions tab scores a Brief decision like any other.
  No new dialog.
- **The score reaches the claims through the brief row's citations**, not a link table. A claim
  cited by two or more decisions scored Wrong and none scored Right is flagged on the Knowledge
  tab with a one-click Retire whose reason names the decisions. Retirement is Ian's act, never
  automatic; the export already excludes retired claims.
- **The export shows last month's calls.** Each section of `brief export` carries the Brief
  decisions for that section from the previous four weeks with their outcomes and notes, so the
  week's sections are written knowing what scored. That is where the learning happens: in the
  writing, in session.
- **The track record is one line on the window**, computed by one SELECT: scored, right, wrong,
  unclear, pending past review; and the same split by section and by cited book.
- No model call anywhere (CLAUDE.md rule 14).

## Pieces

### Piece 1 — migration `Kor.Opportunities.Data/Schema/356_BriefDecisions.sql`

Guarded, its own batch each:
- Drop and re-add `CK_BdDecision_Kind` with `N'Brief'` added to the list (guarded by the
  constraint's definition not containing `N'Brief'`).
- `ALTER TABLE opportunities.BdDecision ADD BriefRowId BIGINT NULL;` and
  `FK_BdDecision_BriefRow` to `opportunities.DirectorBrief(Id)` `ON DELETE NO ACTION`; an index on
  `BriefRowId`.
No data statements, no grants, no new table.

### Piece 2 — the sections file and the import

- `DirectorBriefSection` gains `string? Expected` and `int? ReviewAfterDays`. Validation
  (`DirectorBriefValidation`): when `Recommendation` is non-empty, `Expected` is required, 1–500
  chars, one line; `ReviewAfterDays` 7–90 when present.
- `SqlDirectorBriefStore.ImportAsync`: inside its existing transaction, after writing the five
  rows, insert one `BdDecision` per section with a recommendation: `Kind = N'Brief'`, `Decision =
  LEFT(Headline, 500)`, `Rationale = LEFT(Recommendation, 2000)`, `ExpectedOutcome = Expected`,
  `ReviewAtUtc = weekOf at 16:00 Pacific plus ReviewAfterDays or 28 days`, `DecidedBy = by`,
  `BriefRowId = the new row's Id`, `EngagementId` and `CanonicalOrgId` null. A skipped repeat
  import writes no decisions; a forced re-import writes new rows and new decisions (history keeps
  the old). The import's write boundary extends by exactly this one insert.
- `DirectorBriefRow` gains `long? DecisionId` (the Brief decision for that row, by the join) so
  the This week tab can show "Call: Pending / Right / Wrong" under each section.

### Piece 3 — the export carries last month's calls

`DirectorBriefExportFile` sections gain `PriorCalls: IReadOnlyList<PriorCall>` with
`PriorCall(DateOnly WeekOf, string Headline, string Recommendation, string Expected, string
Outcome, string? OutcomeNote, DateTimeOffset ReviewAtUtc)` for that section's Brief decisions
whose `WeekOf` is within the previous four weeks, newest first. One SELECT-only read on
`IDirectorBriefStore`: `ListPriorCallsAsync(section, since)`. The CLI prints the count per section
beside the claims count.

### Piece 4 — the score reaches the claims

- `IKnowledgeClaimStore.ListClaimVerdictsAsync()` → `ClaimVerdict(long ClaimId, int Right, int
  Wrong, int Unclear, int Pending, IReadOnlyList<long> WrongDecisionIds)`: one SELECT joining
  `BdDecision` (Kind Brief) → `DirectorBrief.Citations` (parsed with `OPENJSON`, `claimId`) →
  grouped by claim. Only decisions with `BriefRowId` count.
- Knowledge tab: claim rows show a verdict column (`2 wrong · 0 right`); a claim with `Wrong >= 2
  AND Right = 0` gets a red flag and the existing Retire action pre-fills the reason
  `Cited by {n} brief recommendations scored wrong: decisions {ids}`. Retire still calls
  `RetireAsync`; nothing retires on its own.
- The claim search used by the export ranks a claim with `Wrong > Right` below otherwise-equal
  claims by subtracting `0.05 × (Wrong − Right)` from its score, bounded at 0.15. One line in
  `KnowledgeSearch.SearchClaimsAsync` with the verdicts injected; a fake-verdict gate proves the
  order.

### Piece 5 — the track record on the window

- `IBdDirectorLedgerStore.GetBriefTrackRecordAsync()` → `BriefTrackRecord(int Scored, int Right,
  int Wrong, int Unclear, int PendingPastReview, IReadOnlyList<(string Section, int Right, int
  Wrong)> BySection, IReadOnlyList<(string Book, int Right, int Wrong)> ByBook)`; one SELECT-only
  read; `ByBook` through the citations' `source`.
- Asks & decisions tab: one line above the decisions grid: `Brief calls: {scored} scored · {right}
  right · {wrong} wrong · {unclear} unclear · {pendingPastReview} past review unscored` and a
  second line `By section: …  By book: …`. The This week tab shows the call status under each
  section (`Call: Pending, review {BdDay}` / `Right` / `Wrong`).
- Both read on load and refresh, never on tab change.

### Piece 6 — gates

**Knowledge.Tests (`DirectorBriefTests`):** (1) a sections file with a recommendation and no
`expected` is rejected with a reason; with `expected` over 500 or multi-line, rejected; (2) the
export round-trips `PriorCalls`; (3) the search demotes a claim with two Wrong and no Right below
an equal-score claim, and never below the 0.15 bound.

**Data gates (`BdDirectorRegressionTests`):** (4) migration 356 is a guarded constraint swap, one
column, one FK, one index, no data statements; (5) `ImportAsync` writes exactly one additional
INSERT, into `BdDecision`, with Kind `N'Brief'` and `BriefRowId`, inside the existing
transaction; (6) `ListClaimVerdictsAsync`, `ListPriorCallsAsync` and `GetBriefTrackRecordAsync`
are SELECT-only and the verdict query uses `OPENJSON` on `Citations`; (7) the ledger store's
write boundary is unchanged (scoring stays `ScoreDecisionAsync`).

**App (`BdDirectorLedgerRegressionTests`, `BdDirectorKnowledgeRegressionTests`,
`BdDirectorMarketAndBriefRegressionTests`):** (8) the track record reads once on load and never
on tab change, and its two lines render from a fixture of 6 scored and 2 pending decisions; (9) a
claim with two Wrong and no Right shows the flag and Retire pre-fills the reason with both
decision ids; a claim with one Wrong and one Right shows no flag; (10) the This week tab shows
`Call: Pending, review {date}` from the fixture and `Right` after scoring, dates through
`BdDay`; (11) the date ratchet still zero; (12) the render harness re-renders
`director-asks.png`, `director-knowledge.png` and `director-week.png` with the verdict fixture;
minimum total unchanged.

## Claude's run after apply (not yours)

Build App tests, Knowledge tests and CLI; run the gate classes; render and read the three tabs;
install the App locally. Migration 356 joins Ian's next SSMS sitting. Then: re-export the week
of 2026-10-19 (now with empty prior calls), add `expected` to the five sections already written,
re-import with `--force`, confirm five Brief decisions exist with review dates of 2026-11-16, and
record the counts in the memory file.

## Read bound

About 120 KB:
- `docs/codex/CODEX-BD-DIRECTOR-9F-…` and `9G-…` briefs and responses
- `Kor.Opportunities.Data/Schema/348_BdDirectorLedger.sql`, `350_KnowledgeClaimsAndConcepts.sql`, `352_DirectorBrief.sql`
- `Kor.Opportunities.Data/Crm/IBdDirectorLedgerStore.cs`, `SqlBdDirectorLedgerStore.cs`, `IDirectorBriefStore.cs`, `SqlDirectorBriefStore.cs`
- `Kor.Opportunities.Data/Knowledge/IKnowledgeClaimStore.cs`, `SqlKnowledgeClaimStore.cs`
- `Kor.Opportunities.Knowledge/Brief/` (all five), `Search/KnowledgeSearch.cs`, `Store/IKnowledgeStore.cs`
- `tools/BdKnowledgeIngest/Program.cs` (the brief verbs)
- `Kor.Operations.App/BusinessDevelopment/Director/BdDirectorViewModel.cs`, `BdDirectorWindow.xaml` (Asks & decisions, Knowledge, This week), `DecisionDialog.xaml.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorLedgerRegressionTests.cs`, `BdDirectorKnowledgeRegressionTests.cs`, `BdDirectorMarketAndBriefRegressionTests.cs`, `BdDirectorRegressionTests.cs`, `BdScreensRenderTests.cs` `RenderDirector`
- `Kor.Opportunities.Knowledge.Tests/DirectorBriefTests.cs`

Do not open the live database, the Books folder or any `.claims.json`. App projects with `UseWPF`
have no implicit `System.IO`.

## Files

Creates: `Kor.Opportunities.Data/Schema/356_BriefDecisions.sql`; the RESPONSE.

Edits: `IDirectorBriefStore.cs`, `SqlDirectorBriefStore.cs`, `IBdDirectorLedgerStore.cs`, `SqlBdDirectorLedgerStore.cs`,
`IKnowledgeClaimStore.cs`, `SqlKnowledgeClaimStore.cs`; `Kor.Opportunities.Knowledge/Brief/DirectorBriefExport.cs`,
`DirectorBriefFile.cs`, `DirectorBriefValidation.cs`, `Search/KnowledgeSearch.cs`; `tools/BdKnowledgeIngest/Program.cs`;
`BdDirectorViewModel.cs`, `BdDirectorWindow.xaml` + `.xaml.cs`; the four App test classes named above, `BdScreensRenderTests.cs`,
`Kor.Opportunities.Knowledge.Tests/DirectorBriefTests.cs`.

## Response

The 9a–9h format. State plainly that nothing was built, run, applied or deployed, that no new
table was created, that nothing retires a claim without a user's action, and that no model call
exists in the change. `git diff --check` clean. Do not commit.
