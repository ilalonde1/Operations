# Codex 15 of N — a wall is a filled rectangle of wall proportions, and it is read as one

## Goal

Give the PDF side a wall reader. Today the intake emits no walls at all: against 31168's Revit DXF
of the same sheets, PDF walls 0, DXF walls 892 (`takeoff pdf-vs-dxf`, 24 issued sheets). The walls
are on the page, drawn the way KOR draws every cut concrete element — as a filled rectangle of
poché — and the classifier, knowing only slab, column and line, files the long ones as floor
plates and discards the short ones for being too slender to be a column.

After this step: `ExtractedGeometry.Walls` holds each wall as its outline, axis and thickness; the
DXF carries them on the WALL layer the DXF-to-ETABS classifier already reads; the ledger says
`BecameWall`; the differential reports PDF walls against Revit walls sheet by sheet.

## Measured premise (2026-09-08, five stick files)

Every filled, non-paper, closed path on a plan whose narrow side is 4"–36" and whose long side is
at least twice that is a **four-vertex rectangle** — 71 of 71 on 31168 p14, 24 of 24 on 31130 p11,
51 of 51 on 31138 p9, 52 of 52 on 31065 p14, 66 of 66 on 31202 p17. Walls are not drawn as pairs of
face lines on these sets: pairing parallel strokes at wall spacing finds 393–563 pairs per page,
which is dimension and grid work.

Applying the DXF side's own wall rules to those rectangles — thickness in [4", 36"], length ≥ 48",
aspect ≥ 2 (`PlanClassificationOptions.MinWallThickness / MaxWallThickness / MinWallLength /
MinWallAspect`, banked in KorStandards as `dxf.min-wall-thickness`, `dxf.max-wall-thickness`,
`dxf.min-wall-length`, `dxf.min-wall-aspect`) — and excluding the title-block and schedule strips:

    sheet          PDF candidates   Revit walls (sum of views)
    31168 p10 S2.01       20             23
    31168 p12 S2.03.1     21             24
    31168 p14 S2.05.1     22             26
    31168 p16 S2.10.1     14             18
    31168 p18 S2.12.1     15             15

Five of five within four; one exact. The shortfalls are the next measurement, not a reason to
widen the rule: an L-shaped core drawn as one ribbon that Revit splits into panels, or a wall
under a notes box, will show in the overlay once the walls are drawn on it.

Where those rectangles go today (31130 p11, 11 candidates): the long ones pass the slab test
(`BecameSlab`) — they are part of the 438 slabs still in excess of Revit's 45 after brief 14 —
and the short ones fail the column aspect (`ColumnAspect`) or the slab minimum (`TooShort`).

## The class, in one sentence

The classifier has three names for a closed filled shape — column, slab, or nothing — and a cut
wall is none of them, so the drawing's most common structural element is filed under whichever
of the three its size happens to fall nearest.

## Change this

### 1. `PdfIntakeOptions` — the wall numbers, from the SHARED rows

