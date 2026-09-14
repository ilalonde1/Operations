# Codex — design + build task: rings from loose segments, the same rings in any arrival order

**Scope: this repository only, the files named below. Write a design note, code and its tests. No
`dotnet build`, no `dotnet test`, no drawing files, no database, no network.** Reading set 58 KB measured
with `wc -c`. Recommended reasoning: high — this is a design problem with one wrong answer already
measured. Work on the tree as it stands (HEAD `af7b7821` on `develop` plus the uncommitted step-63 edits in the working tree — leave those files as they are); Claude wires the class in, runs the
build, the differentials and the six-set gate afterwards and reports back.

## The problem, measured

`PlanLoopBuilder.Build` (`Kor.Operations.EngineeringTools.Core/Dxf/PlanLoopBuilder.cs`, 17.6 KB) stitches
a plan's loose LINE segments into closed rings — every wall outline, column outline and slab edge the
composer reads comes through it, on the DXF route and on the PDF route (`GeometryFilterService` calls it
for slab edges; `StructuralPlanClassifier` for walls, columns, slabs and partitions). It is seeded in
ARRIVAL order and its nodes are numbered by it, so the same drawings with their entities reversed build
other walls on every one of the six harness sets
(`Kor.Operations.EngineeringTools.Core.Tests/Intake/TheSameDrawingsInAnotherOrderBuildTheSameStructureTests.cs`,
9.4 KB, skipped RED with the numbers: 31168 473 walls lost / 241 gained; 31065 134 / 161; 31130 76 / 54).

The one thing already tried is in the comment at the top of `Build` and in `docs/PdfIntake.md` §70: a
canonical geometric sort of the segments (each from its lesser end, sorted by that end then the other).
It moved every set's walls against the bank, was still not the same under reversal on 31065, and moved a
plate under the page-shift differential (`TheSameDrawingsShiftedOnThePageBuildTheSameStructureTests.cs`,
9.2 KB, GREEN and must stay green) — an order keyed on floating coordinates is the frame class of §63 by
another door: shift the drawing on the page and the sort order changes.

