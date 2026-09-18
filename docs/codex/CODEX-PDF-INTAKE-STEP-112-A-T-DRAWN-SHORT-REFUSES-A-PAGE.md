# Codex — step 112: a T drawn short closes 30838's plans and makes RecoverSurfaces refuse 31065's P1

Branch `step-112` (`78ae4a39`) holds the rule; `develop` does not. **Reasoning: low. Open ONLY the lines named; no
grep, no listing, no corpus run. Build and run ONLY the two test filters named. Answer in at most 25 lines: the cause
(one sentence), what you changed, file and line, and the tests' names.**

## The rule (already written, on the branch)
`Kor.Operations.EngineeringTools.Core/Dxf/PlanarRings.cs`, `Bridges` — after the end-to-end proposals: an end of
degree one whose own forward ray meets another edge's interior (`t` in 0.01–0.99) within `_bridgeTolerance` (6 in on
the slab pass) and beyond `3 × _joinTolerance` is carried to that foot; the edge is split at the foot (two spans
replace it; `split` holds its index and `Finish` leaves it out); one carry per edge (the cheapest); the end's unique
cheapest proposal decides; a carry that crosses another edge is refused. `Result.Carries` lists them.

## The defect
On 31065-01's sheet S2.03.1.1 (P1 north, concrete outline) the page's `Build` succeeds with eight carries (the slab-
pass trace lists them: `(79.7,141.8)->(79.5,141.8) (172.1,103.9)->(171.9,103.9) (95.9,141.2)->(95.9,141.1)
(26.7,74.8)->(26.9,74.8) (84.0,189.4)->(84.0,189.8) (132.7,76.1)->(132.6,76.1) (37.3,187.0)->(37.3,187.3)
(211.4,63.9)->(211.8,63.9)` in feet), and then `RecoverSurfaces` throws `InvalidOperationException("Face successor is
not a permutation.")` from `Walk` over the boundary mask (`PlanarRings.cs` ~line 70–75: `boundary[h] = Filled(h) &&
!Filled(h ^ 1)`), so the page loses every ring it had (the 9,143 sq ft P1 plate, banked on develop). Without the
carries the page walks. Three pages of 31065 and one of 30838 refuse this way; 30838's L20 (the set the rule was
written for) closes correctly under it.

Two forms before this one failed the same way: the perpendicular foot (24 carries on this page, many under an inch)
and the same with the split at the foot. So the fault is not the foot's position alone.

## Read (about 8 KB)
1. `PlanarRings.cs` lines 105–135 (`Build`, `Finish`), 259–350 (`Bridges`: the end-end proposals, then the carry block
   and the agreement), 400–430 (`Walk` and the permutation check), 60–76 (`RecoverSurfaces`).
2. `Kor.Operations.EngineeringTools.Core.Tests/Dxf/PlanarRingsBuildTheSameRingsInAnyOrderTests.cs` — the fixture style
   for arrangements; put the new tests in a new file beside it, `ATDrawnShortIsCarriedToTheEdgeTests.cs`.

## What to find and fix
Why a carry (an inserted span from a degree-one end to a point on another edge's interior, with that edge split there)
leaves the mesh's successor structure non-permutational for the boundary walk when it walks for `Build`. Likely
shapes: (a) a split half of zero or sub-tolerance length when the foot falls within `tolerance` of the edge's own
endpoint after `Arrange` re-clusters points (the `t` bounds are 0.01–0.99 of the edge, not a length); (b) a carry
whose end `p` was ALSO given an end-to-end bridge by `unique` (the choice is by cost; check that a carried end cannot
also appear in an agreed end-end proposal); (c) the split edge's two halves both `Inserted = ins` while the original
edge was a merged duplicate (`edgeIndex` at line ~237 ands `Inserted` over duplicates), and `Cut`/`Owner` treat
inserted edges differently in the boundary mask. Write the invariant the mesh needs as ONE sentence in a comment above
the carry block, then fix it there (a length floor on the split halves, or refusing the carry when its foot lies within
`tolerance` of a vertex, or excluding the carried end from the end-end agreement) — the smallest change that makes
the invariant hold.

## Tests
1. In the new test file: a T drawn short — three spans making a U open at the top-left, with the fourth
   span stopping 4 in short of the left span's middle: one face after `Build`, and `RecoverSurfaces(_ => false,
   (_, _) => true)` returns one slab. Then the same with the short end 2 mm from a vertex of the span it runs into,
   and with two ends carried onto the same span: still one slab, no exception.
2. Run: `dotnet test Kor.Operations.EngineeringTools.Core.Tests --filter "FullyQualifiedName~PlanarRings"` and
   `--filter "Speed!=Slow"`. Do not run the six-set gate; it is the next step here, not yours.

Summary lines on the new test: WHAT IT COVERS (the three shapes) and WHAT IT DOES NOT (the real sheet; a carry the
agreement refuses).

## Outcome (2026-09-17 17:40, after Codex's run)

Codex delivered shape (a): the foot within the join tolerance of the target edge's end is refused, with three tests,
and said plainly the guard was unproven on the sheet — it was not the sheet's fault. The exception, made to name the
vertex and the owners of the colliding half-edges, read: *"at (57705,24663) two boundary half-edges (owners 40/none and
39/none, twins) share a successor"* — a face whose walk passes one vertex twice (a box inside a floor with a corner ON
the floor's edge), split at the pinch into the face's ring and the hole's ring, the hole's ring owned by nobody because
`Regions` reserved same-component containment for the outside's rings. develop refused two pages of 31065 the same way
WITHOUT the carries (p7, p12); the carries moved which pages hit it. Fixed in `Regions`: a ring split from a walk that
also made a positive ring holding it is that ring's hole (`AHoleTouchingItsFaceAtAVertexIsItsHoleTests`, proved by
breaking). 31065: 71 of 71 pages arrange; P1 north reads its 11,442 sq ft floor. Codex's guard and tests kept.
