# Codex response — step 87 review (2026-09-16 08:30), and what was done with it

**Codex's counterexample** (verbatim): an unlabelled horizontal sheet title ROOF PLAN (40 × 12 pt tokens at
(2280, 200), (2330, 200)) beside a larger upright FOUNDATION PLAN section marker (24 × 168 and 24 × 72 at
(2440, 500), (2440, 630)) on a 2592 × 1728 page. The upright tokens become one FOUNDATION PLAN run (gap 10,
under the 48-pt split), transformed height 24 against the title's 12; `TitleText` admits the marker solely
because it names a plan and has the largest font. The horizontal-only rule selects ROOF PLAN.

**Confirmed** by the test `TheHorizontalTitleOutranksALargerUprightMarker` at Codex's tokens: red before the
rule ("FOUNDATION PLAN"), green after. **Step 92** (`SheetTitleReader.TitleText`): the readings are asked in
order — the horizontal words, then the upright — and the first with a plan-naming block answers. 30941 and
01589 still read their upright titles (their horizontal words name no plan). 1,457 green, six byte-identical.
