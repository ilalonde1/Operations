# Codex 26 of N — the drawings' storeys against the model's

## Goal

Hand what brief 25 reads per sheet to the ETABS build as a check. The build takes its storeys
from the reference model; the drawings state every storey height on their wall elevations. Where
the two disagree, somebody should be told, and until now nothing compared them.

Implemented by the verifier on 2026-09-08 (Codex out of usage); this is the record.

## The class, in one sentence

A set's storey table is the union of its elevation sheets' ladders reconciled by the pair of level
names; the model's height for the same pair is the difference of the two named elevations, and the
build reports where the two disagree without changing the model.

## Changed

- `Core/Intake/SetStoreys.cs` (new): `Read(pdf)` — one light vector read per page, the sheet typed
  as the intake types it, the ladder read on section/elevation sheets only; `Reconcile` — median
  and spread per (level, level below) pair, first-seen order.
- `Core/Intake/StoreyAgreement.cs` (new): `Compare(table, stories, unitInInches)` — the model's
  height for a pair is Elevation(level) − Elevation(level below) when the model names both;
  `Result.Summary()` is the one line the build reports. Tolerance 25 mm.
- `DxfToEtabsService`: when `--stick-file` is given, the summary line is added to the warnings.
  Nothing about the model changes.
- `takeoff storeys-check <stickfile.pdf> <model.e2k>` prints the table and the line.
- Tests: `TheDrawingsStoreysAgainstTheModelsTests` — median and spread across sheets; the
  interleaved-site-list case (LEVEL 11 → LEVEL 10 is 116 in, not the 77.5 in to C-ROOF); an
  off-tolerance storey named with both numbers; the empty case says why.

## Measured (`takeoff storeys-check`, 31168 stick file against its reference model)

    22 storeys on 4 of 4 section/elevation sheets
    20 match the model by both level names; 20 of those within 25 mm — every delta 0 to +5 mm
    unmatched: L2 -> L1 (2,808 mm), L1 -> P1 (8,652 mm): the model names level 1 A-LEVEL 1 / B-LEVEL 1

Live, on the share (`TheLiveSetsStoreysAgreeWithTheirModelTests`, 13 s): the newest dated stick
file under 31168's "05 Stickfile" (2026-08-25, with architectural sheets) against
`31168-reference.e2k` — at least 18 matched, none off tolerance, holds.

## What remains

- Level names the two sides spell differently (L1 against A-LEVEL 1 / B-LEVEL 1): a building-aware
  match, once the per-building ladders are separated (the busiest-column rule takes one today).
- Replacing the reference storeys with the drawings' where no reference exists — the first sheet of
  a new job — is the step after the check has been trusted on the jobs that have both.
