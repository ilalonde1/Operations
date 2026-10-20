# Step 153 — a plan the engineer has marked up is read for his markup

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the corpus gate below has judged it.

**Knob:** `KOR_STEP153_OFF=1` reads every plan page whole, as before (`PdfOnlyBuild.WriteSheets`).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): 1 of its 8 plan pages carries structure ink (A105,
19 marks; the threshold is `MinStructureInk` = 4). Corpus reach unmeasured — 0 of 297 counted, 0 of the 48.

## The witness

With step 152 alone, A105's typical floor (L35) read the engineer's core AND the architect's drawing: 27 walls, of
them the architect's unit partitions; 17 openings, of them her room fills (1,051 / 438 / 329 sq ft ... cut from a
4,852 sq ft floor). His markup states the structure; her linework is what he drew it over.

A page carrying structure ink in a declared colour (step 152) at or above `MinStructureInk` is re-read
markup-only — the mode the markup DXF already uses (`IntakeRequest.MarkupOnly`).

| L35 | step 152 only | + step 153 |
|---|---:|---:|
| walls | 27 | 11 — his core and cross-walls, every one pier-labelled |
| columns | 11 | 10 — on his 10 squares |
| openings | 17 | 0 |

Rendered on his sheet: `scratchpad/omar-153/l35-final.png`.

## Known, open

1. Two legs of the core's left wall (beside stair B, beside stair A) are not split out of the core region.
2. Shaft openings: his markup does not draw them and markup-only drops the architect's, so the shafts are floored.

## What must NOT move

- The six banked sets carry no markup: byte-identical.
- A page with a few marks (fewer than four) is read whole, as before.

## The measurement that decides it

    takeoff corpus-gate --bisect KOR_STEP153_OFF

⛔ Run where the drawings are local. Never over the VPN.
