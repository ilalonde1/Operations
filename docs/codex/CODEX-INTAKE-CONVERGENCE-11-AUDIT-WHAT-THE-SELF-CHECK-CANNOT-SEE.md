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
- Column schedules unchanged: 31130 7 marks, 31168 6, 31138 10, 31065 7 — and if the mark pattern
  widens, 31168 should read MORE than 6 and still not read a footing row as a column.
- The gate reproduces 41/52 on 31130 S2.01.2, and still collapses on a wrong scale: 31065 at 1:100
  scores 19/36, 25/31, 22/33, and at 96 scores 3/36, 3/31, 3/33.
- Any constant that becomes a rule keeps its current value as the default, so every number above is
  unchanged until a job says otherwise.
- Anything claimed about a whole set is stated as X of Y, measured against the five stick files, not
  against a unit test. **The last two rounds both passed their unit tests and were wrong on real
  drawings.**
