# Codex 11 of N — audit what I built, and what the self-check cannot see

## Goal

Adversarially audit one session's ingestion work: a PDF→DXF verb, a shared schedule reader, and a
gate that checks a sheet against itself. It is measured and it works on real jobs — which is exactly
when it is worth attacking, because the failures it has already surfaced are the same shape as the
ones it is still hiding.

**Assume the author was wrong. The three defects listed under "already found" were all found by
accident or by one measurement; there is no reason to think that set is complete.**

## What was built

| | |
|---|---|
| `takeoff pdf-takeoff` | reads the drawing (not just Bluebeam markup), writes DXF, `--kor-layers` |
| `PrintedLength` | one reader for a printed length: `4' - 0"`, `26"`, `3 1/2"`, `2500` |
| `MarkRowScheduleReader` | one mark-row reader, `dxf.schedule.*` keys — column, footing, flat shear wall |
| `ColumnScheduleReader` | deterministic; replaces the `sched-read column` vision call |
| `PlanAgreesWithItsSchedule` | the gate: a sheet's geometry against its own schedule |

Measured across five KOR jobs: footings 1 of 5 → 3 of 5; column schedules 4 of 4; flat shear-wall
rows where `ScheduleGridReader` returned nothing; and the gate reproduced a hand count exactly
(41 of 52 on 31130 S2.01.2).

## Already found — do not re-report these, attack what is LIKE them

**1. A hardcoded aspect ratio silently discards a declared column type.**
`GeometryFilterService`: `if (!sub.IsAnnotation && maxDim > 2.5 * minDim) continue;`
31168's COLUMN SCHEDULE - PARKADE declares `PC01 14" x 36"` — aspect **2.571**. Measured on that
job's own sheets: **54 shapes at exactly 14x36 rejected on p11, 32 on p12**. Its most common parkade
column never reaches the model. `dxf.max-column-aspect` exists as a rule key on the DXF side;
PdfToSafe carries its own constant instead.

**2. The mark pattern is narrower than KOR's own marks.**
`^[A-Z]{1,3}\d{1,2}$` cannot match `C02-A`, `C03-B`, `PC03-A`, `GC11-C` — all declared on 31168
S2.02. That sheet declares roughly fifteen marks and the reader reads six.

**3. The gate cannot see a column the reader MISSED.**
It scores only what was found. 31168 p11 scored 0/220 — and the reason was not that 220 columns are
wrong sizes, it is that the real columns were never detected at all and 220 isolation-joint squares
were. The score was right, the story it implied was not.

## Change this one first — stop guessing what a mark looks like

`^[A-Z]{1,3}\d{1,2}$` is a guess at the SHAPE of a mark, and it is load-bearing twice over: it
decides which schedule rows exist, and which words on the plan count as labels. It has now been
wrong on real KOR drawings twice — `C02-A`, `C03-B`, `PC03-A` and `GC11-C` are all declared on
31168 S2.02 and none of them matches.

**The drawing does not need to be guessed at, because the schedule states the marks.** A schedule's
first column IS the list of marks. Read that column structurally — the tokens sharing the table's
leftmost x under its heading — and take the strings literally, whatever they look like. Then match
plan labels against THAT KNOWN SET rather than pattern-matching every word on the page.

This is worth doing before anything else in this file because:

- it deletes the most brittle regex in the stack, and both known mark failures with it;
- it removes the need for the 15-point mark-column bucket, since the mark column is simply the
  column those tokens are in;
- it makes plan-label matching exact instead of approximate, which is what
  `PlanAgreesWithItsSchedule` attributes columns by, and what `FootingScheduleReader` counts
  placements by;
- and it removes the reason `Options.RequireDimensionPair` had to exist — a plan label only counts
  if the schedule already named it, so a stray `SF1` on the plan can no longer anchor a mark column
  and take a whole table down with it.

⚠ Keep `MarkPatterns` as a fallback for a sheet whose schedule cannot be located, and say in the
result which route was used. A reader that silently changes how it identified a mark is the same
class of problem as everything else in this file.

