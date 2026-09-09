# Codex 31 of N — audit: steps 20 to 22, the PDF alone

## Goal

Read, do not build, do not run. Intake steps 20, 21 and 22 landed on 2026-09-09 in five commits,
`ee635198` (step 20), `fb8971ab` (21), `f6b6d68f` (pdf-levels), `70ab2d6a` (22) and `56a86980`
(a probe fix), and are described in `docs/PdfIntake.md` §28–§31. They were implemented by the
verifier in one sitting, measured against Revit's DXF of 31168 and two engineer models, and read by
nobody who did not write them. Their claim is large: a model built from a stick-file PDF with no
Revit and no reference model, with the parkade plates within 1% of Revit. Find where the code
contradicts its own stated rule, where a rule is a fact about one job wearing a universal
sentence, where a test's name is wider than its assertions, and where a doc claim is not something
the code does. Report; change nothing.

Repo-only. Files in scope (the diff `ee635198^..56a86980` is the audit's boundary):

    Kor.Operations.EngineeringTools.Core/PdfToSafe/GeometryFilterService.cs   the face-line wall rule (CutPen, FilledWallFaceLines, WallsFromFaceLines), the Band fate, the match-line fate
    Kor.Operations.EngineeringTools.Core/PdfToSafe/SheetFurniture.cs          MatchLines, IsOnMatchLine
    Kor.Operations.EngineeringTools.Core/PdfToSafe/DxfExporter.cs             face lines not written as beams; the KOR_MATCHLINE layer
    Kor.Operations.EngineeringTools.Core/PdfToSafe/PdfGeometryModels.cs       WallFaceLines, FirstFaceWall, LineWidths, MatchLines, PlanMatchLine
    Kor.Operations.EngineeringTools.Core/Intake/PathFate.cs                   BecameWallFace, Band, MatchLine
    Kor.Operations.EngineeringTools.Core/SheetInventory.cs                    the face-wall row
    Kor.Operations.EngineeringTools.Core/Dxf/DxfFloodFillPlateDetector.cs     EnclosedByWallPanels
    Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs      the panel-plate fallback, MinFloorCoverage, the coverage gate
    Kor.Operations.EngineeringTools.Core/Dxf/MatchLineSheetJoin.cs            SameAs as a line
    Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs             the seeded reference grid, seams on the grid, partner linework into the leader's frame, joinable, the unjoined-seam warning
    Kor.Operations.EngineeringTools.Core/Kor.Operations.EngineeringTools.Core.csproj   InternalsVisibleTo "takeoff"
    Kor.Operations.EngineeringTools.TakeoffCli/Program.cs                     pdf-levels; pdf-overlay's --walls trace, pens, regions, match lines
    Kor.Operations.EngineeringTools.Core.Tests/Intake/TwoFaceLinesAWallsThicknessApartAreAWallTests.cs
    Kor.Operations.EngineeringTools.Core.Tests/Intake/AFilledBandThickerThanAnyWallIsNotASlabTests.cs
    Kor.Operations.EngineeringTools.Core.Tests/Intake/APlanTooWideForOneSheetIsSplitOnAMatchLineTests.cs
    Kor.Operations.EngineeringTools.Core.Tests/Dxf/AStoreysPlateIsWhatItsWallsEncloseTests.cs
    Kor.Operations.EngineeringTools.Core.Tests/Intake/EveryPathHasExactlyOneFateTests.cs
    Kor.Operations.EngineeringTools.Core.Tests/Intake/TheWallRuleChangesNothingElseTests.cs
    docs/PdfIntake.md §28–§31
    docs/etabs-handoff/pdf_only_all.sh, e2k_read.py, storey_walls.py, draw_storey.py (probes; read for what they claim to measure)

No UNC paths, no databases, no other repos, no stick files, no DXFs: the ledgers, censuses,
overlays and the five-set builds are the verifier's; you read code and prose. The engineer's
rulings the steps cite are quoted in the doc sections; take the quotes as given.

## Questions, in order

1. **A wall is what the cut pen encloses** (`WallsFromFaceLines`). The rule pairs cut-pen lines,
   longest overlap first, one wall per face line, refusing a face already another wall's on its
   other side (`faceSide`), a third cut-pen line between or at the same spacing beyond, more than
   one lighter line between (by distinct offset), a run of three risers a tread apart, an X corner
   to corner, a pair under a wall or column, and a pair of less than wall aspect. From the code
   alone: (a) name a drawn wall this refuses — a wall whose one face is broken by a pilaster, a
   wall on a grid line whose face is the grid line, a wall drawn at the cut pen on one face and a
   lighter pen on the other; (b) name a non-wall it accepts — the doc names a 4" curb; find
   another; (c) `CutPen` takes the commonest width along filled walls' faces — on a sheet with no
   filled wall the rule reads nothing; on a sheet whose filled walls' faces are drawn in two pens,
   which wins, and is that what the sheet's cut walls use?
2. **Order dependence.** The pairing walks candidates sorted by overlap then gap, and `Covered`
   consults walls made earlier in the same pass. Construct two pairs of lines where the order of
   acceptance changes the result, and say whether `TwoFaceLinesAWallsThicknessApartAreAWallTests`
   would notice.
3. **A band is not a slab** (`PathReason.Band`). Thicker than the thickest wall, up to twice it,
   aspect ten and more, not paper. Name a real floor this refuses (a corridor slab, a walkway, a
   balcony run) and what the ledger then says about it. Is `Band` reachable for an annotation
   path, and should it be?
4. **The plate from wall panels** (`EnclosedByWallPanels`). The panels are painted, dilated by
   the doorway radius, the outside flooded and grown back. (a) A ring with a gap of exactly one
   doorway: closed or open? Both ends of that question have a case in the tests; is the boundary
   itself tested? (b) The plate's boundary is traced on the dilated-then-eroded mask: is it the
   outer face, or the outer face plus up to one pixel, and does the 0.4% and 0.7% agreement with
   Revit in §31 include that bias or hide it? (c) The coverage gate counts columns whose
   centre lies in the plate; a parkade whose columns stand ON the perimeter wall line — is the
   gate stable there? (d) `painted < 2 * wallArea → null` as the leak test: construct a ring that
   leaks and still passes it.
5. **The match line** (`SheetFurniture.MatchLines`). The label's aspect decides the orientation;
   the nearest spanning line within six text heights is the seam. (a) A label that is a
   single rotated word "MATCHLINE" 6 pt wide: `Math.Max(w.Width, w.Height)` is used for the LINE
   search reach but `w.Height > w.Width` for orientation; a label of one square word — which
   way? (b) A sheet with two match lines and two labels: does `found` dedupe the right way, and
   does `IsOnMatchLine` then fate the pieces of both? (c) A dash-dot line whose pieces sit at two
   y values a point apart (a thick pen drawn as two strokes): does the bucket by `(int)Math.Round`
   split it below the 40% span and lose it? Say what the report shows a user in that case.
6. **Seams as lines** (`MatchLineSeam.SameAs`). The new branch accepts two collinear, overlapping
   seams. On a Revit export where three sheets carry the same match line (a plan cut into thirds),
   or where two unrelated sheets on one storey both carry a match line on the same grid line to
   different neighbours, what does `Group` now do that it did not before `70ab2d6a`? Is that
   guarded by `DominantSide`?
7. **Joinable is every sheet** (`joinable = files.ToList()` when no `match-line-join.<job>.*` rows
   exist). The banked ruling in migration 059 says LEVEL 1 of 31168 "is NOT joined" because she
   accepted it as it was. On a run WITH the rules database and WITH the 31168 rows, does
   `joinJob` (the output file's name) still match the rows when the output is named
   `out-pdf-only.e2k` or `31168-C.e2k`? State what a mismatch silently does now, and what it did
   before the change.
8. **The seeded grid.** With no reference, the sheet with the most named axes becomes the
   reference in its own frame. Name a set where that sheet is the wrong one (a key plan naming
   every axis of every building; a site plan) and say what the by-name fits of the others then
   agree on. Is the `Note` on that sheet's `Fit` distinguishable in the report from a real fit?
9. **Partner linework into the leader's frame** (`Unapply`). Check the arithmetic against
   `AnnotationOverlay.Frame.Apply` for a non-zero rotation: `Apply` uses exact trig at quarter
   turns and `Trig(degrees)` elsewhere; `Unapply` uses `Math.Cos`/`Math.Sin` always. Does a
   90° frame round-trip exactly, and does anything downstream decide on exact endpoint identity
   (closure of the partner's wall outlines) that this would break?
10. **pdf-levels.** Levels chained from the lowest stated level at 0; a level named twice keeps
    the first. Name a stick file where "first stated" is the wrong one, and what the model then
    carries. Does the levels file record the unit, and what does `dxf-to-etabs` assume if
    `--levels-unit` is omitted?
11. **The trace hooks.** `GeometryFilterService.FaceTrace` is a static `Action<string>?` set by
    the CLI around one call. Is any production path able to observe it set? Is
    `InternalsVisibleTo("takeoff")` wider than the trace needs?
12. **Names wider than checks.** For each of the four new test classes and the two changed ones,
    one line: does the class name promise more than its assertions cover, and does each carry
    WHAT IT COVERS / DOES NOT as rule 11 of `CLAUDE.md` asks? For `AStoreysPlateIsWhatItsWallsEncloseTests`
    say specifically whether any test exercises the classifier's gate (foundation sheet, slab
    edges present, coverage) or only the detector.
13. **Doc against code.** In `docs/PdfIntake.md` §28–§31, every sentence of the form "X is Y"
    about the code: mark the ones you could not find in the code. Ignore the numbers; the verifier
    owns those. Flag any sentence that says "universal" or "any job" about something the code does
    for these five sets only.

## Output

One file, `docs/codex/CODEX-31-AUDIT-RESPONSE.md`, findings first, each with file:line, the
sentence it contradicts, and the smallest input that shows it. No fixes, no build, no test run.
