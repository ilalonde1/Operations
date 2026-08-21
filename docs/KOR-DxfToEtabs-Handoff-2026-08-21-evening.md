# DXF → ETABS — where this stands, evening of 2026-08-21

Written before a context compaction. Read this first; verify anything it claims before acting on it.

Operations `2588be88` on `develop`, pushed. KOR.Drafter `99ebc7b` on `main`, pushed.

## The thing that happened today

**A generated model was imported into ETABS for the first time.** It opened, and it stands there as
a building — plates, cores, columns, parkade wall. That import found a defect no test suite had
found in weeks: opening polygons were written with vertices out of perimeter order, so they
self-intersected and ETABS silently discarded every six-point opening on the job. Fixed
(`InPerimeterOrder`), re-imported, errors gone.

Everything else below is secondary to that. **The import is the test that finds things.**

## What is in Andrea's folder right now

`\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\`

| file | what |
|---|---|
| `31168-FROM-DRAWINGS.e2k` | 23 storeys · 531 walls · 1,147 columns · 26 plates |
| `31168-FROM-DRAWINGS-report.txt` | placed sheets first, cut sheets under their own heading |
| `31168-QUESTIONS-for-Andrea.xlsx` | J1/J2/J3 NEEDS YOU on top, then 18 DECIDED, then 5 SCOPE |
| `KOR-31168-SUMMARY.pdf` | one page — the thing to show her |

Scope is what she asked for on 21 Aug: parkade P1–P3, **Level 1 entire** (both towers' ground
floors included, they sit at grade inside the podium), YMCA to `C-ROOF`, no towers above grade.
Produced with `-TopStorey "C-ROOF"`, which cuts by ELEVATION. `--tower C` cannot express it: it
cuts by name, keeps the 16 unprefixed tower floors (levels 11–26, labelled BLDG A&B on the
drawings), and throws away `A-LEVEL 1` / `B-LEVEL 1` which are wanted.

## Open, and honest about it

1. **Four storeys have no floor plate** — `A-LEVEL 1`, `B-LEVEL 1`, `C-LEVEL 3`, `LEVEL 1 MEZZ`.
   208 slab-edge outlines will not close on the Level 1 sheet. **Tested at bridge tolerance 6, 12
   and 18 inches: identical output.** Not a tolerance problem. This is J1 in her workbook and it is
   the single best question to put to her.
2. **18 wall outlines would not resolve into panels** — J3, with measured sizes.
3. **`C-ROOF` has a plate with nothing beneath it** — J2.
4. **Nobody has checked the walls are in the right PLACES.** `ModelCoverageTests` has only ever run
   against the full 63-storey model, never this cut one.
5. **The 88" opening height is still unverified.** Codex proved `ReferenceRules` works; the
   reference simply carries no SPANDREL labels, so the derivation never fires.
6. **Column aspect ≤ 3.0 rejects 17.9% of portfolio columns.** Delicate: Andrea ruled 3.0 and her
   own model's most slender column is exactly 3:1. Do not overrule an engineer with a statistic.

## Things proven today, so nobody re-derives them

- **The ETABS diaphragm warning on import is HER reference model's**, not ours. `D1` is assigned to
  areas at both LEVEL 2 (2,045.2) and LEVEL 3 (2,255.2). Every storey elevation in our output is
  identical to the reference — checked. We assign no diaphragms at all, by her ruling.
- **`JBP_C_B_STRUCT` is not structure.** 1,078 line entities across 57 of 62 sheets; she was shown
  a plot of it and said "no, ignore those". Banked in KorStandards, migration 042.
- **`--bridge`, `--join`, `--extend` used to be dead flags** — `ApplyRules` took the database value
  over the caller's, so `--bridge 14` ran at 6 in silence. Fixed. A conclusion in the gap register
  had been reached through one of them.
- **Openings: 47 defined, 47 assigned by us.** A 48th `OPENING "Yes"` in the file is the engineer's
  own, untouched.

## Traps

- **Never write a C# regex or a Windows path through a non-raw Python string.** `\b` becomes a
  BACKSPACE silently. Cost an hour today and a Codex brief built on a wrong hypothesis. CLAUDE.md
  rule 7.
- Full suite is 10–14 min; ~20 tests rebuild both buildings over SMB. **Do not start one and keep
  editing** — it locks the output and wedges test hosts in SMB I/O.
- The suite has **not** run on `2588be88`. Last green was 491 at Codex's run, two changes earlier.
- Codex also works in this tree. Check `git log --oneline -15` before assuming your view is current.
