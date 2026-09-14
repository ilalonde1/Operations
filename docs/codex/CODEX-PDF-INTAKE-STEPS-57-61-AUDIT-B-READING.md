# Codex — adversarial audit of intake steps 57–61, brief B of two: the reading rules

**Reading set: 12 files at HEAD (`d42ad436` on `develop`), 95 KB measured with `wc -c` before handover.
No commit range. No build, no test run, no drawing files, no `.e2k`, no `.dxf`, no CSV, no
database, no network, no other repository, nothing under `Baselines/` or `docs/etabs-handoff/`.**
Recommended: reasoning **medium**. Brief A (the instruments) is a separate file for a separate
window; do not read its files from here.

## What is being audited

Steps 47 and 60 (2026-09-13) let a storey be named by a WORD (MAIN, GROUND, UPPER, a basement, a
loft) and its level be taken from the set's own framing-over clauses; a view's title may run two
lines; a building's roof plan names that building's roof (step 61). The claims are in
`docs/PdfIntake.md` §66 and §69, and §70 item 3, each with WHAT THIS COVERS / WHAT IT DOES NOT.
Your job is to find where the code does not do what those sections say, where a rule called
universal is fitted to one set (31089-01's townhouses, 01389's house, 31168's tower B), and where
a test's name is wider than its assertions.

## Read, in this order (byte sizes are what you will read)

1. `CLAUDE.md` rules 10, 11 and 12 only (about 3 KB of a 15 KB file).
2. `docs/PdfIntake.md` — §66, §69 and §70's item (3) only. About 12 KB. Do not read the rest.
3. Source, whole files:
   - `Kor.Operations.EngineeringTools.Core/Intake/SheetViews.cs` (21 KB)
   - `Kor.Operations.EngineeringTools.Core/Intake/StoreysFromPlans.cs` (16 KB)
   - `Kor.Operations.EngineeringTools.Core/Dxf/DrawingVocabulary.cs` (15 KB)
4. Source, named regions only:
   - `Kor.Operations.EngineeringTools.Core/Dxf/PlanSheetNaming.cs`: `Parse` (both overloads,
     ~90–200), `TitleOf`, `StoreyAboveTheHighestPlan` and `OneRoofOf` (~463–500). About 9 KB.
   - `Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs`: the vocabulary block only
     (~725–745, from "THE WORDS THIS OFFICE USES" to the glossary). About 2 KB.
   - `Kor.Operations.EngineeringTools.Core/Intake/SheetDxfName.cs`: `For` (the whole method). About 4 KB.
5. Tests, whole files:
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/AStoreyMayBeNamedByAWordTests.cs` (7 KB)
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/ASheetIsItsViewsTests.cs` (15 KB)
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/ASetsStoreysAreWhatItsPlansNameTests.cs` (read
     the class summary and the test names; the bodies only where a name claims a roof or a word)

## The questions, in priority order

**A. The word chain (`DrawingVocabulary.WithFloorWordsRankedBy`, §69 rule 3).** The set's
framing-over clauses rank its floor words from 1 upward; a number shown over the top word anchors
the chain; two stories about one word leave the row alone. Smallest inputs: a set whose chain is
GROUND → MAIN and whose other building's chain is MAIN → UPPER (two buildings, two chains in one
set); a clause whose over-word is a basement word; "MAIN FLOOR SHOWING MAIN FLOOR FRAMING OVER"
(a typo); a chain of one word anchored by "2ND" where the row says the word is 2 already. Is the
chain applied identically by the ladder (`StoreysFromPlans.Merge`) and the composer
(`DxfToEtabsService`) — both call it on the view names, but are those the same list of names?

**B. Two-line titles (`SheetViews.Titles`, §69 rule 2).** An underlined line that names no plan
by itself takes the line directly above it when the two together do; a stroke with another line
of text between it and a line underlines that other line. Smallest inputs: a three-line title; a
title whose SECOND line names a plan alone ("LEVEL 3" over "PLAN"); a notes column whose last line
is underlined under a heading that names a floor; two titles stacked one above the other on a
sheet with plans stacked vertically (the summary says views stacked vertically are not covered —
is that still true with the join, or worse?). `NamesAPlan` now accepts a word floor with a
framing-over clause: "MAIN FLOOR SHOWING" alone (a first line) — does it name a plan, and does
the `consumed` pass stop it becoming a view of its own in every layout?

**C. A building's roof (`StoreysFromPlans.Merge`, §70 item 3).** A tagged roof plan names
`<TAG>-ROOF` after that building's highest level; only an untagged roof plan names ROOF. Then
`PlanSheetNaming.StoreyAboveTheHighestPlan` / `OneRoofOf` must put that building's roof plan's
members on `<TAG>-ROOF`: trace it — does `MatchStories` restrict a tagged sheet to storeys of its
building, and does "C-ROOF" pass `StoryBelongsToBuilding`? A set with two roof plans for one
building (main roof and elevator roof, both tagged); a roof plan tagged for a building none of
whose plans name a level (highest unknown).

**D. Step 47 revisited (§66).** A floor word before a floor noun only; a word beside a number
(the number wins). Smallest inputs: "MAIN LEVEL 2 PLAN"; "LEVEL 2 MAIN FLOOR PLAN"; "UPPER
PARKADE PLAN" (UPPER before a non-noun); "GROUND FLOOR" in another office's sense (the floor
above a basement is level 1 here — is it in Britain?). Is `LevelOfWord`'s ordinal list bounded
(TWELFTH) and what happens above it?

**E. The checks versus their names.** For each test file above, compare WHAT THIS COVERS with
the assertions. In particular `ATitleMayRunTwoLinesAndAWordFloorWithItsFramingOverNamesAPlan`:
its fixture puts the two lines 12 pt apart with 8 pt tokens — is the join's window ("within two
heights") exercised at its boundary, and is the interposed-line rule asserted at all?

## Known and not to be re-found

- 31089-01's eleven buildings are NUMBERED (BUILDING 1 … 11) and the building tag reads letters
  only, so they stack at one place (§69 WHAT IT DOES NOT). Not a finding.
- The title reader's own failures (30768-01's "-", 30888-01's and 30980-01's word salad) are the
  next step, named in §69.
- A basement word is a parkade level by its own rule and never enters the chain (stated).

## Output

`docs/codex/CODEX-PDF-INTAKE-STEPS-57-61-AUDIT-B-READING-RESPONSE.md`. One finding per heading,
most severe first, each with: the sentence in `docs/PdfIntake.md` it contradicts (section and the
quoted words), the file and line, the smallest input that shows it, and what the code does with
that input. Findings are source deductions; say so. Do not propose fixes longer than a sentence;
do not write code. Cap: 12 findings. If you run out of real findings before 12, stop.
