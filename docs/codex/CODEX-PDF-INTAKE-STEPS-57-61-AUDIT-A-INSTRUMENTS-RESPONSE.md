# Intake steps 57–61 — audit A response

Source deductions, not executed reproductions. Reviewed the brief's source/test reading set and the specified documentation excerpts at working-tree HEAD `69ecef0c` (the handover names `d42ad436`). No commit-range comparison, builds, tests, drawing/model/CSV files, database, network, or brief B investigation. The examples below are hypothetical inputs traced through the inspected code; none is a claim about prevalence in the corpus. Existing unrelated working-tree edits were left untouched.

## 1. High — lost and gained members need not describe one matching

**Claim:** §70 F17/F18: “every member takes ONE partner (a second copy is gained).”

**Source:** `Kor.Operations.EngineeringTools.Core/Dxf/ModelDiff.cs:106–108,139–159`.

**Smallest input:** one storey, before columns at X = `{0,2}`, after columns at X = `{-2,1}`, all Y = 0. Supply unchanged shared grid labels sufficient to select shift `(0,0)`, so registration cannot absorb the example. All extents are zero.

**Deduction:** the before-to-after pass visits 0 first and takes 1, its nearest partner. Before 2 cannot take the remaining −2, so it is lost. The after-to-before pass independently visits −2 first and takes 0; then 1 takes 2. Nothing is gained. The report therefore says two columns before, two after, **one lost and zero gained**. A complete permitted matching exists: `0 ↔ −2`, `2 ↔ 1`, both within the two-unit coordinate tolerance. Each individual pass respects exclusivity, but their outputs do not describe one shared one-to-one matching. This can report movement where the comparator's own admissibility rule permits complete agreement.

The requested three-member version is before `{0,1,2}`, after `{-2,-1,0}`: the forward pass loses 2, the reverse pass gains nothing, although pairing each member with its two-unit-left counterpart matches all three. `ATurnedWallAndASecondCopyAreSeen` does not exercise competing partners.

## 2. High — the twin rule cannot distinguish tessellation from two adjacent shapes, and ambiguous partners change the union

**Claim:** §67 correction: “Two filled triangles of one colour sharing an edge whose union is a convex quadrilateral are one shape”; §70 item 4: “the first carries the four corners”.

**Source:** `Kor.Operations.EngineeringTools.Core/PdfToSafe/TriangleTwins.cs:53–81`; `Kor.Operations.EngineeringTools.Core/PdfToSafe/GeometryFilterService.cs:239–246`.

**Smallest input:** two separate, closed, filled hatch cells of the same colour, neither annotated nor clipping: `T0 = [(0,0),(600,0),(600,600)]`, `T1 = [(0,0),(600,600),(0,600)]`, in millimetres. They are geometrically identical to a tessellated rectangle but semantically two cells.

**Deduction:** the rule accepts them as one square and suppresses independent processing of T1. No inspected predicate distinguishes the cells' provenance or purpose. This is a defect in the stated inference “are one shape”, rather than a failure to implement that predicate. It does not by itself prove that the downstream classifier emits a column.

The smallest ambiguous extension adds `T2 = [(0,0),(600,0),(300,-300)]`. T0 forms a convex quadrilateral with either T1 or T2. Input `[T0,T1,T2]` pairs T0 with T1; `[T0,T2,T1]` pairs it with T2, because the first acceptable neighbour wins. The union's geometry changes and the other triangle remains unpaired. Thus even a real T0/T1 rectangle can lose its intended partner to adjacent same-colour ink. Two arrowheads meeting only at their tips are **not** this example: they share only one vertex and fail the explicit two-vertex condition.

## 3. High — equal-within-tolerance diagonal endpoints can be invisible to the twin lookup

**Claim:** §70 item 4: “Two filled triangles of one colour that share an edge and whose union is a convex quadrilateral are one shape”. The class additionally promises shared vertices “equal to a hundredth of a millimetre”.

**Source:** `Kor.Operations.EngineeringTools.Core/PdfToSafe/TriangleTwins.cs:19–20,46–50,59–67,86`.

