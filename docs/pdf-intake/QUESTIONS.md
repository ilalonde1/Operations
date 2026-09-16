# PDF intake — open questions for Ian

One file, one line per question, newest first. Answer by editing the line or by mail; a question leaves this file
the day it is answered (its answer goes where it belongs: a rule, a row, a section). Nothing here blocks the work.

## Open (2026-09-16 14:20)

- **Migrations 094 and 095** — `KOR.Drafter/db/094_ARectangleNoScheduleDeclaresIsAWallPier.sql` (two rows: pier
  long side 24 in, aspect 2) and `095_TheYardstickIsHerPrimaryModel.sql` (two vocabulary rows). Idempotent, in order
  after 093. Tell me when they are applied and I remove the two keys from `UnbankedByDesign`.
- **Item 8 — three live jobs for a current engineer's look.** No longer what the work waits on: the corpus's own
  review (`takeoff corpus-disagreements`, §111) is the input; your picks are the confirmation at the end.

## For an engineer, when one is asked (few, each backed by a measurement)

- **Piers.** A filled rectangle no column schedule declares, longer than 24 in and twice as long as thick, is now
  modelled as a wall pier (step 99; 622 of our unmatched columns over 37 of her own models stand on a wall she
  modelled). Confirm or correct the two numbers — one line.
- **Openings.** A stair or shaft drawn as a closed loop inside a floor is now filled (step 98). Should the model
  carry it as an opening, and by what mark on the plan (a hatch, a word, a layer)?
- **Her primary model per job.** The yardstick now prefers a model named FULL/GRAVITY over the newest file and
  never a 2NDRY/CRANE/MASS/CHECK one (step 101). Is that the office's practice for naming the main model?

## Answered

- **Q1 — 31162's wood-plan "footings" (item 3b, §94)** — answered by the corpus 2026-09-16 13:12: of our columns her
  models have no partner for, 622 over 37 sets stand on a wall she modelled, 483 of 554 with a section 24 in or
  longer on the long side; her ruling W1. Shipped as step 99 (`ac76d7e8`): a size the schedule declares is a column,
  an undeclared one over 24 in and twice as long as thick is a wall pier. The wood beam-with-post symbol on 31162
  (21 × 50, undeclared) reads as a pier under it — a wall, not a footing and not a column.
- Migration 093 — applied by Ian 2026-09-16 morning; `dxf.parkade-words = P;B` is live; run 26 measured it (one mover).
- Codex — the six review briefs ran 2026-09-16 08:30–09:25: four rules shipped (steps 92–95), two limits stated (§105, §107).
