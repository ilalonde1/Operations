# Codex brief — break the four things built on 2026-08-27

## Your job

Four capabilities were added today, all of which put numbers in front of a structural engineer or an
estimator. **Try to make them wrong.** Not review them — falsify them. A number that is quietly out
by 5% and looks plausible is worse than a crash, because it reaches a bid.

Read-only. No builds, no `dotnet test`, no git commands, no file changes. Read the code, reason
against real inputs, and report defects with the input that triggers them.

Rank every finding by **whether it can put a wrong number in front of a person with nothing in the
output saying so.** That is the only severity axis that matters here.

## What was built

| commit | thing |
|---|---|
| `5eb1cf3c` | `Kor.Operations.EngineeringTools.Core/E2kQuantityTakeoff.cs` — prices the concrete in a generated ETABS model |
| `514f7a19` | `Kor.Operations.EngineeringTools.Core/E2kModelQuery.cs` — answers questions about a finished model |
| `65b352c4` | `Kor.Operations.App/EngineeringTools/PdfToSafe/PdfToSafeWindow.xaml.cs` — a load diagnosis, plus a measurement test |
| `950c13c9` | `Kor.Operations.EngineeringTools.Core/RevitScheduleImporter.cs` — reads raw Revit schedule exports |

CLI verbs in `Kor.Operations.EngineeringTools.TakeoffCli/Program.cs`: `e2k-takeoff`, `e2k-ask`,
`revit-takeoff`.

Binding context you must read first: `docs/Takeoff-Doctrine.md` (four gates), `CLAUDE.md` (rules 5,
9, 10), and `Kor.Operations.EngineeringTools.Core/StructuralTakeoffService.cs` (what consumes these
rows).

## The specific attacks I want tried

### 1. `E2kQuantityTakeoff` — the concrete in an ETABS model

- **Openings.** An opening is deducted from a floor when the opening's **centroid** falls inside the
  floor polygon. Find the cases that breaks: an L-shaped or crescent floor whose centroid is outside
  itself; two floors overlapping on one storey so a hole is deducted twice or from the wrong one; an
  opening larger than the plate; an opening on a storey where the plate is assigned to a different
  storey name for the same floor.
- **Wall length.** A `PANEL` is measured as the distance between the **first and last distinct** plan
  points. What does a three-point or curved panel do? A panel whose two ends coincide? Does any real
  generated model produce one?
- **Storey rise.** `StoreyRise` walks *down* from each storey to the nearest one more than
  `SameFloorTolerance()` below. `SameFloorTolerance` is half the median rise **within one building's
  stack**. Construct a storey list where this returns the wrong rise — a podium with a tall transfer
  level, a stack of two storeys, a model where every storey is within tolerance of its neighbour.
  What happens when `ReadStories()` returns one storey? Zero?
- **Double counting.** `DropMembersDuplicatedOnOneFloor` exists on `E2kDocument` but the takeoff does
  not call it. Can one physical member be priced twice because it is assigned to two storeys?
  `StoreysByObject` returns a *list* of storeys per object — the takeoff reads `AREA ASSIGNS` rows
  directly instead. Are there models where one object has two assign rows?
- **Units.** `LengthUnitInInches()` returns null for an unrecognised unit and the code assumes inches
  with a flag. Section thicknesses (`SLABTHICKNESS`, `D`, `B`) are assumed to be **inches always**
  while plan coordinates are scaled. Is that true for a metric ETABS model? If a model is in
  millimetres, what comes out, and does anything say so?
- **Grade.** Parsed by regex from the material name. What does `"30 MPa Floor"` vs `"4000Psi"` vs a
  material with two numbers in it produce?

### 2. `E2kModelQuery` — the answers

- The invariant that matters: **two questions about one model must not contradict.** `Storeys()`
  computes concrete via `E2kQuantityTakeoff`; `WorthALook()` uses `E2kDocument.FloorGaps()`. Find an
  input where the storey table and the concerns disagree in a way a reader would call a contradiction.
  One such bug was already found and fixed (a coverage measure being described as absence) — find the
  next one.
- `Storeys()` counts an object on a storey via `StoreysByObject`, but `E2kQuantityTakeoff` prices via
  `AREA ASSIGNS`/`LINE ASSIGNS` rows. **Two different notions of "on this storey" in one method.**
  Where do they diverge?
- `RiseByStorey` in `E2kModelQuery` is a second implementation of the rise rule in
  `E2kQuantityTakeoff.StoreyRise`, with a different fallback. Do they ever disagree? They are both
  displayed.

### 3. `RevitScheduleImporter`

- **The total row.** `IsTotalRow` matches a level starting with `TOTAL`, `GRAND TOTAL` or `SUM`. Is
  there a real level name that starts with any of those? What if Revit localises it? What if totals
  are on but the level column is blank rather than "Grand total: n"?
- **The self-check.** The grand total is compared against the sum of rows in **that file**. If the
  file holds two categories, or the schedule is filtered, is the check still meaningful — or does it
  produce a false WARNING that trains people to ignore warnings?
- **Header detection** scans the first 10 rows for one naming both a level and a volume. A schedule
  with a "Level" column but volume named something else? A schedule where a *data* row happens to
  parse as a header?
- **`ReadVolume`** takes `^(-?\d+(?:\.\d+)?)\s*([^\d\s]*)$` after stripping commas. What does it do
  with `"1 234 m³"` (space thousands), `"1.234,56 m³"` (European), `"12'-6\""`, a negative volume, or
  a cell Revit writes as `"489.03 m³ "` with a trailing tab?
- **Element from title.** `FOOTING` beats `COLUMN` — check the order of every branch against real
  Revit default schedule names, including "Structural Rebar Schedule", "Wall Schedule" on an
  architectural wall category, and a renamed schedule like "Concrete — Level 3".
- **Unit.** Imperial is detected only by a unit string starting `yd` or equal to `CY`. What does
  Revit actually write for imperial volumes? If it writes `CF` or `ft³`, this silently prices cubic
  feet as cubic metres. **This one I could not verify — no imperial export was available.** Say what
  Revit really emits.

### 4. `PdfToSafeWindow.DiagnoseLoad`

- It is called on load and on page change. Is there a third path that still shows the old
  "Ready for configuration and export." message while extracting nothing? Grep every `SetStatus`.
- Does the export path refuse an empty geometry, or will it write an empty `.e2k`/`.f2k` anyway?

## Two claims to check rather than trust

1. **"The two published 31168 models price building C identically."** The test
   `ShippedModelsAgreeWithEachOtherTests.TheTwoPublished31168ModelsPriceBuildingCTheSame` asserts it.
   Is the assertion actually capable of failing — does it compare a non-empty set, and does it read
   the real published files rather than skipping silently? A test that skips is not a passing test.
2. **"Reading the raw Revit exports reproduces the verified 31065 takeoff."**
   `RevitScheduleAgreesWithTheRealTakeoffTests`. Same question: does it skip? Does column 7 of the
   answer key really hold the IFC quantity, or was that inferred from the first few rows?

## What a finding looks like

State the input, the wrong output, and what should have happened. If you cannot construct an input
that produces a wrong number, say the check is clean rather than listing style opinions — a long
list of nits buries the one real defect.

Say explicitly which of these you could **not** determine from the code alone, and what file or
answer would settle it.
