# Step 181 — the PDF reader reads a column's turn (PARKED, not in the code)

## Written 2026-10-25. Measured, not committed: it fixes the columns it was written for and moves things it was not.

**Knob (when it lands):** `KOR_STEP181_OFF=1`. The parked code is the 2026-10-25 version of `GeometryFilterService.cs`
(`TurnedClipBox`, `Bearing`, the two column sites) kept with the session's work; this note is what it must answer first.

**Target set:** 2 witness sets, 31065-01 and 31130-01 (columns on a skewed grid).

## What it does

A column the page draws turned is read at its own size and turn: a filled column whose oriented box is plainly smaller
than its box on the page's axes, or a fill square to the page painted through a turned clip (31065: the clip IS the
column - S2.10.1's 350 x 875 at 81 degrees, a 30-point stair-stepped outline; looked at). A declared size matched by the
page box gives way to the oriented box when that is a declared size too.

## Measured on the six banked sets (each step off alone, against all on)

- 31130: its 5 skewed columns onto the engineer's bearing (270 of 270, from 265). Good.
- 31065: 15 more on the engineer's bearing; the typical sheet alone writes them at 81 (the engineer's 82); but on the whole
  build 20 still go out at ANG 0 where the engineer's are at 82 - another sheet on the same storeys draws them without the
  turned clip, and wins. Not understood yet.
- 31168: 8 columns at B-L40 gone (4 corner pairs; B-L40 is not in the engineer's model, which is itself mostly KOR-generated
  output - see the reference-model note). Not understood yet.
- 31170-arch: 6 columns become walls. Not understood yet.
- 31130 and 31138: walls lost 3/2, gained 4/4; one column each. Not understood yet.

Each of these is to be found on the drawing and looked at before this lands.
