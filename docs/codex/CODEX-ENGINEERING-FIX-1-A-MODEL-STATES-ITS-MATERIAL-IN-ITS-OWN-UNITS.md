# Engineering fix 1 of 7: a model states its material in its own units

Bounded brief for Codex. Source edits and gates only. **Do NOT run `dotnet build` or `dotnet test`**: your test runner
hangs here, and Claude verifies on the dev box after you finish. Apply the edit, read your own diff back, write the
response, then stop. Do not touch git beyond your own edits (no stash, reset, clean or commit).

The response goes beside this file as `CODEX-ENGINEERING-FIX-1-A-MODEL-STATES-ITS-MATERIAL-IN-ITS-OWN-UNITS-RESPONSE.md`.
Reasoning: medium. Reading set, measured: 11 KB plus three line ranges, about 25 KB in all.

## What is wrong, measured on 2026-10-19

`Kor.Operations.EngineeringTools.Core/Dxf/E2kShellBuilder.cs` writes the model shell when no reference model is
given. That is the route every PDF stick file takes.

- Line 111 declares `UNITS "LB" "<unitName>" "F"`. The PDF route passes `mm` (`Intake/PdfOnlyBuild.cs`, `LevelsUnit = "mm"`).
- Lines 151-153 then write the default concrete as **pound-inch numbers**: `WEIGHTPERVOLUME 0.08680555` (lb/in³),
  `E 3604996.5` (lb/in²) and `FC 4000` (lb/in²). The comment at line 146 says "in pounds and inches".

**ETABS reads every value in the file's declared units.** That is ground truth from a model ETABS itself wrote,
30783-01 `EQ Case 1.e2k`, which declares `UNITS "KIP" "IN"`. It stores the same 4000 psi concrete as:

    MATERIAL  "4000Psi"    TYPE "Concrete"    WEIGHTPERVOLUME 8.680556E-05
    MATERIAL  "4000Psi"    SYMTYPE "Isotropic"  E 3604.997  U 0.2  A 5.50000004295725E-06
    MATERIAL  "4000Psi"    FC 4

So in a file declared `LB` / `MM`, `FC 4000` means 4000 lb/mm², which is about 27,600 MPa. All six banked PDF-route
models (`Kor.Operations.EngineeringTools.Core.Tests/Baselines/pdf-only-*.e2k`) declare `"LB" "MM"` and carry
`FC 4000`.

There is a second literal of the same kind. Lines 133-134 default the lowest storey's height to `120`, which is
inches (10 ft). In a millimetre shell it is 120 mm.

The composer is already unit-aware: `E2kGeometryComposer` snaps with an `inch` argument (`SnapHalfInch`, `SnapInch`).
The shell is the outlier.

## The fix, in `E2kShellBuilder.cs` only

1. **One conversion, keyed on the ETABS length name.** Add a private helper returning inches per declared unit:
   `IN` = 1, `FT` = 12, `MM` = 1/25.4, `CM` = 1/2.54, `M` = 1/0.0254. Match case-insensitively. Any other name
   throws `ArgumentException` naming the unit and the five it accepts. Never fall back silently.
2. **Write the four material numbers in the declared unit.** With k = inches per unit and the force unit kept as `LB`:
   - stress (E, FC): lb/in² × k²
   - weight per volume: lb/in³ × k³
   - `U` (Poisson) and `A` (thermal, per °F) are unchanged.
   - `GRADE "f'c 4000 psi"` stays as written: it is a label, not a value.

   Format with `CultureInfo.InvariantCulture` and up to 9 significant figures. **For `IN` the three lines must come
   out byte-identical to today.**
3. **The default lowest-storey height is 120 inches expressed in the declared unit** (both places on lines 133-134),
   with the same formatting.
4. **Rewrite the comment at lines 141-147** to say the numbers are a 4000 psi mix written in the file's own declared
   units. Cite the 30783-01 lines above as the evidence.

Change nothing else in this file, and nothing in any other source file.

## Gates (authored, not run), in `Kor.Operations.EngineeringTools.Core.Tests/DxfToEtabsTests.cs` beside the existing `FromLevels` test (around line 1865)

- `TheShellStatesItsConcreteInTheUnitsItDeclares` (Theory over `in`, `ft`, `mm`, `cm`, `m`). Build a two-level shell.
  Read the `FC`, `E` and `WEIGHTPERVOLUME` values back out of its text. Convert them to psi and lb/in³ with the
  inverse factors, and assert 4000, 3604996.5 and 0.08680555 within 1e-6 relative.
- `AnInchShellIsUnchanged`: `FromLevels(levels, "in")` still writes exactly `FC 4000`, `E 3604996.5` and
  `WEIGHTPERVOLUME 0.08680555`.
- `AMillimetreShellWritesSixPointTwo`: for `mm` the FC value parses to 6.20001 within 1e-4 (4000 / 645.16).
- `AnUnknownUnitIsRefusedNotGuessed`: `FromLevels(levels, "yd")` throws `ArgumentException`, and its message names `yd`.
- `TheDefaultBaseHeightIsTenFeetInAnyUnit`: a one-level shell in `mm` puts the Base storey 3048 mm below the level.

## Never read

`Baselines/`, any `.e2k`, `.dxf` or `.csv`, and `docs/etabs-handoff/`. The six banked models **will** change (their
three MATERIAL lines). **Do not re-bank them.** Claude rebuilds them and proves, by diff, that nothing but those
lines moved.

## Read

- `Kor.Operations.EngineeringTools.Core/Dxf/E2kShellBuilder.cs` (whole file, 11 KB)
- `Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs` lines 640-670 only (the one production caller)
- `Kor.Operations.EngineeringTools.Core.Tests/DxfToEtabsTests.cs` lines 1840-1890 only
- `Kor.Operations.EngineeringTools.Core.Tests/StickFileSlabThicknessReaderTests.cs` lines 160-175 only

## Response

List every file changed and the gates added, and quote the three MATERIAL lines your code writes for `mm`. Under 60 lines.