⚠ THE GENERAL LESSON, and the thing to look for elsewhere: a regex parsing a NOTATION is fine —
`PrintedLength` reading `4' - 0"` has been right on every job since it was written. A regex encoding
a CONVENTION is the recurring defect: metric-only sizes, this mark shape, a 2.5 aspect ratio, a
260-point heading scope. Every failure this session was the second kind.

## What to attack

**Every constant that is not a rule.** These decide what reaches a model and none is reachable as a
setting: the 2.5 column aspect; `columnMinDimMm` 200 and `columnMaxSizeMm` 1500 in
`GeometryFilterService.Classify`; `HeadingBandFraction` 0.18 and the 15-point mark-column bucket in
`MarkRowScheduleReader`; `DefaultToleranceMm` 25 and `DefaultLabelReachMm` 1500 in the gate;
`slabMinDiagonalMm` 1000 and `lineMinLengthMm` 200. For each: what drawing convention does it
assume, and which of the five local jobs would it be wrong for?

**The rule plumbing that is not actually plumbed.** `DxfExporter` takes a
`PlanClassificationOptions` so `--kor-layers` names layers from the job's own
`dxf.*-layer-patterns` — but `pdf-takeoff` has no `--rules-db` and never loads them, so it always
passes the compiled defaults. The defect was fixed at the API and left live at the call site. Look
for others of that shape.

**Whether the gate can be gamed by its own pipeline.** Geometry and schedule are meant to be
independent evidence. They now share `PrintedLength`, `VectorPageReader`, and a coordinate
convention. Is there an error that moves BOTH sides the same way and so raises the score while making
the model worse? A differential cannot see a fault present in both runs.

**The `Score` property.** `SizesDeclaredSomewhere / ColumnsFound` — a sheet where the reader finds
three columns and all three match scores 1.0. Is that number safe to put in front of an engineer, or
does it need a floor on `ColumnsFound` and the "matched to its own mark" figure promoted?

**Two jobs still read nothing and neither is diagnosed.** 31202 has no foundation schedule found in
59 pages; 31168 footings return 0 cy with a schedule heading present. Both were left alone.

## What NOT to do

- **Do not change the extraction layer.** Measured: `fitz.get_drawings()` gives 3,696 paths against
  PdfPig's 3,683 on the same page, and extracted text matches the rendered pixels character for
  character. Do not add PyMuPDF, pdfplumber or pdfium.
- **Do not loosen the aspect limit blindly.** It exists to keep linework out. Raising it to admit
  `PC01` may admit a great deal else; the question is whether it should be a rule, and what the
  evidence per job says — not what number is better.
- **Do not make the gate lenient to raise scores.** A low score that sends someone to look at the
  sheet is the product working.
- **Do not touch the DXF classifier's own rules.** 62 `dxf.*` keys, the model to copy.
- **Do not rewrite the readers.** They now read four practices' layouts; the risk is that a
  refactor loses a convention that was learned the hard way.

## What I will check

- Footings unchanged: 31065 1,174 cy, 31138 353 cy, 31130 258 cy.
- Column schedules: 31130 7 marks, 31168 6, 31138 10, 31065 7 today. Reading marks off the table's
  own column should INCREASE 31168 — its sheet declares roughly fifteen, including `C02-A`,
  `C03-A`, `C03-B`, `C04-A`, `C04-B`, `GC11-C`, `PC03-A`, `PC03-B` — while 31130, 31138 and 31065
  stay as they are, and no footing row is read as a column.
- With marks taken from the schedule, 31168's `PC03-A 42" x 42"` should be recognised: 21 shapes at
  42x42 are already detected on p11 and currently match nothing, because the mark was never read.
- The gate reproduces 41/52 on 31130 S2.01.2, and still collapses on a wrong scale: 31065 at 1:100
  scores 19/36, 25/31, 22/33, and at 96 scores 3/36, 3/31, 3/33.
- Any constant that becomes a rule keeps its current value as the default, so every number above is
  unchanged until a job says otherwise.
- Anything claimed about a whole set is stated as X of Y, measured against the five stick files, not
  against a unit test. **The last two rounds both passed their unit tests and were wrong on real
  drawings.**
