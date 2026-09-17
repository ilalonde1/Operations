# PDF intake — open questions for Ian

One file, one line per question, newest first. Answer by editing the line or by mail; a question leaves this file
the day it is answered (its answer goes where it belongs: a rule, a row, a section). Nothing here blocks the work.

## Open (2026-09-16 18:25)

- **Migration 096** — `KOR.Drafter/db/096_TheProfessionsKnowledgeIsRows.sql` (WP7: the `knowledge` schema — Source,
  Clause, RuleClause, the view — six sources, the four NBC 9.8 stair clauses and their links). Idempotent, after 095.
  Parse-probed with the app's login, which cannot create a schema, so its first live run is yours; the closing SELECT
  shows four rule-to-clause rows. Tell me when it is in.
- **Item 8 — three live jobs for a current engineer's look.** No longer what the work waits on: the corpus's own
  review (`takeoff corpus-disagreements`, §111) is the input; your picks are the confirmation at the end.

## For an engineer, when one is asked (few, each backed by a measurement)

- **Piers.** A filled rectangle no column schedule declares, longer than 24 in and twice as long as thick, is now
  modelled as a wall pier (step 99, rows live in 094; run 28: 2,496 columns became walls over 158 sets). A SCHEDULED
  size stays a column — and there the office has two practices: 31130 schedules 14×36 and models it as a column;
  31087 schedules 36×44 (×56), 31017 18×30 (×70), 31053 24×36 (×36) and model them as wall piers (579 of ours still
  stand on a wall of hers after step 99, run 28's disagreements). Which is the rule — the schedule, or the 24-in
  line whatever the schedule says? One line.
- **Openings.** The drafter's X across a box is now read as a shaft's opening (step 104), and 78% of ours are hers
  where she modelled (111 of 142 on five sets). Two things the plan does not say: (a) 31168's L15–26 draw a
  3.6 × 1.5 m X-box either side of every perimeter column, 0–700 mm in from the slab edge — what is it (a recess, a
  step, a drop)? It is not cut. (b) Her shafts drawn WITHOUT an X (31202's 2.5 × 3.5 m elevators on nine storeys, its
  8.5 × 31 m void) — by what mark on the plan should those be read (a hatch, a word, a layer)?
- **Her primary model per job.** The yardstick now prefers a model named FULL/GRAVITY over the newest file and
  never a 2NDRY/CRANE/MASS/CHECK one (step 101). Is that the office's practice for naming the main model?

## Answered

- **Migrations 094 and 095** — applied by Ian 2026-09-16 ~15:50 (a first run ~15:30 failed on a duplicate Topic and
  NULL SettingUnits in both files; corrected and probed in a rolled-back transaction first). Five rows live, read back
  from `vw_RuleSetting`; the pier keys left `UnbankedByDesign` in `f5616a25`.

- **Q1 — 31162's wood-plan "footings" (item 3b, §94)** — answered by the corpus 2026-09-16 13:12: of our columns her
  models have no partner for, 622 over 37 sets stand on a wall she modelled, 483 of 554 with a section 24 in or
  longer on the long side; her ruling W1. Shipped as step 99 (`ac76d7e8`): a size the schedule declares is a column,
  an undeclared one over 24 in and twice as long as thick is a wall pier. The wood beam-with-post symbol on 31162
  (21 × 50, undeclared) reads as a pier under it — a wall, not a footing and not a column.
- Migration 093 — applied by Ian 2026-09-16 morning; `dxf.parkade-words = P;B` is live; run 26 measured it (one mover).
- Codex — the six review briefs ran 2026-09-16 08:30–09:25: four rules shipped (steps 92–95), two limits stated (§105, §107).
