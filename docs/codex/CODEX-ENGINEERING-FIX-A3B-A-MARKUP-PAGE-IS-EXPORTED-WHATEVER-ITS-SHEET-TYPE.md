# Engineering fix A3b: a markup page is exported whatever its sheet type, and the old-entry test expects the markup text

**Budget.** This brief is about 3 KB, and you read three short ranges.

**Rules for this run:**
- Open nothing else. No repo search, no git.
- Do **not** build or test; Claude verifies.
- Reasoning: **low**.

**Response:** `docs/codex/CODEX-ENGINEERING-FIX-A3B-A-MARKUP-PAGE-IS-EXPORTED-WHATEVER-ITS-SHEET-TYPE-RESPONSE.md`,
under 20 lines.

## Measured 2026-10-19 on the A3 build

1. **`TheMarkupDxfIsTheSameFromTheAppAndTheCliTests` fails in both modes** at line 43: `Assert.Equal(1, result.Written)`,
   where the actual value is 0.
   - `PdfOnlyBuild.WriteSheets` writes nothing for a page not typed `plan` (`PdfOnlyBuild.cs:169-179`). That rule
     exists for the stick-file route: a section's poché is not a storey (brief 17).
   - The Regent fixture page is not typed `plan`. The app exports any page the user picks.
   - **Ian's decision, 2026-10-19:** in markup mode, the page the user names is exported whatever its sheet type.
     Markup is the engineer's drawing, not a reading of the architect's sheet.
2. **`DrawingIntakeTests.TheNewEntryRetainsTheOldGeometryAndDxfBytes(markupOnly: true)` fails.**
   - It compares `DrawingIntake.ReadSheet` with `PdfPlanReader.Read`. Since A3, the intake also carries the markup
     text (`MarkupText.ExtractMarkupTextAnnotations`), and the old reader never did.
   - Actual: one TextAnnotation, "Check this column"; expected: none.
   - This is A3's intended change, so the expectation is what is out of date.

## The fix

1. **`PdfOnlyBuild.cs:172`:** the plan-sheet rule applies only when not reading markup. Change
   `if (record.SheetType != "plan")` to `if (record.SheetType != "plan" && !markup)`. Add one comment line above it
   citing the decision. Nothing else in the method changes. The stick-file route passes `markup: false`, so it is
   unaffected.
2. **`Kor.Operations.EngineeringTools.Core.Tests/Intake/DrawingIntakeTests.cs`, `TheNewEntryRetainsTheOldGeometryAndDxfBytes`:**
   - When `markupOnly`, add the markup text to the old reader's result before comparing:
     `expected.TextAnnotations.AddRange(MarkupText.ExtractMarkupTextAnnotations(doc.GetPage(1), 96 * PdfToSafeConstants.PointsToMm));`
   - Rename nothing. Leave every other assertion, including the DXF byte comparison, as it is.

## Read

- `Kor.Operations.EngineeringTools.Core/Intake/PdfOnlyBuild.cs` lines 160-182
- `Kor.Operations.EngineeringTools.Core.Tests/Intake/DrawingIntakeTests.cs`: the method
  `TheNewEntryRetainsTheOldGeometryAndDxfBytes` and the file's `using` lines

## Do not touch

Any other file. In particular, do not change `TheMarkupDxfIsTheSameFromTheAppAndTheCliTests`: it is correct, and it is
what proves fix 1.
