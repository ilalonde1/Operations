# CODEX — DXF→ETABS: THE REPORT MUST DESCRIBE THE FILE AFTER THE CUT

> **IMPORTANT: Do NOT run `dotnet build` or `dotnet test`.**
> Verification happens on the dev box on Claude's side. Your test runner consistently hangs here for
> 15+ minutes and spawns orphan dotnet processes that lock build artifacts. Apply the edits,
> sanity-grep your own diff, then ping. Stop there.
>
> **Do NOT run any destructive git operation** — no `git clean`, no `reset --hard`, no force push.
> **Do not touch anything under the 31168 job share.** The published files are evidence until this
> lands.

This is the fix for the audit in `CODEX-DXF-TO-ETABS-FULL-AUDIT.md`. Its response file carries my
verification of every finding — read both. Four defects were confirmed against the shipped
artifacts, one was not demonstrated, one was refuted. **Fix the four. Do not fix the other two.**

---

## The one fault, stated once

**The report and the workbook are written from the composition. The file is written from the cut.
Nothing measures one against the other.**

The tool composes the whole site once, then cuts a one-building model out of it — storeys dropped,
storeys renamed, other buildings' objects removed, duplicate assigns deduped. The `.e2k` that
reaches the engineer is the product of all of that. The sentences and numbers beside it are, in
several places, the product of none of it.

There is already one guard for this class, `NotesAboutStoreysThisModelHas`
(`DxfToEtabsService.cs:1905`). **It rewrites storey NAMES in flag text and never looks at numbers,
never reaches the workbook, and never reaches the sheet table.** Every confirmed defect below walks
straight past it. That is the shape of the fault: a guard aimed at one noun in one channel.

I do not want four patches. I want the boundary made explicit — one place that knows what the saved
file contains, and every describing surface reading from it — and then the four symptoms fall out.

**Scope discipline: this is the describing layer only.** Do not change geometry, classification,
`RisesTo`, the building cut, or storey placement. The audit attacked all of them and they held: the
two models agree exactly on all 8 of building C's own storeys and are proper subsets on the 4
shared, which is the load-bearing invariant and was broken for a week before it held. If a fix here
looks like it needs a geometry change, **stop and say so in your reply instead of making it.**

## The four, with the evidence that confirmed each

All measured 2026-08-31 against `31168-FROM-DRAWINGS.e2k` (building C, 13 storeys) and
`31168-TOWERS-FROM-DRAWINGS.e2k` (the site, 63 storeys), published 28 Aug 19:48/19:50.

### 1. The workbook asks the engineer about a storey her file does not have — CRITICAL

`31168-QUESTIONS.xlsx` row **S7**, `NEEDS YOU`: *"IS THIS A FLOOR, AND HOW THICK? … **B-LEVEL 28**:
1,298 sq ft at (3273, 2327)."* The building-C file contains no `A-` or `B-` storey at all.

`ModelQuestionnaire.cs:618` reads `sheet.Stories.FirstOrDefault()` from `report.Sheets`, which is the
**pre-cut** placement ledger. `ModelQuestionnaire` contains no reference to the saved file's storey
list and none to `NotesAboutStoreysThisModelHas`.

Note the comment sitting directly above that code: this question was *added* because a storey once
shipped with no diaphragm and nothing asked about it. The fix for one gap opened this one. Whatever
you do here must not close it by deleting the question — a real unpriced floor still has to be asked
about.

### 2. The same workbook tells her a storey with three slabs has none — CRITICAL

Rows **F2** and **J1** both name `LEVEL 1 MEZZ`: *"left without a plate rather than given an
invented one… They need a slab edge drawn"*, *"carry walls and columns and no slab, so they have no
diaphragm until you add one"*, *"There is no closed outline on any slab layer of these storeys"*.
The same sentence is in the report at `31168-FROM-DRAWINGS-report.txt:197`.

