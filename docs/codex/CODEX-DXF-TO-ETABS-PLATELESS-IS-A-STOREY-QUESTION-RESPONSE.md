Applied.

What changed:

- `E2kDocument.FloorGapDetails()` now classifies a storey by member coverage, not by whether any plate exists on the merged floor:
  - `covered == 0` -> `FloorsWithNoPlate`
  - `0 < covered * 2 < member count` -> `MostlyUncovered`
  - mezzanine partial coverage remains suppressed
- The floor-grouping stays in place, so the shared-ground-floor case still sees a plate if that plate actually reaches the storey's members.
- `ModelQuestionnaire` now suppresses standing row `F2` whenever a real report has no plateless-storey flag. `J1` was already emitted only from that same flag, so its absence in the regenerated building-C workbook is deliberate when the no-plate list is empty.
- `F2` wording now describes the saved-model condition instead of asserting a specific slab-layer cause.
- The floor-gap fixture now asserts that `LEVEL 1` is mostly uncovered and not plateless.
- Added a focused questionnaire regression test: no plateless flag means no `F2` or `J1`; a plateless flag means both rows name the storey.

Expected 31168 report diff from this predicate:

- Site model: `B-LEVEL 28` should move out of the partial-coverage sentence and back into the strong "no floor plate at all / no diaphragm" sentence.
- Building-C model: no new plateless storey should appear; `F2` and `J1` should stay absent if the list is empty.
- Shared ground-floor storeys should not change because their members are actually covered by the shared plate.

Per the brief, I did not run `dotnet build`, `dotnet test`, or touch the 31168 job share.
