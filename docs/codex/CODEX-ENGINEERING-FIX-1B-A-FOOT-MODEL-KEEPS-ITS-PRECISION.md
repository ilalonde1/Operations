# Engineering fix 1b: a feet model writes its numbers precisely enough

**Budget.** This brief is about 3 KB, and you read two line ranges, about 8 KB.

**Rules for this run:**
- Open nothing else. No repo search, no git.
- Do **not** run `dotnet build`, `restore` or `test`; Claude verifies.
- Reasoning: **low**.

**Response:** `docs/codex/CODEX-ENGINEERING-FIX-1B-A-FOOT-MODEL-KEEPS-ITS-PRECISION-RESPONSE.md`, under 25 lines.

## Measured 2026-10-19, after fix 1

**The defect.** The extended unit differential (`AModelIsTheSameInInchesAndMillimetresTests`) fails on its new feet
arm:

    Expected: [... "WALLTHICKNESS 8"]      Actual: [... "WALLTHICKNESS 8.004"]

`E2kGeometryComposer` formats every length it writes with two fixed formatters (`E2kGeometryComposer.cs:2074-2075`):

    private static string Trim(double value) => value.ToString("0.###", Inv);
    private static string F(double value) => value.ToString("0.####", Inv);

Three decimals of a **foot** is 0.012 in, so an 8 in wall (0.666667 ft) is written as 0.667 ft, which is 8.004 in.
Inch and millimetre models are fine at these decimals.

**Stale register.** `EveryReaderConstantIsTriagedTests` fails with: "triaged constants no longer in the code (remove
their lines): E2kDocument.cs:maxPlausibleStoreyHeight, E2kDocument.cs:duplicateFloorTolerance". Fix 1 turned both into
unit-scaled locals.

## The fix

1. **`E2kGeometryComposer.cs`.** The decimals follow the model unit. Let
   `extra = options.ModelUnitInInches > 1 ? (int)Math.Ceiling(Math.Log10(options.ModelUnitInInches)) : 0`.
   For FT and M that is 2; for IN, MM and CM it is 0.
   - `Trim` writes `3 + extra` decimals, and `F` writes `4 + extra`.
   - Pass `extra` to both, or build the two format strings once in `Compose` (line 419; `options` is in hand at 430)
     and pass them down.
   - Do not use a static field: shared static state has broken this suite before.
   - Every use of the two formatters must take the unit-aware form. Counted: `Trim(` on 10 lines and `F(` on
     3 lines, each including its definition. One further line calls `.Trim()` on a **string**; that is not the
     formatter, so leave it. In IN and MM models the output must stay byte-identical.
2. **`Kor.Operations.EngineeringTools.Core.Tests/Intake/EveryReaderConstantIsTriagedTests.cs`.** Delete exactly the
   two dictionary lines keyed `"E2kDocument.cs:maxPlausibleStoreyHeight"` (line ~130) and
   `"E2kDocument.cs:duplicateFloorTolerance"` (line ~223).

## Read

- `Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs`, only the lines that contain `Trim(` or `F(`
  (use your editor's find), plus 415-435 and 2070-2080
- `Kor.Operations.EngineeringTools.Core.Tests/Intake/EveryReaderConstantIsTriagedTests.cs`, lines 125-135 and 218-228

## Do not touch

Any other file, including the test that fails. It is correct, and it is what proves this fix.
