# Codex review — step 83b: a pair whose band holds a wall already read is the void between two walls; the nearer partner

Commit `4f194e8b` on `develop`. **Reasoning: low. Open ONLY the lines named; no grep, no listing, no build, no
test run. Answer in at most 20 lines.**

## Read (about 8 KB)
1. `Kor.Operations.EngineeringTools.Core/Dxf/WallOutlineDecomposer.cs` lines 20–75 — the `Decompose` overloads
   (standing walls, other faces); lines 270–330 — `AFaceLiesBetween` (a parallel face on the partner's side,
   at least WallFloor from face i and less than the separation minus WallFloor, overlapping by
   MinPanelOverlap) and `AWallStandsInside` (a wall already read stands in the band).
2. `Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs` lines 890–935 — where `drawnFaces` is
   built from the open chains' and loops' edges and passed in; and lines 2375–2390 — the `PairOpenFaces`
   refusal that also asks `AWallStandsInside`.
3. `Kor.Operations.EngineeringTools.Core.Tests/Dxf/TheGapThatClosesAnOpenChainIsNotAFaceTests.cs` — the two
   tests `APairWhoseBandHoldsAWallAlreadyReadIsTheVoidBetweenTwoWalls` and
   `AFaceWithANearerPartnerInAnotherChainIsNotPairedAcrossIt` only.

## The rule under review
An open chain's face is not paired with a face across a band when a third drawn face lies between them on the
partner's side (nearer partner), or when a wall already read stands in the band.

## The one question
Name ONE real wall this rule now loses: two faces of ONE wall with a third line between them that is not a
wall face — a hatch line, a centreline, a door swing's arc tessellated into short segments, a dimension
line — such that `AFaceLiesBetween` is true and the wall is refused. State which of its conditions (parallel
within ParallelDot, on the partner's side, ≥ WallFloor from face i, < separation − WallFloor, overlap ≥
MinPanelOverlap) a centreline or hatch line would satisfy, and which one condition added would refuse the
hatch/centreline and keep the void rule.

## Not asked
The DXF reader; the composer; the coverage ratchets; anything outside the lines named.
