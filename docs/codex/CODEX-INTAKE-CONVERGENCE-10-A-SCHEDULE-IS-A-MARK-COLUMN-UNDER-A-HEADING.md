# Codex 10 of N — a schedule is a mark column under a heading, not a layout

## Goal

Four readers pull structured data out of a drawing's schedules. Each one encodes how ONE drawing
set happened to be drawn, in a compiled regex, with no rule behind it and no question ever asked.
Measured across five KOR jobs, that is not an edge case — it is the normal outcome.

Make them one reader whose conventions are **settings**, the way the DXF classifier's already are.

## The evidence

**A deterministic takeoff returned zero on four of five jobs, silently.** `takeoff footings`, before
2026-09-02:

```
31065   1,174 cy      2500 x 2500 x 900 DEEP        metric
31138       0 cy      12' - 0" x 12' - 0" x 60"     imperial
31130       0 cy      4' - 0" x 4' - 0" x 26"       imperial
31168       0 cy      schedule found, no size rows
31202       0 cy      no schedule found in 59 pages
```

The cause was one pattern in `FootingScheduleReader`: `^(\d{3,4})\s*[xX×]\s*(\d{3,4})...(?:DEEP|DP)`.
Three-to-four-digit integers — millimetres. The single job it read is the only metric set. `0 cy` is
indistinguishable from "this job has no footings", and a deterministic tool reporting a total does
not look broken, so nobody looked.

**`PrintedLength` fixed that half**: 31138 → 353 cy, 31130 → 258 cy, 31065 unchanged at 1,174 cy.
`ColumnScheduleReader` was then written and reads 4 of 4 jobs. Both live in Core now.

**But the same class kept appearing, three more times, at three different levels:**

1. *Units.* Metric-only size pattern. Above.
2. *Cell order.* 31130 prints `MARK | STRENGTH | SIZE`, 31168 prints `MARK | SIZE | STRENGTH`.
   Reading a row left to right takes 31130's `45 MPa 12" x 24"` as a 45 x 610 column, rejects it as
   implausible, and loses the row.
3. *Table shape.* `ScheduleGridReader` expects a schedule whose vertical axis is a level ladder. On
   31130 page 12 it returns **1 level row, 0 thickness cells, 0 wall bands** — because that sheet's
   shear-wall schedule is `SWA | 12" | 35 MPa | 15M @ 14" EACH FACE`, one row per mark, no level
   axis. That is the same shape `ColumnScheduleReader` already reads.

**And the scoping is a convention too.** Attributing rows to a heading by distance-in-points fitted
31130's 56–183pt row offsets and returned nothing for 31168's 287–512pt on a larger sheet. Nearest
heading is also wrong: 31168's column rows sit **nearer the FOUNDATION heading than their own**.

**The asymmetry that explains all of it.** `DxfToEtabsService.ApplyRules` reads **62 distinct `dxf.*`
keys** from KorStandards — including `dxf.wall-layer-patterns`, `dxf.column-layer-patterns`,
`dxf.slab-layer-patterns`, every tolerance and every limit. A missing rule stops a production run by
design.

`FootingScheduleReader`, `ScheduleGridReader` and `ScheduleConcreteReader` reference a rule key, a
Ruling or a FormatConvention **exactly zero times**. The mechanism exists; this half of the codebase
was never wired into it.

## What to change

**One mark-row schedule reader.** Footing, column and shear-wall schedules are the same object: a
column of marks under a heading, with cells identified by WHAT THEY ARE — a size is an `a x b` whose
both sides carry a unit mark, a strength is a number before `MPa`, a bar callout is `n-nnM`. The
per-schedule differences become a vocabulary, not a reader.

`ColumnScheduleReader` is the working prototype of this: it reads four practices' layouts without
knowing any of their column orders, and `PrintedLength` reads their units without knowing which.

**Give it rule keys, following the `dxf.*` convention exactly**: which marks a schedule declares,
which heading words identify it, the plausible dimension range, and the horizontal band a table
occupies. Same `settings.ListOr` / `ValueOr` shape as `ApplyRules`.

**Say so when a schedule is found and not read.** "Schedule text present but no parseable rows"
already exists and is honest; the total that follows it is not. A page that has a schedule and reads
no rows from it should not contribute a silent zero to a number an engineer will quote.

## What NOT to do

- **Do not change the extraction layer.** It is not the weak link and this was measured:
  `fitz.get_drawings()` returns 3,696 paths on 31130 page 12 against PdfPig's 3,683 — within ~1% on
  3 of 4 pages sampled. Text extraction agrees with the rendered pixels character for character,
  including the drawing's own typos. Do not add PyMuPDF, pdfplumber or pdfium to this path.
- **Do not add a lattice/table-cell finder.** The ruled lines are there (108 horizontal, 80 vertical
  in 31130 page 12's schedule band) but cell-finding is not what fails — every failure above found
  the text and then misread it.
- **Do not make any of this a vision call.** `sched-read column` already asks an AI to look at a PNG
  of text this project reads exactly, for free, and the same way twice. The new reader should
  replace that path, not join it.
- **Do not touch `PlanClassificationOptions` or the DXF classifier's rules.** They are the model to
  copy, not the thing to change.
- **Do not delete or rewrite the existing readers in place.** Their per-schedule knowledge is the
  requirement; it needs lifting into the vocabulary, not discarding.

## What I will check

- `takeoff footings` still reads 31065 at 1,174 cy, 31138 at 353 cy, 31130 at 258 cy — the metric
  job unchanged is the regression that matters most.
- The shear-wall schedule on 31130 page 12 reads `SWA/SWB/SWC/SWD` with thicknesses 12"/12"/12"/16"
  and strengths 35/45/45/55 MPa, which `ScheduleGridReader` currently returns nothing for.
- Column schedules still read on all four: 31130 (7 marks), 31168 (6), 31138 (10), 31065 (7), with
  31130's `PC1 12" x 24" 45 MPa` and 31168's `TC02 16" x 40" 45 MPa` exact.
- A row is still attributed to the table it is under, not the nearest heading — 31168 is the case
  that separates those, and losing part of a table is worse than losing all of it, because the total
  still looks like an answer.
- Every convention the new reader relies on is reachable as a setting, and the reader names which
  one it used.