**Smallest input:** eligible same-colour triangles, in mm:

- T0: `(0.499,0)`, `(600.499,0)`, `(600.499,600)`.
- T1: `(0.501,0)`, `(600.501,600)`, `(0.501,600)`.

**Deduction:** the intended shared vertices differ by only 0.002 mm in X and pass `Near`. But T0 is indexed in cells `(0,0)`, `(600,0)`, `(600,600)` and T1 in `(1,0)`, `(601,600)`, `(1,600)`. None coincides. The lookup never presents T1 to the shared-edge test, so both stay unpaired. One boundary-crossing endpoint alone is insufficient to demonstrate the miss if the other shared endpoint still supplies a common lookup cell; this example crosses at both.

The lookup rounds to whole millimetres, but equality is **not** rounding to hundredths: it independently requires `abs(dx) <= 0.01` and `abs(dy) <= 0.01`. A 0.02 mm mismatch fails that explicit limit; fans of three or more triangles are explicitly excluded by the class remarks. Neither is an additional undisclosed implementation finding.

## 4. High — a turned wall can have exactly the same centre and X/Y extents

**Claim:** §70 F17/F18: “a wall carries its extent along each axis (a wall turned in place is lost and gained)”.

**Source:** `Kor.Operations.EngineeringTools.Core/Dxf/ModelDiff.cs:153,189–192`; `Kor.Operations.EngineeringTools.Core.Tests/Intake/TheDifferentialAndTheRenderAreCodeTests.cs:155–171`.

**Smallest input:** a panel's plan points change from `[(-100,-100),(100,100)]` to `[(-100,100),(100,-100)]`, with an unchanged frame and storey. These are different diagonal walls crossing at their centre.

**Deduction:** both become `Member(0,0,200,200)`. Neither is lost or gained. Reflection of a thin rectangular panel about an axis gives the same collision if four plan corners are required. The encoding preserves the bounding box, not the diagonal's direction.

A 45-degree rotation is not inherently missed: an axis-aligned long wall rotated 45 degrees changes both extents and is lost/gained, as the documented contract intends. But a wall at +22.5 degrees rotated to −22.5 degrees turns 45 degrees while keeping both extents. For endpoints `±(100,100*(sqrt(2)-1))` and their Y-reflected counterparts, both encode centre `(0,0)` and rounded extents `(200,83)`. The existing test exercises only the horizontal-to-vertical case. The reversed differential inherits this blind spot despite its claim to compare every wall's placement.

## 5. Medium — the unit differential compares XY multisets, not every joint's place and connectivity

**Claim:** §70 F24: “the unit differential compares every joint's place to a ten-thousandth of an inch, every member's kind, storey and joints”.

**Source:** `Kor.Operations.EngineeringTools.Core.Tests/AModelIsTheSameInInchesAndMillimetresTests.cs:79–94,120–122`.

**Smallest input:** two emitted models with identical point names and XY coordinates, but a different optional Z offset on one point. Alternatively, keep four corner coordinates fixed and change a panel's ordered connectivity from perimeter order `A B C D` to crossing order `A C B D`.

**Deduction:** the point regex records only the first two numeric coordinates, so the Z change does not reach `JointsInInches` or `Placements`. The connectivity reader replaces joint names with XY strings and sorts them; the two panel orders also become identical strings. Storey names are compared, but storey elevations and connectivity's numeric level offsets are not represented. Thus a changed spatial joint or changed panel topology can satisfy the asserted summaries. These are hypothetical mutations of emitted output, not a claim that the fixture currently produces them.

`N()` itself is accurately described as four decimals in inches: 0.0001 inch is **0.00254 mm**, finer than 0.01 mm, not coarser. The assertions compare rounded coordinate strings, not Euclidean distance within that tolerance; values on opposite sides of a rounding boundary can compare unequal despite a smaller separation.

## 6. Medium — the unit check loses which section owns each dimension

**Claim:** §70 F24: “and both D and B”; the class's WHAT THIS COVERS says “every section's size agrees once converted”.