The file gives `LEVEL 1 MEZZ` **three** plates: `KF5`, `KF6`, `KF7` — 2,754 + 2,330 + 1,095 sq ft.
They are the engineer's own three mezzanine slabs, and closing the third took a day on 28 Aug.

The cause is `E2kDocument.FloorGaps()` (`E2kDocument.cs:908`). Its plateless test at **`:973`** is
`mine.Count(m => Covered(m, above)) * 2 < mine.Count` — *fewer than half this storey's members stand
under a plate.* **That is coverage, not existence.** A mezzanine is a partial floor by definition —
125 members, three small slabs — so it trips every time.

The other call site already knows. `E2kModelQuery.cs:141` names the identical value `mostlyUncovered`
and carries this comment: *"FloorGaps measures COVERAGE, not existence… Saying 'has no slab' of a
storey that visibly has one in the table above is the tool contradicting itself, so it says what it
actually measured."* **The fix was written and applied to the `/ask` path and never swept to the
report path**, which is the one she reads. `FloorGaps` has zero test references in the repo.

**My design decision, so you do not have to guess it:** split the return. One list is *storeys whose
floor carries no plate at all* — that is a real condition, it is what `B-LEVEL 28` genuinely is in
the site model, and it keeps the "add a plate" sentence honest. The other is *storeys where most of
the structure stands outside every plate on the floor* — reported in the words `E2kModelQuery`
already uses, and never as "no slab". Both the report and the questionnaire read the split; neither
gets to conflate them again. `IsMezzanineStorey` (defined `E2kGeometryComposer.cs:1614`, applied at
`:1392` and `:1639`) already suppresses the sibling coverage warning for mezzanines, with a comment
saying asking her about one *"a third time is how a questionnaire stops being read"* — decide whether
that suppression belongs in `FloorGaps` too, and say which you chose and why.

Also fix the fabricated supporting prose that rides along: J1 claims *"Closure tolerance was tested
at 6, 12 and 18 inches on this job and the result did not change."* The slab that closed it was
found at a flood-fill bridge of 126 in. A sentence that recites a test that did not settle the
question is worse than no sentence.

### 3. The building-C report presents tower sheets as having filled it — MATERIAL

The placed-sheet table lists `A-LEVEL 28`–`32`, `B-LEVEL 30`–`35` and `S2.21.1_2_A-LEVEL 27` with
nonzero walls, columns and slabs — `B-LEVEL 33` at 28/73/2 — in a file with zero `A-`/`B-` storeys.
`DxfToEtabsService.cs:2221` divides rows on `s.Stories.Count > 0`, and `s.Stories` is captured before
`KeepOnlyTower`, `KeepStoreysUpTo`, `DropStoreys` and the renames.

**The intended design is already written down three lines above that code** — the comment at `:2217`
says the tower sheets *"were removed on purpose; they belong under a heading that says so."* So the
decision is made and the split exists; it is simply keyed on the pre-cut story list instead of the
saved file's. Rewire it, do not redesign it.

**Do not solve this by deleting those rows.** A drawing that was read and then left out is something
the engineer needs told — that is the whole point of the existing *"2 drawing(s) carry structure that
is NOT IN THIS MODEL"* note, and it was hard-won. The heading the comment already asks for is the
answer, so the placed table means "these are the drawings that filled this file."

Related, same table: the `Cols` column is captured before the declined-circle pass.
`S2.22.1_1_LEVEL 33 PLAN - BLDG A` reports **49** columns; `A-LEVEL 33` in the file has **24**. That
is the grid-bubble sheet — 32 ten-inch polygon circles in the perimeter band, part of the 96 declined
across the set. The row should state what the model got; the declines are already reported separately
and should stay reported.

### 4. The cut leaves 1,075 orphan joints in the file — MATERIAL, and this one is in the `.e2k`

Both reports print `Joints : 1835`, and both files really do contain 1835 `POINT` definitions — so
this is not a stale print, it is the file. In the building-C model **only 760 of those points are
referenced by any `LINE` or `AREA`. 1,075 are orphans**, the site composition's point table left
behind when the objects standing on them were cut. The site model is 1835/1835, zero orphans.

