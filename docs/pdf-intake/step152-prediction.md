# Step 152 — the engineer's structure ink declares what a markup is

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the corpus gate below has judged it.

**Knob:** `KOR_STEP152_OFF=1` writes markup on the layer its shape gave it, as before (`DxfExporter`, `MarkupOrStructuralBaseLayer`).
**Setting:** `dxf.pdf.markup-structure-colours`, compiled default `800000=WALL, FFFF00=WALL-BELOW?, 0080C0=BEAM?, FFFF80=BEAM?`.
A trailing `?` is an ASSUMPTION: written on a `-ASSUMED` layer, so the report's layer line states the guess. No KorStandards row yet.

**The assumed colours (2026-10-19), every filled markup colour in 143 E 3rd's set accounted for:**

| Colour | Shapes | Read as | Why |
|---|---|---|---|
| #800000 | 187, on 7 pages | WALL | KOR's structure ink; walls and columns |
| #FFFF00 | 34 | WALL-BELOW? | structure arriving from the storey below and stopping here (step 165). 205 of Omar's files: 87 yellow shapes, 85 highlight boxes - nothing states it |
| #0080C0 | 3 | BEAM? | red-dashed bands with a width dimension, 8'-0" x2 on L2, 4'-0" on L36 between two columns; read as slab edges they were cut as holes (387, 267, 117 sq ft). Beams are not modelled (LayerLedger) |
| #FFFF80 | 4 | BEAM? | L36, where a yellow wall line crosses the corridor: the line over the doorway, a header; cut as two holes before |
| #004000 | 10 | (undeclared) | the floor's area measurement - read as the floor, as before |

`PdfOnlyBuild.SheetsResult.UndeclaredMarkupFills` lists every undeclared markup fill colour in report.txt ("Markup fills"),
the check for the class both bands belonged to: an undeclared filled markup falls through to a slab edge and is cut as a hole.

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel, A105, marked up by Omar): 19 of its 19 burgundy marks
reach the composer on `KOR_V-WALL-MARKUP`, where 0 did. Corpus reach unmeasured — 0 of 297 counted, 0 of the 48.

## Why markup is not classified by shape

Step 2b's wall rule (`bdc52d7f`) is measured on drawing poché, and markup is exempt from every drawing rule by design
(`EveryPathHasExactlyOneFateTests`: "Markup bypasses furniture, paper, frame, minimum-size and aspect rules"). A markup is
what its colour declares — `SlabColorSettings.ElementType` in the window, set per job. A dropped-in PDF has nobody to set
it, so the route wrote every burgundy region on SLAB and the engineer's walls became floor.

This declares KOR's structure ink, **markup only**: page linework of the same colour is the drawing's and keeps its
own layer. A105: walls and columns #800000; the floor measurement #004000; the dimensions #F00000.

## What must NOT move

- The six banked sets carry no markup: byte-identical.
- Markup in any other colour: unchanged.

## The measurement that decides it

    takeoff corpus-gate --bisect KOR_STEP152_OFF

⛔ Run where the drawings are local. Never over the VPN.