**Source:** `Kor.Operations.EngineeringTools.Core.Tests/AModelIsTheSameInInchesAndMillimetresTests.cs:69–74,96–97,119`.

**Smallest input:** two sections, initially `S1: D=10 B=20`, `S2: D=30 B=40`; in the other model, `S1: D=10 B=40`, `S2: D=30 B=20`, after conversion to inches. Preserve the members and section assignments.

**Deduction:** both models produce the sorted dimension strings `{B 20, B 40, D 10, D 30}`. Section identity and the association between D and B have disappeared. The new check does read both fields, but each section's dimensions can change without changing its assertion. `Placements` also carries no assigned section, so it cannot recover that association.

## 7. Medium — a same-count placement change need not read as Composition; it can disappear entirely

**Claim:** §68 WHAT THIS DOES NOT: “a placement that changed to the same COUNT of different sheets (reads as Composition)”; the main paragraph describes “with both the same the composition”.

**Source:** `Kor.Operations.EngineeringTools.Core/Intake/CorpusDiff.cs:54–62`; `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/CorpusQueryVerb.cs:174–179`.

**Smallest input:** one job has a model in both rows, one built storey, one placed sheet, one column, no walls and no plated storeys. Before, sheet A supplies the column at one location; after, sheet B supplies it elsewhere, while those counts remain equal.

**Deduction:** `Classify` returns `Unchanged`, not `Composition`, and the job is omitted from the printed movers. Composition requires a changed column, wall or plated-storey count. The documented count-only limitation is real, but its stated fallback is too strong: unchanged counts cannot establish unchanged composition.

Even information that the set ledger can distinguish is ignored: change only `SheetsWritten` from 1 to 2, holding the compared fields fixed, and the class remains `Unchanged`. This could distinguish a changed input/view population; the existing classes do not represent it. Storeys before placement is implemented and explicitly tested with both changing; it is a deliberate priority, not evidence that placement stayed equal or a causal explanation of the movement.

## 8. Medium — the diff drops complete recall misses from yardstick verdicts

**Claim:** §68: “the yardstick's verdict where a set has one”. §70 F19 explicitly recognises that a set with none of ours inside the footprint and columns of hers on shared storeys “is a complete recall miss”.

**Source:** `Kor.Operations.EngineeringTools.Core/Intake/CorpusDiff.cs:32`; `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/CorpusQueryVerb.cs:168–170,178`. Contrast its `Summary` at `:61–64`.

**Smallest input:** before: `OursCompared=1`, `OursWithin100=1`, `TheirsCompared=1`, `TheirsWithin100=1`; after: `OursCompared=0`, `OursWithin100=0`, `TheirsCompared=1`, `TheirsWithin100=0`, with a yardstick on both rows.

**Deduction:** `HasYardstick` is false because it requires positive **ours** denominators on both sides. The set contributes no better/worse/same verdict or yardstick totals; a displayed mover gets `-` for its yardstick. Recovery from a complete miss is excluded in the same way. F19's change to `corpus-query summary` is present and handles this population, but the adjacent diff still silently excludes it. The issue is the broader §68 verdict claim, not a claim that the specifically named summary fix is absent.

## 9. Medium — “better / worse / same” means supported count, without saying that it ignores share and recall

**Claim:** §68 reports “yardsticks 14 better / 8 worse / 16 same” and presents this as “the yardstick's verdict”.

**Source:** `Kor.Operations.EngineeringTools.Core/Intake/CorpusDiff.cs:32–33`; `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/CorpusQueryVerb.cs:164–170`.

**Smallest input:** one eligible set changes from `OursWithin100/OursCompared = 8/64` to `5/10`.

**Deduction:** `Within100 = −3`, so the table reports **0 better / 1 worse / 0 same**, beside `8 of 64 -> 5 of 10`. Supported share improved from 12.5% to 50%. Conversely, a fixed supported count with a much larger denominator is called “same”. The raw fractions are visible, but neither the heading nor the inspected §68 text defines “better” as an absolute-count judgment. No theirs-to-ours recall field participates. This is an inadequately labelled metric, not proof that share is always the preferable measure. The fixture's better/worse cases have count and share move in the same direction and cannot expose the distinction.