⚠ **The trap that makes this dangerous.** On a gap-fill job the reference model's own objects are
carried through into the output and **they are not this tool's to judge** — `ShippedModelInvariants`
documents exactly this, and without that carve-out 31138 fails 514 checks, every one of them the
engineer's own work. So: prune only points **this tool generated** and that nothing references any
more. A point that came in with the reference `.e2k` stays, referenced or not. Get this wrong and you
delete an engineer's joints out of her own model.

## What must exist when you are done

**A single place that answers "what does the saved file contain?"** — storeys, members by storey,
plates by storey, joints. `E2kDocument` already reads the finished model (`ReadStories`,
`PlateNames`, `StoreysByObject`, `PlanPointsOfObjects`, `FloorGaps`) and `DxfToEtabsService`
already recomputes part of the top-line block after the cut. Consolidate rather than add a third
reader — there are already two definitions of a storey height in this codebase and that is what
cost four days.

**Every describing surface reads from it**: the top-line block, the sheet table, the flags, the
warnings, and `ModelQuestionnaire`. The questionnaire is the one that currently reads none of it, and
it is the one the engineer answers.

**New blocking invariants in `ShippedModelInvariants`**, because a publish stages to temp, verifies,
and only then copies into her folder — that is the gate these should sit behind, not a test:

1. **No number a report prints disagrees with the file beside it.** Storeys, walls, columns, floors,
   joints. This alone catches defect 4 and would have caught the `"5 of 90 floor plate(s)"` line
   (both reports say 90; the files hold 15 and 89).
2. **No sentence in the report OR the workbook names a storey the file does not have.** The existing
   `NotesAboutStoreysThisModelHas` is the seed; it has to cover the workbook and the sheet table.
   Catches defects 1 and 3.
3. **No generated joint is orphaned.** Catches defect 4's file half. Reference-model points exempt.

**Tests.** Every check above becomes a test in `Kor.Operations.EngineeringTools.Core.Tests` in this
change — that is a standing rule here, ad-hoc checks do not stay ad-hoc. `FloorGaps` currently has
none at all; give it ones that pin the split, including a partial floor (small plate, many members)
and a genuinely plateless storey. Assert on the shipped-file shape, not on a composer intermediate.

## Traps in this codebase, named because they have each cost a day

- ⚠ **`PlanSheetNaming.Vocabulary` is a public mutable static and xUnit runs test classes in
  parallel.** A test that passes alone and fails in the suite is shared state, not flake. It was
  written off twice as unexplained before anyone looked for the static.
- ⚠ **Never write a regex through a non-raw Python string** if you generate any code that way — `\b`
  becomes U+0008 silently and the pattern matches nothing. Edit C# regexes directly.
- ⚠ **The coverage ratchets in `ModelCoverageTests` may only ever come down.** If your change makes
  one need to go up, you have lost members — say so in your reply rather than adjusting the number.
- ⚠ `short` is a C# keyword; it has bitten a variable name here before.
- ⚠ `EngineerModelBenchmarkTests` **passes silently when the share is unreachable** — an 8 ms
  "Passed! 6/6" is a skip. Do not treat its green as evidence of anything.
- The engineer **rejected borrowed slabs on 25 Aug**. Nothing here may invent, borrow or infer a
  plate, and `-InferFloors` stays off. Where a floor is genuinely absent, the answer is to say so.

## What to report back

A short response file — what you changed and why, the design decision you took on the `FloorGaps`
split and the mezzanine suppression, anything you found that the audit missed, and **anything you
believe cannot be fixed in the describing layer alone**. That last one matters: if a number can only
be made true by changing when the cut happens, I want to know before I read the diff.

No summary of the diff itself; I will read it. Write to
`docs/codex/CODEX-DXF-TO-ETABS-REPORT-AFTER-THE-CUT-RESPONSE.md`. Ping when applied.
