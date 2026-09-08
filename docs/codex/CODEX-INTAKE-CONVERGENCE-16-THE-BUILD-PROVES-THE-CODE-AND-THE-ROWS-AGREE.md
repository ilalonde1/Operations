# Codex 16 of N — the build proves the compiled defaults and the banked rows agree

## Goal

Make it impossible for a compiled default to drift from the KorStandards row it mirrors without a
test failing, and fix the drift that exists today. Nothing about what a rule MEANS changes; this
step converges the two places a rule's number lives onto one source and makes the build the
witness.

## What was measured (2026-09-08)

`analysis.vw_RuleSetting` holds 49 numeric `dxf.*` rows. Compared one by one with the compiled
defaults in `PlanClassificationOptions` and `ComposeOptions` (via `BuiltInRuleValues` in
`DxfToEtabsService`, which already maps every key to its property):

| Key | Row | Compiled | Origin of the row |
|---|---|---|---|
| `dxf.max-wall-thickness` | 60 in | 36 in | migration 038, corpus of 1,126 models: 42" walls 1,256 times, 48" 831 times |
| `dxf.max-column-size` | 132 in | 96 in | migration 038, same corpus: 99.2% of 7,538 columns |
| `dxf.outline-self-touch-tolerance` | 0.5 in | 0.05 in | migration 045's committed text says **0.05**; the live row says 0.5 and is attributed to 045 |

46 of 49 agree. The first two are the code failing to follow a corpus-verified widening: a
production run (which reads the rows) admits a 42" wall, a default-mode run (tests, the WPF
window without a connection, every instrument in this programme) refuses it. The third is the
row failing to match its own migration; migration 081 (written alongside this brief, applied by
Ian) sets it back to 0.05, the value the code and the committed migration both carry, on the
migration's own evidence (a gap of exactly 0.00).

Also seen: `PlanClassificationOptions.SpandrelDepth = 24` has no row (`dxf.spandrel-depth-floor`
and `-ceiling` exist; this one is unmapped); `dxf.min-wall-thickness` appears twice in the view
because both `FormatConvention` and `Ruling` carry it; and none of the PDF side's own keys
(`dxf.pdf.slab-min-diagonal-mm`, `-line-min-length-mm`, `-column-max-size-mm`, `-column-min-dim-mm`,
`-agreement-tolerance-mm`, `-agreement-label-reach-mm`) has a row at all.

## The class, in one sentence

A rule's number lives in two places, the row and the compiled default, and nothing compares them,
so the row moves with the evidence and the default stays where it was written.

## Change this

### 1. Align the two compiled defaults to their rows

`StructuralPlanClassifier.cs`: `MaxWallThickness` 36.0 → **60.0**; `MaxColumnSize` 96.0 → **132.0**.
Update each property's remark with the corpus basis (from `docs/KOR-DxfToEtabs-HowItWorks-and-Measured-2026-08-21.md`
§"What is banked"). Nothing else in that file.

### 2. Expose the mapping

`DxfToEtabsService.BuiltInRuleValues(PlanClassificationOptions, ComposeOptions)` becomes
`internal static` (it is private today). `Kor.Operations.EngineeringTools.Core.Tests` already has
`InternalsVisibleTo`. Add the PDF side's shared keys the same way: a
`PdfIntakeOptions.BuiltInRuleValues()` returning the four wall keys and `dxf.max-column-aspect`
with their default values converted back to inches, plus the six `dxf.pdf.*` keys in millimetres.

### 3. The parity test — `Core.Tests/Rules/CompiledDefaultsAreTheBankedRowsTests.cs`

- Loads `RuleSettings.Load()` (the `KOR_ENGINEERINGTOOLS_STANDARDSDB` connection). If the
  variable is unset or the database unreachable, the test FAILS and says so — a gate that passes
  by not running is the fault it exists to catch (see `LiveProjects`). `[Trait("Speed", "Slow")]`.
- For every key in `DxfToEtabsService.BuiltInRuleValues(new(), new())` and in
  `PdfIntakeOptions.BuiltInRuleValues()`: if the row exists, the values must agree within 1e-6 in
  the row's unit (booleans as 0/1). Every disagreement is collected and the test fails once with
  the whole list, key by key, row value and compiled value.
- If the row does NOT exist, the key must be in an explicit `UnbankedByDesign` list in the test,
  each entry with a one-line reason. Seed it with the six `dxf.pdf.*` keys ("PDF-side thresholds;
  no corpus measurement yet — see PdfIntake.md §5 item 6") and nothing else. An unmapped key
  outside that list fails the test.
- A second test: every public numeric property on `PlanClassificationOptions` and
  `ComposeOptions` appears in `BuiltInRuleValues` or in an explicit `NotARule` list with a reason
  (`ExpectedSlabCount` is per job; `ModelUnitInInches` is a fact about the model; `SpandrelDepth`
  goes in here with "superseded by the floor/ceiling rows — candidate for removal", until someone
  removes it). This is the orphan detector: a compiled number nobody banked and nobody declared.

### 4. Nothing else

No reader, no classifier condition other than the two constants, no row inserted (081 is mine),
no WPF, no PowerShell.

## What NOT to do

- Do not "fix" the self-touch divergence in code. 0.05 is right on the evidence and the migration
  corrects the row.
- Do not add rows for the `dxf.pdf.*` keys. That needs the corpus measurement first.
- Do not run the suites; build Core once. I run the FULL suite for this one, because two DXF-side
  defaults change and the geometry ratchets must have their say.
- Files: `StructuralPlanClassifier.cs`, `DxfToEtabsService.cs` (one keyword), `PdfIntakeOptions.cs`,
  and the new test file. Nothing else.

## What I will check

1. Before 081 is applied: the parity test fails listing exactly one key, `dxf.outline-self-touch-tolerance`
   (0.5 vs 0.05). After Ian applies 081: it passes, 0 divergences, and the orphan test passes.
2. The full Core suite, not the fast filter. The two widened defaults may move a default-mode
   number; a coverage ratchet that RISES is the widening admitting a real 42" wall, and it is
   re-banked with that sentence. One that falls is a fault.
3. The step-3 DXF census on the thirteen baseline pages: unchanged. The PDF side already used 60.
4. `FiveStickFilesTests` 25 of 25; the ledger's five DOCUMENT lines unchanged from §9.