## 10. Medium — reversing an attributed INSERT breaks its compound entity

**Claim:** §70 F11: “the six sets composed as they are and with every view's entities reversed must be the same structure”; `DxfSheet` describes this transformation as the same view and says “everything else is left as it stands”.

**Source:** `Kor.Operations.EngineeringTools.Core/Dxf/DxfSheet.cs:64–81`; `Kor.Operations.EngineeringTools.Core.Tests/Intake/TheSameDrawingsInAnotherOrderBuildTheSameStructureTests.cs:104–129`.

**Smallest input:** an ENTITIES section containing `INSERT` with attributes-follow flag 66=1, followed by one `ATTRIB`, then its `SEQEND`.

**Deduction:** only POLYLINE activates compound grouping. INSERT, ATTRIB and SEQEND become three independent list entries and reverse to `SEQEND, ATTRIB, INSERT`. The result changes ownership/validity of the attribute sequence, not merely independent entity order, so a model difference from this input is not evidence of composer order dependence. The fixture proves the R12 POLYLINE case, not this one.

BLOCKS is outside the selected ENTITIES span and is left untouched; this code does not reverse or break that section. Whether the six banked views contain attributed INSERTs is **not established by the allowed reading set**. No drawing inspection was authorised, and no corpus-impact claim is made here.

## 11. Medium — the yardstick's smaller-move tie-break depends on the relative coordinate origins

**Claim:** §68: “judged by its SUPPORT; a tie in support goes to the tighter cluster, then the smaller move”.

**Source:** `Kor.Operations.EngineeringTools.Core/Dxf/ModelYardstick.cs:438–453`.

**Smallest input:** one shared storey, ours `{(0,0)}`, theirs `{(-1000,0),(1000,0)}`. Both candidate bins have one vote, support one and spread zero. The ascending bin order retains shift `(−1000,0)` when move lengths tie.

**Deduction:** now express our same one-point model in a translated coordinate frame, ours `{(1500,0)}`. The candidates are −2500 and −500; the smaller move selects −500, aligning us to the *other* engineering column. Preserving the original correspondence would require −2500. Equal fit quality is resolved by origin proximity, not a translation-invariant property of the fit.

The implementation follows the quoted tie-break literally. The problem is its unstated assumption, rather than a different algorithm: the inspected yardstick rule establishes no prior preference for models already occupying nearby coordinates. A reissue comparison can have that prior; these cross-model inputs need not. Translating both models together leaves displacement vectors unchanged and is not this counterexample.

## 12. Low — the frame regression test never pits better support against a worse candidate

**Claim:** §68: “every bin within one vote of the fullest is refined to its median and judged by its SUPPORT”, supported there by `TheYardsticksFrameIsJudgedBySupportAndNeverByTheOrderOfOurColumns`.

**Source:** `Kor.Operations.EngineeringTools.Core.Tests/Intake/AModelIsMeasuredAgainstTheEngineersOwnTests.cs:145–161`; `Kor.Operations.EngineeringTools.Core/Dxf/ModelYardstick.cs:438–452`.

**Smallest input:** the test's own ours `{0,6000,24000}` and theirs `{0,6000,30000}`, all Y=0, in the two specified orderings.

**Deduction:** the competing 0 and +6000 shifts each have two votes, support two and zero spread. The assertions prove that these two input orders select the zero move with support two. They do not prove that support defeats a higher-vote bin, that a bin one vote below the fullest can win, or that tighter spread wins. A rule choosing the smaller move among fullest bins, then measuring its support, would satisfy this fixture while omitting the newly claimed support-based selection. The current production code visibly performs support scoring; this is a test-coverage finding, not a claim that that branch is absent.

**Requested loop analysis (C; known failure, not a new finding).** Ranked by the earliest visible mechanism that can change topology in `PlanLoopBuilder.Build`:

