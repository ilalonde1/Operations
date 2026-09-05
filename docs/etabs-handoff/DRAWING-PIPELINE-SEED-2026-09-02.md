# Seed — the drawing pipeline, intake to deliverable

Paste this into a fresh session. It is written for someone with no context, and it is deliberately
specific about what has been MEASURED versus what is assumed, because most of the wasted effort on
this pipeline has come from re-deriving things that were already measured, or trusting things that
were never checked.

---

## What the pipeline is

A structural drawing arrives as a PDF, CAD, or a Revit model. It has to become an analysis model
(ETABS `.e2k`, SAFE `.f2k`), a quantity takeoff, and a set of deliverables an engineer will open.

    drawing PDF ──> geometry + schedules ──> DXF ──> dxf-to-etabs ──> .e2k ──> publish gate ──> the engineer
    Revit ─────────────────────────────────> DXF ──┘

Both halves are in `Kor.Operations.EngineeringTools.Core`, driven by one CLI, `takeoff`
(`Kor.Operations.EngineeringTools.TakeoffCli`, 48 verbs; `takeoff --help` lists them and a test
enforces that the list is complete).

## Start here

- `CLAUDE.md` in the repo root. Twelve rules, each one written after it was broken at cost. They
  are gates, not advice. Rules 1 (search first), 5 (verify the artefact), 9 (data local, one
  harness, LOOK at the output) and 12 (a truncated command cannot support a claim about the whole)
  are the ones this pipeline keeps needing.
- `docs/etabs-handoff/FINDINGS-2026-09-01.md` — the measured issue list for the ETABS side.
- `docs/codex/CODEX-INTAKE-CONVERGENCE-1..11-*.md` — the intake series. 1–9 landed and are
  verified; 10 landed 2026-09-02; **11 is an adversarial audit of the intake work and is the most
  useful single read** for the state of it.

## Test data is local and you should keep it that way

Five KOR jobs, whose stick files exercise five different drafting conventions. **Copy them local
before iterating** — reading over SMB on every run has cost hours before (rule 9):

    \\Kor-fs01\Projects\Projects\03 Residential\<job>\05 Stickfile\*.pdf

    31130-01 (Uplands Lot 12)      imperial, two towers, PARKADE COLUMN SCHEDULE, no .e2k at all
    31138-01 (2170 W 1st)          imperial, 10 column marks incl. PL1
    31168-01 (YMCA Langara)        imperial, SUFFIXED marks (C02-A, PC03-A, GC11-C), 3 col schedules
    31065-01 (5350 Heather)        METRIC (states 1:100), wrapped schedule rows
    31202-01 (Hotel Circle SD)     no foundation schedule found in 59 pages — undiagnosed

## What is TRUE, measured, do not re-derive

- **Extraction is not the weak link.** `fitz.get_drawings()` returns 3,696 paths against PdfPig's
  3,683 on the same page — within ~1% on 3 of 4 pages sampled. Extracted text matches the RENDERED
  PIXELS character for character, including the drawings' own typos. ⛔ Do not re-platform onto
  PyMuPDF, pdfplumber or pdfium. It has been proposed and measured down twice.
- **The failures are all one class: a convention compiled into a binary.** A metric-only size regex
  read 1 of 5 jobs. A 2.5 column-aspect limit discarded 54 shapes at exactly 14x36 on one sheet
  because 31168 draws `PC01` at 14"x36". A 260-point heading scope fit one sheet size and returned
  nothing for a larger one. A mark regex could not match `C02-A`.
- **A regex parsing a NOTATION is fine** (`PrintedLength` reading `4' - 0"` has been right on every
  job). **A regex encoding a CONVENTION is the recurring fault.**
- **The scale is printed on the sheet** and 1/8" = 1'-0" is 1:96, not 1:100. `takeoff scale-scan`
  reads it where it is machine-readable (3 of 3 pages on 31065; 0 of 3 on 31168).
- **`annotationsOnly` used to default true with no caller able to change it**, so a clean issued
  drawing set read as completely empty. Fixed; `pdf-takeoff` reads the drawing by default.

## The self-check, which is the most valuable thing here

`PlanAgreesWithItsSchedule`. A sheet states its columns TWICE — as geometry and as a schedule — read
by entirely separate code, so agreement between them is evidence **needing no reference model**.
Every other gate in this repo compares against a reference model, an engineer's ruling, or a previous
run, so none can say anything about the first sheet of a new job.

    takeoff pdf-takeoff <pdf> <out.dxf> --pages 11-13 --scale 96
    ...  cover 43/72 labelled, emitted 266 (3.7x); unplaced C02-A,...

- **Coverage** = matched-to-their-own-mark / marks the drawing labels. Did we find what is there?
- **Precision** = emitted / labelled. Did we invent things? 3.7x means over-detection.

