# BD Director 10a — the doctrine learns from what happens

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-BD-DIRECTOR-10A-THE-DOCTRINE-LEARNS-RESPONSE.md` in the
9a–9k format.

## Why this exists

The doctrine is 110 accepted cards distilled from 18 books (`opportunities.DoctrineCard`, migration 362).
Today a card changes only when Ian accepts, rejects or amends it. Nothing the team does flows back. Ian:
"can these things truly be used as an elastic learning center?" Not yet. This brief closes the loop:

suggest (a move in the daily hour cites the cards behind it) → act (Ian marks the move Done, Not today
or Not doing, which already exists) → outcome (the pursuit the move was about advances, is won, lost or
archived, now with a one-line reason) → adjust (each card shows its track record; cards that keep being
skipped or never move a pursuit are flagged for amend or retire; the next synthesis reads KOR's own
record and proposes amendments, retirements and cards learned from experience).

Searched before writing (CLAUDE.md rule 1). What exists:

- **The hour** — `Kor.Opportunities.Data/Crm/DirectorHour.cs`: `DirectorHourContent` with `HourMove(Rank,
  Title, Minutes, WhyNow, Principle, Drafts, Steps, Links)`; `HourLink.Kind` in org|opp|pursuit|mpi|person|
  director; validated on `hour import` (CLI, `tools/BdKnowledgeIngest/Program.cs`, `HourImportAsync`);
  stored as JSON in `opportunities.DirectorHour.ContentJson` (migration 360). Written in session by Claude.
- **Move results** — `opportunities.DirectorHourMove(HourId, MoveRank, Result, Note, RecordedBy,
  RecordedAtUtc)` (migration 361); `HourMoveResult` read by `BdRhythmViewModel`; Today's Done / Not today /
  Not doing buttons write it.
- **The doctrine** — `Kor.Opportunities.Data/Crm/BdDoctrine.cs` (`DoctrineCardRow`, `IBdDoctrineStore`,
  `SqlBdDoctrineStore`: ListCards, DecideCard, StartRun, Report); statuses proposed|accepted|rejected|retired;
  `AmendsCardId` retires the amended card on accept. `BdPromptComposer.Synthesis` writes the synthesis
  prompt; `BdDoctrineViewModel` + the Doctrine tab in `BdDirectorWindow.xaml` show it.
- **Duplicates** — `Kor.Opportunities.Knowledge/Doctrine/DoctrineDuplicates.cs` (fd4e3066): `prompt report`
  refuses or holds cards that repeat existing ones. Do not change it.
- **Pursuit stages** — `opportunities.CrmEngagements` (Stage 1 Pending, 3 Submitted, 6 Won, 7 Lost,
  8 Archived; `OutcomeReason`, `ClosedAtUtc`) and `opportunities.CrmEngagementStageHistory(EngagementId,
  Stage, EnteredAtUtc, ByStaffId)`. `SqlCrmEngagementStore` (~line 277–303) keeps `OutcomeReason` for
  stages 6 and 7 only. Stage changes in the App go through `CrmViewModel.ChangeStageAsync` (~line 735) and
  the Director's pursuit actions.

## The decisions, so you build the one thing

1. **No new table for the track record.** It is computed from what is already stored: hour content
   (which cards each move cites, which pursuits it links), move results, and stage history. One pure
   function computes it; the store only reads.
2. **A move cites cards by id**: `HourMove` gains `Cards` (list of card ids, optional, default empty).
   `hour import` refuses an id that is not an accepted card of the hour's role. Old hours without `cards`
   still import and read.
3. **"Advanced"** means a pursuit linked from the move (`kind: "pursuit"`) entered a later stage, or
   was won, within 30 days after the hour's date, per `CrmEngagementStageHistory`. Lost and archived count
   separately. The function states this rule in its summary.
4. **Flags** (the only judgement in code; everything else is shown, not scored):
   `Skipped`: Not doing on its last 3 suggestions. `Idle`: suggested 5 or more times and never Done.
   `NoEffect`: Done 4 or more times on pursuit moves with none advanced. Flags appear on the card;
   nothing is retired automatically.
5. **Every close carries a reason.** Moving a pursuit to Won, Lost or Archived anywhere in the App
   asks for one line (the existing `NoteDialog`), required. `OutcomeReason` is kept for stage 8 as well.
6. **Retire is Ian's click.** A flagged card offers Retire (accepted → retired, `DecidedBy` = Ian) and
   Ask Claude to amend (opens the existing Ask Claude flow, question prefilled with the card and its record).
7. **The synthesis reads the record.** `BdSynthesisInput` gains the track records and the last 90 days of
   closed pursuits with their reasons. The prompt gets a section "What KOR's own record shows" and the
   instruction: propose amends or retirements where the record argues for them; propose a card from KOR's
   experience only when two or more outcomes share a reason, citing those pursuits by id.
8. Nothing calls a model or any outside service (CLAUDE.md rule 14). No API markers.

## Pieces

### Piece 1 — the move cites its cards (Data, CLI)

`HourMove.Cards` (`IReadOnlyList<long>`, JSON `cards`, default empty). `DirectorHour` validation:
ids must be positive and unique within the move. `hour import`: load accepted cards of the role
(`IBdDoctrineStore.ListCardsAsync`) and refuse with the move rank and the bad id when one is not
accepted. Hours already stored (ids 1 and 2) have no `cards` and must still parse.

### Piece 2 — the track record (Data)

New `Kor.Opportunities.Data/Crm/DoctrineTrackRecord.cs`:
`record DoctrineCardRecord(long CardId, int Suggested, int Done, int NotToday, int NotDoing, int Undecided,
int PursuitsAdvanced, int PursuitsWon, int PursuitsLost, DateOnly? LastSuggested, IReadOnlyList<string> Flags)`
and a pure `static IReadOnlyList<DoctrineCardRecord> Compute(IReadOnlyList<(long HourId, DateOnly ForDate,
DirectorHourContent Content)> hours, IReadOnlyList<(long HourId, int MoveRank, string Result)> results,
IReadOnlyList<(long EngagementId, int Stage, DateTimeOffset EnteredAtUtc)> history, IReadOnlyList<long>
cardIds)`, per decisions 3 and 4. A store method on the rhythm or doctrine store reads the three inputs
(hours with ContentJson, all move results, stage history for engagements linked from moves).

### Piece 3 — a reason on every close (Data, App)

`SqlCrmEngagementStore`: keep `OutcomeReason` for stages 6, 7 and 8. Every App path that moves a pursuit
to 6, 7 or 8 asks for a required one-line reason with `NoteDialog` and passes it; cancelling the dialog
cancels the move. Find every such path (`CrmViewModel.ChangeStageAsync`, the Director's pursuit actions,
the Pursuits screen) and list them in the response.

### Piece 4 — the record on the Doctrine screen (App)

Each accepted card shows one line under its title: "Suggested 6 · done 4 · not today 1 · not doing 1 ·
2 pursuits advanced" ("Not suggested yet" when zero). A "Needs a look" section above Accepted lists
flagged cards with their flag in plain words ("Skipped the last 3 times it came up") and two buttons:
Retire (asks first) and Ask Claude to amend. `IBdDoctrineStore.RetireCardAsync(id, actor)`: accepted →
retired only, returns false otherwise.

### Piece 5 — the card on the move (App)

Today: under a move's principle, "From the doctrine: <card title>" for each cited card (accepted cards
only; a retired one is shown struck through with "retired"). Clicking it opens the Doctrine tab on that
card.

### Piece 6 — the synthesis reads KOR's record (Data)

`BdSynthesisInput` gains `IReadOnlyList<DoctrineCardRecord> Records` and `IReadOnlyList<ClosedPursuit>
Closed` (`record ClosedPursuit(long EngagementId, string Company, int Stage, string? Reason, DateTimeOffset
ClosedAtUtc)`). `BdPromptComposer.Synthesis` adds "What KOR's own record shows" (flagged cards first, then
the record table, then closed pursuits with reasons, 90 days) and the instruction in decision 7. The CLI
`prompt synthesis` and `BdDoctrineViewModel.PrepareSynthesisAsync` fill both.

### Piece 7 — gates

**Data (`DoctrineTrackRecordTests`, new):** (1) a card cited on three moves, results Done/NotDoing/none,
gives Suggested 3, Done 1, NotDoing 1, Undecided 1; (2) a pursuit link whose engagement enters stage 3
ten days after the hour counts as advanced, forty days after does not, stage 7 counts as lost; (3) each
flag fires on its exact rule and not one short of it; (4) old content JSON without `cards` parses.
**CLI (`BdDoctrineRegressionTests`):** (5) `hour import` refuses a non-accepted card id with the move rank;
(6) the CLI allowed-options test still holds. **Store:** (7) `RetireCardAsync` retires an accepted card
and refuses proposed or rejected; `OutcomeReason` survives stage 8. **App:** (8) the close paths found in
piece 3 all require a reason (a test per path, or one over the shared helper if you introduce one);
(9) the synthesis prompt carries the new section and the instruction, with a fixture record and two
closed pursuits. **Render (`BdScreensRenderTests`):** (10) the Doctrine screen with one flagged and two
recorded cards; Today with a move citing a card. No model call or API marker in new code.

## Claude's run after apply (not yours)

Build and run the gate classes and the Director filter; render and read the two PNGs; install locally;
write the next hour citing card ids and import it; check the track record by query after Ian marks moves.

## Read bound

About 130 KB, measured. **Read the line ranges given, not the whole file.** Several of these files are
large (the Director window XAML is 102 KB, the render tests 62 KB, `CrmViewModel.cs` 48 KB, the CLI 43 KB)
and only a slice matters. To find anything else, search (`rg -n`) and read 40 lines around the hit; never
open a file over 30 KB whole. Line numbers are as of commit 9f9217fc; if a range has drifted, search for
the named symbol. The table columns are given in this brief, so do not open the migrations.

| File | Lines | For |
|---|---|---|
| `Kor.Opportunities.Data/Crm/DirectorHour.cs` | all (12 KB) | `HourMove`, validation |
| `Kor.Opportunities.Data/Crm/BdDoctrine.cs` | 1–200 | records, `IBdDoctrineStore`, `DecideCardAsync` |
| `Kor.Opportunities.Data/Crm/BdPromptComposer.cs` | 1–30, 50–190 | inputs, `Synthesis` |
| `Kor.Opportunities.Data/Crm/BdRhythmStore.cs` | 55–100, 230–260 | `IBdRhythmStore`, move results |
| `Kor.Opportunities.Data/Crm/SqlCrmEngagementStore.cs` | 260–320 | the stage update and `OutcomeReason` |
| `tools/BdKnowledgeIngest/Program.cs` | 255–300, 463–495, 596–690 | `HourImportAsync`, `PromptSynthesisAsync`, `ValidateFor` |
| `.../Director/BdDoctrineViewModel.cs` | all (9 KB) | the Doctrine screen's model |
| `.../Director/BdRhythmViewModel.Screens.cs` | 1–120 | `HourMoveView`, Today's moves |
| `.../Director/BdDirectorWindow.xaml` | 105–249, 365–441 | the Today and Doctrine tabs only |
| `.../Director/BdDirectorWindow.xaml.cs` | 170–260 | Ask, Synthesize, Accept handlers |
| `Kor.Operations.App/Crm/CrmViewModel.cs` | 730–790 | `ChangeStageAsync` |
| `Kor.Operations.App/Crm/CrmView.xaml.cs` | 340–375 | the stage-change caller |
| `.../Tests/BusinessDevelopment/BdDoctrineRegressionTests.cs` | 1–60 | fixtures and helpers |
| `.../Tests/BusinessDevelopment/BdScreensRenderTests.cs` | 500–620 | the Director render fixture |

Other close paths: find them with `rg -n "CrmEngagementStage\.(Won|Lost|Archived)|ChangeStageAsync" Kor.Operations.App --glob "!*Tests*"`
and read only around each hit.

Do not open the live databases, the share or Deltek. App projects with `UseWPF` have no implicit
`System.IO`. The API guard (`tools/claude_api_guard.py`) refuses the `/ask` service's method name in
Director code; name local-Claude methods plainly (the existing one is `OpenQuestionInClaudeAsync`).

## Files

Creates: `Kor.Opportunities.Data/Crm/DoctrineTrackRecord.cs`; `DoctrineTrackRecordTests.cs` (in the App
test project beside the doctrine tests, or the Data test project if it holds no WPF); the RESPONSE.

Edits: `DirectorHour.cs`, `BdDoctrine.cs`, `BdPromptComposer.cs`, `SqlCrmEngagementStore.cs`, the rhythm
store, `tools/BdKnowledgeIngest/Program.cs`, `BdDoctrineViewModel.cs`, `BdRhythmViewModel.Screens.cs`,
`BdDirectorWindow.xaml` + `.cs`, `CrmViewModel.cs` and the other close paths, the three test files.

## Response

In the 9a–9k format: what changed per piece, every close path found, the gates authored and not
executed, what was not done and why, and anything in the existing code that contradicted this brief.
Do not build, test, run tools, open a database or commit. `git diff --check` clean.