1. **Seed order and global edge consumption** (`:104–124`). A seed's starting direction is its supplied Start→End, and every traversed edge becomes globally used. Two rings sharing an edge cannot independently claim that edge after the first walk consumes it. The choice persists even if that walk ultimately becomes an open chain or a simplified loop is discarded. This is the primary shared-edge ownership decision.
2. **Node creation and numbering** (`:56–79,91–97`). It is more than a numerical tie: the first encountered coordinate remains the representative. At tolerance 0.05, collinear endpoints 0, 0.04, 0.08 form one node if 0.04 arrives first; endpoints 0 and 0.08 arriving first form two nodes, and 0.04 then ties to the earlier ID. Both the partition and representative coordinates can change before walking begins.
3. **Adjacency insertion order at a junction** (`:85–98`) is explicitly arrival order and is handed to continuation selection. It is a potential carrier of the seed-order preference at every junction.
4. **`PickContinuation`'s tie** is the point where that candidate list becomes an edge choice (`:119`). The brief permits reading `Build`, not this helper's body, so the exact tie comparator and whether it uses adjacency order cannot be established here. Its ranking after adjacency describes data flow, not a measured independent contribution.
5. **`BridgeChains`** receives the already order-dependent chain list (`:139–144`). Its own internal selection rule is likewise outside the authorised region. It can propagate changed inputs; an additional intrinsic order dependence is not proved by this call site.

A rule for which ring owns a shared edge must resolve seed/edge consumption and continuation at junctions, and what becomes of consumed edges when a walk fails. Node equivalence is a separate earlier ambiguity; a ring rule alone does not decide it. No coordinate sort is proposed. The same earlier-ID tie is visible in the composer's `PointAt` (`E2kGeometryComposer.cs:700–703`), while `PlacedMembers.Near` (`:1981–1982`) uses Euclidean distance as F10 states.

**Other requested distinctions (A, D, F).**

- **Twin fates:** `GeometryFilterService.cs:242–243` clones an existing `PathFate` with only `PathIndex` changed. That operation cannot itself make its copied disposition contradict its copied reason. If the first fate is absent at that moment, `:244` skips the second path without adding a fate. Deferred-fate lists are visible nearby, but their producing/finalising branches and `EveryPathHasExactlyOneFateTests` are outside the reading set. A concrete raw-path input reaching that missing-first-fate state is therefore **not established**, and no invariant violation is asserted as a finding. Nor can this local snapshot copy prove equality after any later fate updates.
- **Registration support:** `ModelYardstick.cs:448` counts each supplied *our* point that has **any** qualifying *their* point. For a fixed candidate this is independent of enumeration order, but is not a one-to-one count: ours `(0,0)` and `(10,0)` can both be supported by theirs `(0,0)` at shift zero. Pair votes also still determine candidate eligibility (`:438`). Repeated readings can affect both eligibility and support; §68's removal of pair-count-only winner selection should not be read as deduplication or matching distinct engineering columns.
- **Wall distance:** `ModelYardstick.cs:297,302` explicitly avoids polygon parity for two-point walls. For three or more points it returns zero for even-odd interior (`:304`), so §70 F20's literal “distance to the wall's edges” is narrower than the implementation. A square with corners `(±1000,±1000)` gives its centre distance zero rather than the edge distance 1000. This is sensible for distance to a filled wall footprint, but requires that interpretation. A self-crossing ring is not rejected: for `[(-1000,-1000),(1000,1000),(-1000,1000),(1000,-1000)]`, point `(0,700)` lies in the parity-filled top lobe and also returns zero, despite its nearest edge being 300 mm away. Whether such a ring reaches this method is unestablished. The “on her walls” count additionally requires nearest engineering column distance greater than 300 mm (`:194–198`); it is a subset of unsupported columns, not every column lying on a wall.
- **Duplicate ownership:** `CorpusAnalyzer.cs:253–268` proves byte identity within equal-name/equal-length groups, then assigns a representative by filename prefix, else lexicographic job order. If identical bytes genuinely belong to folder/job A but were misnamed with participating job B's number, B wins; the mapping labels A as B's copy. If the filename's job is absent, the fallback can identify a representative that is not the true owner. `AnotherJobsFileReason` (`:236–237`) calls this “the stick file of another job: byte-identical to”; the test constructs that reason but expressly excludes the actual emitted row. Filename/folder disagreement alone cannot establish true ownership. Renamed copies being missed is already documented and is not re-reported as a new finding.
- **Population changes:** `CorpusDiff.Compare` puts only intersecting jobs into classes (`:47–51`); jobs present on just one side are explicitly printed as only-before/only-after by the verb. “Every set” therefore means every shared job for the class table. The fixture itself asserts seven classified jobs plus one on each side (`WhatMovedBetweenTwoRuns…Tests.cs:57–59`); it does not hide the distinction.

