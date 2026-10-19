# Engineering fix 1: an inch is an inch in every unit — the shell, the storey reader, and the test that proves it

**Budget.** This brief is about 7 KB, and it names three files: 11 KB, 11 KB, and six line ranges of a third.

**Rules for this run:**
- Open nothing else. Do not search the repo or run git.
- Do **not** run `dotnet build`, `restore` or `test`; Claude verifies on the dev box.
- Reasoning: **medium**.
- Write the response, then stop.

**Response:** `docs/codex/CODEX-ENGINEERING-FIX-1-A-MODEL-STATES-ITS-MATERIAL-IN-ITS-OWN-UNITS-RESPONSE.md`, under 40 lines.

## The fault, one class, measured 2026-10-19

Rules in this code are stated in inches. A model counts in its own unit, which `CONTROLS UNITS` declares and
`E2kDocument.LengthUnitInInches()` (line ~1999) converts. The PDF route writes **millimetre** models, and KOR's
reference models include 159 in **feet** and 29 in millimetres. Wherever an inch literal is compared with a model-unit
value without scaling, it is wrong outside inch models.

`AModelIsTheSameInInchesAndMillimetresTests` already guards the composer's own constants this way. It does not
compare materials, `ReadStories`, or feet.

**Instances to fix:**

| # | Where | Literal | What it is |
|---|---|---|---|
| 1 | `E2kShellBuilder.cs` 151-153 | `WEIGHTPERVOLUME 0.08680555`, `E 3604996.5`, `FC 4000` | a 4000 psi mix in lb-in, written under any declared unit |
| 2 | `E2kShellBuilder.cs` 133-134 | `120` | default lowest-storey height, in inches |
| 3 | `E2kDocument.cs` 165, 211 (`ReadStories`) | `480.0`, `60` | the plausible storey-height band, in inches |
| 4 | `E2kDocument.cs` 279 (`NearestFloorBelow`, static) | `12.0` | duplicate-floor tolerance |
| 5 | `E2kDocument.cs` 455, 458 | `480.0`, `60` | the same band, in a second method |
| 6 | `E2kDocument.cs` 665 | `12.0` | storeys within a foot are one level |
| 7 | `E2kDocument.cs` 1125, 1130 (`SameFloorTolerance`) | `12.0` (three times) | gap and floor tolerance |
| 8 | `E2kDocument.cs` 1217, 1238 | `36.0` | reach of `WithinOrNear` |

Ground truth for #1: a model ETABS wrote itself (30783-01, `UNITS "KIP" "IN"`) stores the same mix as `FC 4`,
`E 3604.997`, `WEIGHTPERVOLUME 8.680556E-05`. Every value is in the declared units.

## The fix

1. **One unit table.** Move the `switch` inside `LengthUnitInInches()` into
   `public static double? InchesPerUnit(string unitName)` on `E2kDocument`. `LengthUnitInInches()` calls it, with the
   same five names and unchanged behaviour.
2. **The shell** calls `E2kDocument.InchesPerUnit(unitName)`, and throws `ArgumentException` naming the unit when it
   is null. With k = inches per unit:
   - `E` and `FC` are psi × k².
   - `WEIGHTPERVOLUME` is lb/in³ × k³.
   - `U`, `A` and `GRADE` are unchanged.
   - Format: `ToString("G9", CultureInfo.InvariantCulture)`.
   - The 120 default becomes `120 / k`, formatted with the file's `Trim()`.
   - Rewrite the comment at 141-147 to say the values are in the declared unit, citing 30783-01.
3. **`E2kDocument` #3-#8:** multiply each literal by `inch`, the length of one inch in model units:
   `1.0 / (LengthUnitInInches() ?? 1.0)`. Static helpers (`FloorUnder`, `NearestFloorBelow`) take `inch` as a new
   parameter from their instance caller. In an inch model `inch == 1`, so inch models behave byte-identically.
4. **Do not change any other literal.** If another literal in the ranges below looks like an unscaled inch length,
   list it in the response with its line, and leave it.

## The test: extend the existing one

The file is `Kor.Operations.EngineeringTools.Core.Tests/AModelIsTheSameInInchesAndMillimetresTests.cs`. Read it whole;
do not create a parallel test.

- **A feet arm.** Add `var feet = Compose(root, "ft", 1.0 / 12.0, 2, "ft");` (DXF `$INSUNITS` 2 is feet, already
  supported). Assert it equals `inches` on every property the millimetre arm is already checked on.
- **Materials.** In `Compose`, read `FC`, `E` and `WEIGHTPERVOLUME` from `doc.LinesOf("MATERIAL PROPERTIES")`, and
  convert them to psi and lb/in³ with `doc.LengthUnitInInches()`. All three arms equal 4000, 3604996.5 and 0.08680555
  within 1e-6 relative.
- **Storeys.** In `Compose`, take `doc.ReadStories()`. It returns `StoryLevel(string Name, double Elevation,
  double ElevationBelow)` and does not include Base. Convert `Elevation` and `ElevationBelow` to inches. All three arms
  agree, per storey name, to 1e-4 in.
- **Two small facts:**
  - `InchesPerUnit("yd")` is null.
  - `E2kShellBuilder.FromLevels(E2kShellBuilder.ParseLevels(["L1, 0", "L2, 144"]), "in")` writes exactly these three
    lines:
    - `  MATERIAL  "KOR-DEFAULT-CONCRETE"  TYPE "Concrete"  GRADE "f'c 4000 psi"  WEIGHTPERVOLUME 0.08680555`
    - `  MATERIAL  "KOR-DEFAULT-CONCRETE"  SYMTYPE "Isotropic"  E 3604996.5  U 0.2  A 5.5E-06`
    - `  MATERIAL  "KOR-DEFAULT-CONCRETE"  FC 4000`
- **The remarks block.** Update WHAT THIS COVERS to add materials, storey elevations and floor-below, and feet. Keep
  WHAT IT DOES NOT, and add: "#4-#8 are exercised only as far as this small two-storey plan reaches them".

## Read

- `Kor.Operations.EngineeringTools.Core/Dxf/E2kShellBuilder.cs` (whole)
- `Kor.Operations.EngineeringTools.Core.Tests/AModelIsTheSameInInchesAndMillimetresTests.cs` (whole)
- `Kor.Operations.EngineeringTools.Core/Dxf/E2kDocument.cs`, these lines only:
  - 155-300
  - 440-470
  - 655-670
  - 1105-1135
  - 1200-1245
  - 1990-2020

## Do not touch

Any other file. The six models in `Baselines/` will change: their materials, and anything the storey literals
affected. **Do not re-bank them.** Claude rebuilds them and diffs every change against this brief.