Add `MinWallThicknessMm`, `MaxWallThicknessMm`, `MinWallLengthMm`, `MinWallAspect`. Defaults
101.6, 914.4, 1219.2, 2.0 (the DXF side's 4", 36", 48", 2.0 in millimetres). `ApplyRules` reads
them from the DXF side's own keys, converting inches to millimetres, the way `SharedMaxColumnAspect`
already shares `dxf.max-column-aspect`: a wall's proportions do not depend on whether the drawing
arrived as PDF or CAD. Add the four keys to the shared-keys list beside it. No new KorStandards row.

### 2. `ExtractedGeometry` — walls exist

```csharp
public sealed record WallPanel(IReadOnlyList<(double X, double Y)> Outline,
                               (double X, double Y) Start, (double X, double Y) End, double ThicknessMm);
public List<WallPanel> Walls { get; } = new();
public List<(byte R, byte G, byte B)> WallColors { get; } = new();
public List<bool> WallIsAnnotation { get; } = new();
```

Parallel lists, as the slab and column lists are kept. Outline in mm, axis endpoints and thickness
in mm, from `LoopGeometry.MinAreaBox` (Core.Dxf; convert to `DxfPoint` and back).

### 3. `GeometryFilterService.Classify` — one rule, placed exactly

Immediately AFTER the declared-column-size rule (`GeometryFilterService.cs:143-150` at HEAD) and
BEFORE the `looksLikeColumn` / slab branch, for a closed, filled, non-annotation content path:

```csharp
// A WALL IS A FILLED RECTANGLE OF WALL PROPORTIONS. KOR draws every cut concrete element as
// poché; a column is the one whose size the sheet declares (the rule above), a wall is the
// one at least 48" long and at least twice as long as it is thick, 4"–36" thick. Measured
// 2026-09-08 on five sets: every such shape was a four-vertex rectangle, and on 31168 the
// count per sheet landed within four of the Revit export's (docs/codex/…15…md).
var box = LoopGeometry.MinAreaBox(pts as DxfPoint list);
if (pts.Count == 4
    && box.Thickness >= minWallThicknessMm && box.Thickness <= maxWallThicknessMm
    && box.Length >= minWallLengthMm && box.Aspect >= minWallAspect)
{
    result.Walls.Add(new WallPanel(pts, box.AxisStart, box.AxisEnd, box.Thickness));
    result.WallColors.Add(color); result.WallIsAnnotation.Add(false);
    Fate(PathReason.BecameWall, result.Walls.Count - 1);
    continue;
}
```

Four new parameters on `Classify` with defaults equal to the option defaults, so every existing
caller compiles and behaves as before until it passes them; `PdfPlanReader.Read` and
`DrawingIntake` pass the options' values.

`pts.Count == 4` is deliberate: a filled shape with more vertices of wall thickness is a ribbon
(an L or U core drawn as one outline) and keeps today's fate. Count it in a new CONTEXT row so we
see how many there are — do not split it. Splitting ribbons is the DXF side's `AddWallOrColumn`
job and belongs to a later step, with its own measurement.

`PathReason.BecameWall` → `Disposition.Read`.

### 4. `DxfExporter` — walls on the wall layer

Write each `WallPanel.Outline` as a closed polyline on layer `WALL` (default) or
`KorLayerName("WALL")` under `--kor-layers` — the `"WALL"` case exists at `DxfExporter.cs:~250`
and is unused for outlines today. Keep the existing wall-hinted LINE export as it is. Colour by
`WallColors` where the exporter colours slabs by theirs.

The DXF-to-ETABS classifier reads a four-point rectangle on a wall layer through
`AddWallOrColumn` (`StructuralPlanClassifier.cs:~2100`): `simpleRectangle`, thickness in range,
length ≥ minimum → `WallAxis`. So a PDF-derived DXF now reaches the model with walls through the
same code Revit-derived DXFs use, and no wall logic is duplicated on the PDF side.

### 5. The instruments read walls

- `PdfVersusDxf.Compare`: `PdfWalls = pdfGeo.Walls.Count` (it is a literal 0 today).
- `pdf-overlay`: draw each wall outline in dark red (distinct from the red leftover lines), weight 2.
- `pdf-takeoff`: a `walls` column in the per-page line, between `columns` and `lines`.
- `SheetInventory`: nothing to do — the fate row appears by reason. Add the ribbon context row
  ("filled wall-thickness shapes with more than four vertices — ribbons, not split") from a count
  the classifier records the same way (`PathReason` is not the place; a counter on
  `ExtractedGeometry`, e.g. `WallRibbonsNotSplit`, is).

### 6. Tests, in `Core.Tests/Intake/`

- `AWallIsAFilledRectangleOfWallProportionsTests`: a 12" × 20' grey rectangle → `BecameWall` with
  the right axis and thickness; 14" × 38" → not a wall (length); 12" × 20" → not a wall (aspect);
  a 3" × 10' → not (thin); a 40" × 10' → not (thick); a declared 18" × 60" column → column first;
  a paper-white 12" × 20' → `PaperFill`; the same rectangle as an annotation → today's fate;
  a six-vertex L of 12" thickness → today's fate and the ribbon counter at 1.
- `TheWallRuleChangesNothingElseTests`: the rule-11 differential — `Classify` on a fixture with
  every path kind, with the wall rule's thresholds set so nothing qualifies, equals HEAD's output
  exactly; with them at defaults, every non-wall object is unchanged and the walls are exactly the
  rectangles that qualify.
- `FiveStickFilesTests`: I bank the per-page wall counts after your build; leave a `WallCount`
  slot in the job table at 0 with a comment that the value is banked by the verifier.

## What NOT to do

- No two-face pairing. No ribbon splitting. No beams — a beam below a slab is not poché.
- No change to any existing threshold, to the slab or column branches, or to any reader.
- No change to `StructuralPlanClassifier`, `DxfToEtabsService`, or anything under `Dxf/` except
  reading `LoopGeometry.MinAreaBox` and `OrientedBox`, which already exist.
- No WPF, no rebar, no PowerShell, no new library, no KorStandards row (the rows exist).
- Repo only; no tests run; build Core and TakeoffCli once. I verify.
- Files: `GeometryFilterService.cs`, `PdfGeometryModels.cs`, `PdfIntakeOptions.cs`,
  `PdfPlanReader.cs`, `DxfExporter.cs`, `Intake/PathFate.cs`, `Intake/DrawingIntake.cs`,
  `PdfVersusDxf.cs`, `TakeoffCli/Program.cs`, and the tests named — nothing else.

## What I will check

1. **Census against the step-2 baseline** (`%LOCALAPPDATA%\Temp\kor-drawings\harness\step2-after\`):
   COLUMN and BEAM counts identical on 13 of 13; a WALL layer appears with the candidate counts
   above (31130 p11: 11; 31168 p11–13: 20–22); SLAB down by the walls that were slabs; nothing else.
2. **The ledger**: `BecameWall` per set; `BecameSlab`, `ColumnAspect` and `TooShort` down by the
   same total; the ribbon context row reported; every other row unchanged; totals unchanged.
3. **`pdf-vs-dxf` on 31168**: PDF walls within ±2 of the prototype's per-sheet counts on the five
   sheets above, columns unchanged; then the overlay on each, to see which Revit walls the rule
   does not find and why.
4. **The convergence proof**: `pdf-takeoff … --kor-layers` on 31168 p14, then `dxf-to-etabs` on
   that one DXF against the 31168 reference: the model gains wall panels where it had none.
5. `FiveStickFilesTests` 25 of 25 with wall counts banked; fast suite green.