`docs/PdfIntake.md` §71 (4.8 KB, lines 3573–3640) records where entity order enters `Build`: the seed
order and global edge consumption first; then node creation and numbering (the first coordinate met is a
node's representative, which can change the partition itself); then adjacency insertion order at a
junction; then `PickContinuation`'s tie; then `BridgeChains` on an already order-dependent chain list.

## What is wanted

One rule, stated in a sentence, for which ring an edge belongs to — such that the SET of rings a plan
builds (as point sets, to the join tolerance) is the same for every permutation of its segments, and does
not depend on where the drawing sits on the page. Not a sort. Not a tie-break on coordinates.

The direction we believe is right, and want you to confirm or refute from the code: a plan's segments form
a planar graph; the rings are its bounded faces. Half-edge face enumeration gives every bounded face exactly
once, and a shared edge belongs to BOTH faces on either side of it — the "ownership" question dissolves.
Order enters such an enumeration nowhere except through (a) node equivalence and (b) the order the face
LIST comes out in, which no consumer may depend on (check that: `StructuralPlanClassifier` lines 540–720
and 1940–1975, `GeometryFilterService.cs` 1430–1500 — say for each consumer whether it reads the loops as
a set or walks them in order).

What the design must decide, each with its reason:

1. **Node equivalence.** Today: nearest existing node within the join tolerance by distance, ties to the
   earlier node (arrival). Order-independent alternatives: transitive closure of "within tolerance" by
   union-find (the partition is then the same in any order; its representative point is the cluster's
   centroid, and say why that is not the frame class — a centroid moves WITH the page, a sort key does
   not); or the same with a cap on cluster diameter. State the failure each has (a chain of endpoints
   each within tolerance of the next can close a cluster wider than the tolerance).
2. **Faces.** At each node, half-edges sorted by angle; the face walk takes the next half-edge around the
   node in a fixed rotational sense. The unbounded face is dropped by signed area. Dangling edges
   (degree-1 nodes, pruned iteratively) are the open chains `BridgeChains` bridges today — say whether
   `BridgeChains` can run BEFORE the face walk on the pruned set (bridge, then enumerate) so that it, too,
   sees no order.
3. **The straightest path is gone — say what replaces it.** `PickContinuation` follows the straightest
   continuation at a junction so a slab outline keeps to its own edge instead of turning down linework
   that touches it. Face enumeration turns that slab into several cells wherever linework crosses it.
   Design the recovery: which cells are a WALL band (thickness between the floor and the cap — those the
   classifier wants as they are), and how the remaining cells reunite into the slab outline (union across
   every edge that is not a band edge). Say which consumer needs which: `WallOutlineDecomposer` wants
   ribbons; the slab reader wants the outer outline and the holes; the column reader wants small closed
   rectangles.
4. **What becomes of consumed edges when a walk fails.** In a face enumeration nothing fails — every
   half-edge is in exactly one face — so this question should have no answer left. Confirm.

## What to build

- `Kor.Operations.EngineeringTools.Core/Dxf/PlanarRings.cs` — a NEW class, not wired in: the same
  contract as `PlanLoopBuilder` (`Build(IEnumerable<DxfSegment>) → Result(Loops, OpenChains)`), the same
  constructor parameters, so Claude can swap it behind `PlanClassificationOptions` and run the
  differentials. Reuse `LoopGeometry.Within/Beyond` (to the micron, `LoopGeometry.cs` lines 70–85) and
  `LoopGeometry.Simplify` (line 267); do not touch `PlanLoopBuilder.cs`.
- `Kor.Operations.EngineeringTools.Core.Tests/Dxf/PlanarRingsBuildTheSameRingsInAnyOrderTests.cs` —
  fixtures small enough to draw in a comment: a square with a diagonal (two triangles, and BOTH own the
  diagonal); two squares sharing an edge; a slab rectangle crossed by a wall band (the band as a ribbon,
  the slab as one outline after the cell union); a dangling edge (an open chain); a square whose corner
  is drawn twice, 0.04 apart (one node at tolerance 0.05); and each fixture reversed and shuffled by a
  seeded permutation (`new Random(7)`, several seeds) asserting the ring SET is identical, then the same
  fixture translated by (5000, 3000) asserting the rings moved by exactly that. The class summary states
  WHAT THIS COVERS and WHAT IT DOES NOT (rule 11 of `CLAUDE.md`), and names one same-class fault it would
  not catch.
- `docs/codex/CODEX-PDF-INTAKE-RING-OWNERSHIP-RESPONSE.md` — the design note: the rule in one sentence;
  the four decisions above with their reasons; per consumer, what changes; what the bank will likely do
  (walls that were one ring now two, and why that is right or wrong); and anything you could not do within
  the rules.

## Read, in this order

1. `CLAUDE.md` rules 5, 7, 10, 11 (about 4 KB of 15 KB).
2. `PlanLoopBuilder.cs` — whole (17.6 KB).
3. `TheSameDrawingsInAnotherOrderBuildTheSameStructureTests.cs` — whole (9.4 KB): what the differential
   compares and does not.
4. `docs/PdfIntake.md` §70 (lines 3504–3572, 7.3 KB) and §71 (3573–3640, 4.8 KB).
5. `LoopGeometry.cs` lines 70–85 and 260–300 (about 3 KB of 28): `Within`, `Beyond`, `Simplify`;
   `DxfModels.cs` lines 80–105 (1 KB): `PlanLoop.SignedArea` and `Area`.
6. `StructuralPlanClassifier.cs` lines 540–720 and 1940–1975 (about 11 KB of 168): the consumers.
7. `GeometryFilterService.cs` lines 1430–1500 (about 4 KB of 119): the PDF side's slab-edge consumer.

## Rules for the change

- Warnings are errors repo-wide; xUnit analyzers are on (`Assert.Equal(expected, actual)`).
- Rule 7: no regex and no Windows path through a non-raw string. Rule 5: none applies — you write no file
  you then read back — but every number in the response comes from the code you read, not from memory.
- No coordinate sort anywhere in `PlanarRings` — if you find you need one to break a tie, the tie is the
  finding: write it in the response and leave the tie to geometry (angle, area), never to X and Y.
- Do not touch `PlanLoopBuilder.cs`, `StructuralPlanClassifier.cs`, `GeometryFilterService.cs`,
  `Baselines/`, or any existing test.

## Acceptance (Claude runs; you do not)

The new test class green; then `PlanarRings` swapped in for `PlanLoopBuilder` behind an option and
`dotnet test --filter FullyQualifiedName~TheSameDrawingsInAnotherOrder` green on all six sets; the shifted
differential still green; the six-set gate's diffs each explained by the design note's "what the bank
will likely do". Any set whose walls the swap loses without an explanation sends the design back.
