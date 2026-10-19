# Engineering fix A3d: a DXF opens on its drawing, not on an empty origin

**Budget.** This brief is about 3 KB, and you read one range of one file.

**Rules for this run:**
- Open nothing else. No repo search, no git.
- Do **not** build or test; Claude verifies.
- Reasoning: **low**.

**Response:** `docs/codex/CODEX-ENGINEERING-FIX-A3D-A-DXF-OPENS-ON-ITS-DRAWING-RESPONSE.md`, under 15 lines.

## Measured 2026-10-19

Omar's A105 markup DXF opened in AutoCAD to an empty screen. The drawing appeared only after zooming far out, in the
right-hand corner.

The geometry keeps the page's own frame, about 52-65 m from the origin. The header states the extents correctly:
`$EXTMIN 51837.8643 9060.6033`, `$EXTMAX 64643.0373 44998.8567`. But the file has **no VPORT table**: 0 occurrences.
`DxfExporter.ExportLines` writes only the LTYPE and LAYER tables (`DxfExporter.cs:320-323`, `:477`).

AutoCAD opens on the `*ACTIVE` viewport, not on the extents. With none, it shows its default view at the origin.

**Do not move the geometry.** The page frame is deliberate: `$INSBASE` carries the page origin so a point can be
carried back to the sheet, and other exports of the same page line up with it.

## The fix, in `Kor.Operations.EngineeringTools.Core/PdfToSafe/DxfExporter.cs` only

In the TABLES section, write a VPORT table **before** the LTYPE table (R12 order), with one entry, `*ACTIVE`, centred
on the extents already computed (`bMinX`, `bMinY`, `bMaxX`, `bMaxY`):

    0 TABLE  2 VPORT  70 1
    0 VPORT  2 *ACTIVE  70 0
    10 0.0  20 0.0          (viewport lower-left)
    11 1.0  21 1.0          (viewport upper-right)
    12 <centre x>  22 <centre y>
    13 0.0  23 0.0   14 1.0  24 1.0   15 0.0  25 0.0
    16 0.0  26 0.0  36 1.0  (view direction)
    17 0.0  27 0.0  37 0.0  (target)
    40 <view height>        = 1.1 * max(extents height, extents width / 1.6)
    41 1.6                  (aspect ratio)
    42 50.0  43 0.0  44 0.0  50 0.0  51 0.0
    71 0  72 100  73 1  74 3  75 0  76 0  77 0  78 0
    0 ENDTAB

- Write the numbers with the file's existing `Num`, or `F4` invariant.
- Comment it: the A105 symptom, and that AutoCAD opens on `*ACTIVE`, not on `$EXTMIN`/`$EXTMAX`.

## Gate (authored, not run)

In `Kor.Operations.EngineeringTools.Core.Tests/`, new file `ADxfOpensOnItsDrawingTests.cs`:
- Export a geometry whose one slab spans x 50,000-60,000 and y 10,000-40,000 mm.
- The output holds exactly one VPORT table entry named `*ACTIVE`.
- Its 12/22 centre is (55,000, 25,000) within 0.01.
- Its 40 height is at least 30,000.
- The VPORT table comes before the LTYPE table.

## Read

`Kor.Operations.EngineeringTools.Core/PdfToSafe/DxfExporter.cs` lines 280-330.

## Do not touch

Any other file. Claude verifies that the DXF readers (`DxfRoundTripGate`, the six-set gate) still read every
exported view the same.