It has already earned itself twice: it caught a **wrong scale** (31065 at 96 vs its stated 100:
3/36 vs 19/36 — a 4% error, invisible in any rendering), and it found the aspect defect above.

⚠ It cannot see a column the reader MISSED, because it only scores what was found.

## Open, with evidence, in rough priority

1. **Table extent is under-determined.** Rows are attributed by proximity to a heading plus one
   anchor x. 31065 p14 has THREE "COLUMN SCHEDULE" headings and four mark columns (marks at x≈1370,
   zone columns at 894, footings at 500) inside a 467pt band. Three heuristics have each been right
   on some sheets and wrong on others — a fixed point scope, a topmost anchor, a densest anchor.
   ⭐ The tables are RULED (108 horizontal, 80 vertical measured in 31130 p12's schedule band).
   Bounding rows by the table's own drawn border would settle it with no heuristic. **Do this one
   next.** Symptom today: 31065 reports `8-35M` and `BOT.` as column marks.
2. **The sheet border, title block and schedule tables classify as slabs and beams** and go into
   every DXF. Same linework as (1) — one fix, two wins.
3. **31168 and 31202 read 0 footings.** Different causes, neither diagnosed.
4. **Over-detection**: 3.7x on 31168, from isolation-joint squares reaching the model as columns.
5. **`dxf.pdf.*` and `dxf.schedule.*` rule keys exist in code with no rows banked in KorStandards.**
   Settable in principle, unset in practice. Needs a migration.
6. ⚠ **UNVERIFIED and worth checking first:** is `dxf.max-column-aspect` banked at 2.5 on the
   DXF→ETABS side? If so the DXF path still discards 31168's `PC01`s exactly as PdfToSafe did.
   Nobody has been able to read KorStandards from a dev account to check
   (`KOR_ENGINEERINGTOOLS_STANDARDSDB`, login fails for `kor\ilalonde`).

## The export and deliver side — much less examined, look here with fresh eyes

This half has had far more scrutiny on correctness and far less on whether it is the right shape.

- `takeoff publish <job>` discovers, builds, verifies, gates and lands. `ShippedModelInvariants`
  holds the publish-BLOCKING checks; `docs/etabs-handoff/README.md` and `FINDINGS-2026-09-01.md`
  describe what ships.
- Three models ship today: `31168-FROM-DRAWINGS` (14 storeys), `31168-TOWERS` (64), and
  `31138-FROM-DRAWINGS` (29).
- ⚠ **Two tests are red by design** — `TheTwoPublished31168ModelsAgreeOnEveryStoreyTheyShare` and
  `...PriceBuildingCTheSame`, documented as C2-OPEN: the comparison cannot see the cut re-homing
  members. Do not "fix" them without reading that finding.
- ⚠ **One test is red environmentally** —
  `TheModelsThisCodeBuildsNowAgreeBeforeAnyOfItShips` cannot find the 31168 DXF folder; the share
  layout moved.

**Questions worth fresh eyes on the deliver side:**

- **Is DXF the right interchange at all?** It carries lines and layer names — no thickness, material,
  section or STOREY — so everything downstream re-infers meaning from geometry, and that inference
  is where four days went on "which storey does this member belong to". Meanwhile `EtabsE2kExporter`
  (351 lines) already writes a full multi-storey `.e2k` directly — storeys, materials, sections,
  diaphragms, loads — and `SafeF2kExporter` writes SAFE's native format, whose own comment says SAFE
  imports it and creates real structural objects with no DXF needed. **The better format is not yet
  the better route, because every gate is written against the DXF path.** That tension is unresolved
  and is worth a clear-headed look.
- The schedules now readable (mark, size, strength, reinforcing) are exactly the semantics a DXF
  drops. Nothing yet carries them into the `.e2k`, the S-Concrete `.SCO` loop, or a rebar takeoff.
- Is the publish gate checking the things an engineer actually gets burned by? It was built
  incident-by-incident.

## Working rules that will save you

- **Verify against the five real stick files, not just unit tests.** The last three rounds of this
  work all passed their unit tests and were wrong on real drawings.
- **Render it and LOOK.** `docs/etabs-handoff/plan_sheet.py` draws every storey of an `.e2k` on one
  sheet; `renderpage.py` rasterises a PDF page. Counts have missed faults a picture showed instantly.
- **State findings as X of Y.** A `head`/`tail`-truncated command cannot support a claim about a
  population, and a hook stamps truncated output to remind you.
- **Full suite before a publish** (~7 min); `--filter` while iterating (seconds). A test that passes
  alone and fails in the suite is SHARED STATE, not luck — look for a mutable static first.
- Ian runs Codex. Briefs go in `docs/codex/CODEX-<TOPIC>.md`, handed over, not invoked.