**Assertion-to-coverage accounting (G).** These conclusions concern the inspected assertions, not test execution. For the two last-test-only files, class summaries were outside the permitted region.

| Test file / permitted test | What its assertions establish, and where the wording is wider |
|---|---|
| `WhatMovedBetweenTwoRunsIsClassedByTheFirstThingThatChangedTests` | Exercises each class, the specified priority, two yardstick deltas and absent yardstick. Its “sums a class reports” checks sum columns only in singleton NewModel/LostModel groups; composition walls/plates are individual mover properties. It does not test aggregation across multiple movers, zero-ours recall cases, or conflicting count/share verdicts. The printed table is explicitly excluded. |
| `AStickFileThatIsAnotherJobsIsReadOnceTests` | Checks filename owner, absent-name fallback, same-name/length but different bytes, mapping cardinality and two log entries. Matches the narrow WHAT THIS COVERS. “Read once” and rows “build nothing” in the opening summary are not integration assertions; neither file-read counts nor analyzer builds are observed. The emitted row and renamed copies are explicitly excluded. |
| `TheSameDrawingsInAnotherOrderBuildTheSameStructureTests` | The six-set body, if enabled, checks zero shift and zero ModelDiff changes, inheriting findings 1 and 4 and rounded plate-area comparison. The literal Skip means it currently supplies no running six-set gate, exactly as §70 discloses. The active transformation fixture checks selected record fragments and reversal twice; it does not compare every transformed entity field. A reversible alteration of an unchecked coordinate could satisfy those assertions. The remarks' example of two distinct entities keeping relative order under a full reversal is impossible; the exclusion of other permutations is nevertheless valid. |
| `ADashedLineIsJoinedWhereverThePageOriginIsTests` | Asserts joined counts 1 and 1 after translation, plus count 2 for separated lines (`:31–40`). “Join the same” is verified as cardinality only: neither translated endpoints nor layer/length equality is asserted. The named half-degree example actually uses 0.4 degrees (`:28`), so it is not an exact angular-boundary check. The excluded gap rule is not re-claimed. |
| `OurOwnOutputIsNeverTheYardstickTests` | Covers exactly the three preferred-path fixtures in WHAT THIS COVERS: generated marker, columnless shell, engineer-labelled column. The fallback is expressly excluded and the title's “never” should be read with that scope. This does not establish provenance for arbitrary renamed output. |
| `AModelIsTheSameInInchesAndMillimetresTests` | Checks counts, dimension multiset, pier count, XY joint multiset and projected connectivity. Findings 5–6 qualify the broader position/connectivity and per-section claims. Four-decimal inch precision is correctly stated. |
| `ATurnedWallAndASecondCopyAreSeen` | Asserts one lost/gained axis-aligned wall and one gained duplicate column/wall, with no unexpected column change in the rotation fixture. It does not test alternative wall orientations, competing matches, or plate equality in these variants. Findings 1 and 4 are outside its examples. |
| `TheYardsticksFrameIsJudgedBySupportAndNeverByTheOrderOfOurColumns` | Checks two orders of one equal-support/equal-spread fixture and the smaller move. Finding 12 identifies the missing support-selection and spread-selection cases. |

No implementation changes or fixes were made. The twelve numbered findings stop before the requested cap; the remaining requested checks above distinguish confirmed behaviour, disclosed limitations and evidence that the allowed source regions cannot establish.
