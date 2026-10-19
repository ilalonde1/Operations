# Engineering fix 1: a model states its material in its own units

> ⏸ **HOLD 2026-10-19 — DO NOT RUN YET.** This is one instance of a wider class: inch-only constants in code that
> also handles millimetre and feet models. A second instance is `E2kDocument.ReadStories` (`maxPlausibleStoreyHeight
> = 480`, "believable" 60-480). Under CLAUDE.md rule 11, the differential harness that finds every instance comes
> first. This brief will be revised to use `E2kDocument.LengthUnitInInches()` (one unit table, not a second one), and
> its gate 5 corrected: `ReadStories` does not return Base.

**Budget.** This brief is about 6 KB, and the only file you open is 11 KB. Everything else you need is quoted below.

**Rules for this run:**
- Do **not** open any other file. Do not search the repo or run git commands.
- Do **not** run `dotnet build`, `restore` or `test`; Claude verifies on the dev box.
- Reasoning: **low**.
- Write the response, then stop.

**Response:** `docs/codex/CODEX-ENGINEERING-FIX-1-A-MODEL-STATES-ITS-MATERIAL-IN-ITS-OWN-UNITS-RESPONSE.md`, under 30 lines.

## The defect (measured 2026-10-19)

`Kor.Operations.EngineeringTools.Core/Dxf/E2kShellBuilder.cs` writes the model shell when no reference model is given.
Every PDF stick file takes that route, and it passes `unitName = "mm"`.

- Line 111 declares `UNITS "LB" "<unitName>" "F"`.
- Lines 151-153 write a 4000 psi concrete as pound-inch numbers, whatever unit was declared:
  - `WEIGHTPERVOLUME 0.08680555` (lb/in³)
  - `E 3604996.5` (lb/in²)
  - `FC 4000` (lb/in²)
- Lines 133-134 default the lowest storey's height to `120`, which is inches.

ETABS reads every value in the declared units. A model ETABS wrote itself (30783-01, `UNITS "KIP" "IN"`) stores the
same concrete as:

    MATERIAL  "4000Psi"    TYPE "Concrete"    WEIGHTPERVOLUME 8.680556E-05
    MATERIAL  "4000Psi"    SYMTYPE "Isotropic"  E 3604.997  U 0.2  A 5.50000004295725E-06
    MATERIAL  "4000Psi"    FC 4

So a millimetre shell tells ETABS that f'c is 4000 lb/mm², about 27,600 MPa.

Nothing else in the solution reads these numbers (searched: 0 matches outside this file).

## The fix, in `E2kShellBuilder.cs` only

1. **Add a private helper** returning inches per declared unit, matched case-insensitively:
   `IN` 1, `FT` 12, `MM` 1/25.4, `CM` 1/2.54, `M` 1/0.0254.
   Any other name throws `ArgumentException`, and the message names the unit and the five accepted.
2. **Write the material numbers in the declared unit.** With k = inches per unit, and force staying `LB`:
   - `E` and `FC` = psi value × k²
   - `WEIGHTPERVOLUME` = lb/in³ value × k³
   - `U` and `A` are unchanged.
   - `GRADE "f'c 4000 psi"` is a label and is unchanged.

   Format with `ToString("G9", CultureInfo.InvariantCulture)`. For `in` the three lines must stay byte-identical to
   today.
3. **The default lowest-storey height is 120 inches expressed in the declared unit**, in both places on lines 133-134,
   formatted with the file's existing `Trim(double)` helper (line 197).
4. **Rewrite the comment at lines 141-147** to say the mix is written in the file's declared units, citing the
   30783-01 lines above.

Nothing else in the file changes.

## Gates, in a NEW file

Create `Kor.Operations.EngineeringTools.Core.Tests/TheShellStatesItsConcreteInItsOwnUnitsTests.cs`.

- **Header:** `using System.Globalization; using Kor.Operations.EngineeringTools.Dxf; using Xunit;`
  and `namespace Kor.Operations.EngineeringTools.Core.Tests;`
- **API you need, quoted so you do not open the files:**
  - `E2kShellBuilder.ParseLevels(IEnumerable<string> lines)` takes lines such as `"L1, 0"`, `"L2, 144"` and returns
    `IReadOnlyList<BuildingLevel>`.
  - `E2kShellBuilder.FromLevels(IReadOnlyList<BuildingLevel> levels, string unitName = "in")` returns `E2kDocument`.
  - `doc.LinesOf("MATERIAL PROPERTIES")` returns that section's lines.
  - `doc.ReadStories()` returns storeys with `.Name` and `.Elevation`.
- **The gates:**
  1. `TheConcreteIsStatedInTheDeclaredUnit`: a `[Theory]` over `in`, `ft`, `mm`, `cm` and `m`. Read the FC, E and
     WEIGHTPERVOLUME numbers back from the lines, convert them to psi and lb/in³, and assert 4000, 3604996.5 and
     0.08680555 within 1e-6 relative.
  2. `AnInchShellIsUnchanged`: the three MATERIAL lines equal these exactly:
     - `  MATERIAL  "KOR-DEFAULT-CONCRETE"  TYPE "Concrete"  GRADE "f'c 4000 psi"  WEIGHTPERVOLUME 0.08680555`
     - `  MATERIAL  "KOR-DEFAULT-CONCRETE"  SYMTYPE "Isotropic"  E 3604996.5  U 0.2  A 5.5E-06`
     - `  MATERIAL  "KOR-DEFAULT-CONCRETE"  FC 4000`
  3. `AMillimetreShellStatesSixPointTwo`: for `mm`, FC parses to 4000 / 645.16 within 1e-4.
  4. `AnUnknownUnitIsRefused`: `"yd"` throws `ArgumentException`, and the message contains `yd`.
  5. `TheDefaultBaseIsTenFeetInAnyUnit`: one level `"L1, 0"` in `mm` puts the storey `Base` at elevation −3048 within
     0.01.

## Do not touch

Any other source or test file. The six models in `Baselines/` will change in their MATERIAL lines. **Do not re-bank
them**: Claude re-banks them with a diff proving nothing else moved.
