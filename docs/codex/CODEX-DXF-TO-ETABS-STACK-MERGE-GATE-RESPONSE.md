# CODEX-DXF-TO-ETABS-STACK-MERGE-GATE - RESPONSE

The multiset is the right invariant for this step. It is not sufficient to prove that the final names are the names Andrea wants, but it is sufficient to prove the stack merge did not add, drop, or move any assigned member while reducing object count.

## Shape Applied

Added `MemberPlanStoreyMultisetPreserved` in `Kor.Operations.EngineeringTools.Core/Dxf/MemberPlanStoreyMultisetPreserved.cs`.

It has three use shapes:

- `Capture(E2kDocument)` snapshots a live in-memory document before a transform.
- `Assert(Snapshot, E2kDocument)` is the runtime guard to place around the future stack merge.
- `Compare(string beforePath, string afterPath)` / `Assert(string beforePath, string afterPath)` compares two finished `.e2k` files.

The key is:

`(Kind, canonical exact plan position, Storey)`

Object name is not part of the key. Section is not part of the key. Multiplicity is counted in a dictionary, so two members at one position on one storey remain two members.

The failure message starts with a hard refusal sentence and then reports affected storey/kind groups, for example:

`LEVEL 2: columns 36 -> 60; lost (...); gained (...)`

It caps output to eight storey/kind groups and three lost/gained example positions per group.

## Decision 1 - Position Tolerance

I chose exact coordinate equality, not rounded buckets.

Reason: this gate is for a rename-only in-memory transform. A rename-only transform has no authority to move point coordinates or change connectivities. If any coordinate or connectivity changes, that movement is itself a defect and should fail. The prior `overlap.py` uses 0.1 inch rounding for diagnosis on finished models, but this gate is stricter because it guards code that should not touch geometry at all.

The implementation still normalizes numeric text through parsed `double` values and formats them with invariant `G17`, so `1` and `1.0` compare as the same coordinate after parsing. It does not allow a 0.001 inch drift; the test `MovedMemberFails` proves that.

## Decision 2 - Wall Identity

A position made from multiple plan points is canonicalized across both cyclic rotations and reversed order. That means `(A,B)` and `(B,A)` compare equal for line members, and an area/panel footprint with reversed point order also compares equal.

This is in `PositionKey` / `Rotations` in the new gate. The test `WallFootprintEndpointOrderIsNormalised` covers the area-panel case.

## Tests Added

Added `Kor.Operations.EngineeringTools.Core.Tests/MemberPlanStoreyMultisetPreservedTests.cs`.

The tests cover:

- rename-only object-name change passes
- added assign fails and names the storey
- dropped assign fails
- moved plan position fails
- section change up a stack passes
- object count falling `3 -> 1` with identical assignments passes
- wall/panel reversed footprint order passes
- finished files can be compared

I did not run `dotnet build` or `dotnet test`, per the brief. I did run `git diff --check`; it was clean.

## Stashed Attempt Observation

I read `stash@{1}` without applying it. The attempt added `doc.MergeStackedMembers()` immediately after `RenameStoreysInAssigns()` in `DxfToEtabsService.cs`, then continued into `DropMembersDuplicatedOnOneFloor()`.

Inside `MergeStackedMembers`, it grouped generated columns and panels by raw connectivity/joints, removed later connectivity rows, and rewrote assigns to the first object at that connectivity. There was no before/after `(kind, plan position, storey)` multiset assertion around that rewrite.

That suggests why `C LEVEL 2` could go `36 -> 60`: the merge operated on object connectivity identity and assign rewrites without a differential guard proving the storey-level placement multiset stayed constant after the cut/rename context. The new gate is designed to fail exactly there on the first run.
