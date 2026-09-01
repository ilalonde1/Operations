# CODEX-DXF-TO-ETABS-BEFORE-THIS-GOES-TO-HER Response

## Unsupported first

The ratchet reset is not supported as a per-storey member-size claim. The new counts may be real, but the current check can still credit a member for having the right section somewhere else in the same vertical stack.

I did not find the old publish staleness exclusion still open. `PublishExplainers` now limits the intentional stale-write exemptions to `PublishSummary.cs`, `PublishExplainers.cs`, and `PublishExternalTools.cs`, and the comment explicitly records that excluding `JobPublisher`, `PublishDiscovery`, and `PublishPlan` was wrong.

## Findings ranked by ship cost

### 1. Measured: stack merge can erase building/source attribution before the building cut

Cost: high. This can leave foreign building members in the shipped model, or remove wanted members, while the multiset gate stays green.

Evidence:

- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/DxfToEtabsService.cs:1641` captures `beforeStackMerge`, then `doc.MergeStackedMembers()` runs at `1642`, then the gate asserts only the member-storey multiset at `1643`.
- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/DxfToEtabsService.cs:1698` to `1713` computes the later tower/building removal set from `summary.BuildingOfObject`, keyed by original generated object names.
- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/DxfToEtabsService.cs:1838` calls `doc.DropObjects(going)`.
- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/E2kDocument.cs:815` to `893` merges duplicate stacked members by choosing a keeper object and rewriting `AREAASSIGN` / `LINEASSIGN` lines from absorbed object names to that keeper.
- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/E2kGeometryComposer.cs:306` to `322` records `BuildingOfObject` and `SourceSheetOfObject` as object-keyed dictionaries. The source/sheet fact exists there, before merge.

Why green missed it:

`MemberPlanStoreyMultisetPreserved` checks generated member placement, not object identity, source sheet, or building attribution. A foreign object can be absorbed into a kept object before the building cut. The later `DropObjects` call then targets the absorbed old name, but no assignments use that name anymore.

Next measurement:

Add a source/building-aware merge gate around `MergeStackedMembers`. The minimum useful fixture is two generated members with identical connectivity on the same storey, one tagged building C and one tagged A/B. Run merge, then tower/building cut, and assert the foreign assignment is gone and the wanted assignment survives. The stronger production measurement is a before/after multiset keyed by member geometry, storey, source sheet, and building tag, with any absorbed attribution explicitly remapped to the keeper before source-based cuts run.

### 2. Measured: the engineer-facing sheet ledger still counts surviving object labels, not placement facts

Cost: high. The workbook can tell her a sheet contributed fewer or more walls/columns/floors than it actually did after stack merge.

Evidence:

- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/DxfToEtabsService.cs:2074` to `2124` builds `SheetsAfterCut`.
- It groups `saved.Objects` by `SourceSheet` at `2078` to `2080`.
- It counts walls, columns, and floors with `g.Count(...)` at `2089` to `2094`.
- The comments at `2098` to `2113` correctly say sheet storeys are placement facts because one object can hold placements from several sheets after merge.
- `Kor.Operations.App/EngineeringTools/QuantityTakeoff/ModelQuestionnaire.cs:1510` to `1530` writes the "Sheets read" table from this output.

Why this matters:

The storey list was fixed to avoid the object-label trap, but the object counts still use the old model. Once `MergeStackedMembers` deletes absorbed objects, the absorbed sheets no longer get counted through `saved.Objects`, and the keeper sheet inherits the visible object identity.

Next measurement:

Compare `SheetsAfterCut` counts against assignment/source rows captured before merge, not against surviving object headers. Use a synthetic two-storey/two-source/same-position fixture where stack merge must absorb one object. The sheet ledger should still show each source sheet's placements after merge.

### 3. Measured: the size/thickness ratchet ignores storey

Cost: high-medium. It can make the reset look honest while stepped members are still wrong on specific storeys.

Evidence:

- `Kor.Operations.EngineeringTools.Core.Tests/DxfToEtabs/EngineerModelBenchmarkTests.cs:171` to `191` contains the reset comments and asserts `201/214` columns and `112/122` walls.
- The benchmark data record at `356` to `363` stores `ColumnSizes` as `(DxfPoint At, double Long, double Short, bool Round)` and `WallThicknesses` as `(DxfPoint At, double Thickness)`. There is no storey.
- `Read(...)` records all sections carried by an object into `sectionsOf` at `463` to `471`, then builds `columnSizes` at `492` to `500` and `wallThicknesses` at `503` to `511` without carrying storey.
- The comparisons at `613` to `620` and `623` to `631` match by plan position and size/thickness, not by storey.

Why green missed it:

A stepped column or wall can get credit if the expected size exists somewhere in the vertical object stack, even if the wrong section is assigned on the storey being compared.

Next measurement:

Carry storey through both expected and actual section observations and compare `(storey, plan position, section)`. Rerun the ratchet. If the score drops, the current reset is inflated and should not be treated as a proof that per-storey member sizes are correct.

### 4. Suspected: `SpanEveryGeneratedMemberOneStorey` runs after the merge gate with no runtime physical-height guard

Cost: high if triggered. It can make a surviving generated wall/column physically short after a tower cut, especially where retained storeys are not the contiguous building-C stack expected by the code.

Evidence:

- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/DxfToEtabsService.cs:1641` to `1643` runs the stack multiset gate before the later span rewrite.
- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/DxfToEtabsService.cs:1651` to `1653` calls `doc.SpanEveryGeneratedMemberOneStorey(...)` only when `TowerOnly` is set.
- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/E2kDocument.cs:721` to `738` rewrites every generated `LINE "K[CW]..."` span integer greater than 1 down to `1`.
- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/E2kDocument.cs:828` to `830` explicitly notes that stack merge does not try to preserve span counts because the ETABS export span is currently treated as less authoritative than assignment rows.

Why green may miss it:

The merge gate preserves member-storey placement, not physical height after the final tower cut and span rewrite. Slow model plausibility tests may catch some examples, but there is no runtime postcondition immediately before publish.

Next measurement:

On the exact staged building-C output, after all cuts and after `SpanEveryGeneratedMemberOneStorey`, compute generated line/area physical height from the final storey table. Fail if a generated column is shorter than the expected engineering floor height, if a wall/panel span collapses to a non-physical height, or if `span=1` refers to a next-below storey that is not part of the same retained building stack.

### 5. Suspected: seam clipping relies on a fragile anchor and has no direct clip-edge tests

Cost: medium-high. A bad seam side decision can remove or clip the wrong half of a joined sheet.

Evidence:

- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/DxfToEtabsService.cs:1021` to `1038` builds `seamClipFor` only for `TowerOnly`, joined halves, and slabs. It chooses `mine` from sheet building tags, then uses the average point of all segments in that half as the anchor.
- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/DxfToEtabsService.cs:1069` to `1078` removes members on the far side using center/midpoint side tests.
- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/LoopGeometry.cs:543` to `590` implements `ClipToSideOf`; degenerate seam lines and anchors on the seam return the original ring, and rings clipped below three points also return original.
- Tests cover match-line grouping and dominant-side behavior, but I did not find direct tests for `ClipToSideOf` edge cases such as ring entirely on one side, seam through a vertex, touching the seam, degenerate line, or anchor exactly on the line.

Why this is suspicious:

The anchor is a mean of half-sheet linework, not a semantic building-side point. Heavy annotation, title blocks, or L-shaped plan geometry can pull the mean across the seam.

Next measurement:

Add synthetic `ClipToSideOf` tests for the geometric edge cases above. On the real 31168 staged output, log chosen anchor, side, slab area before/after clipping, and member removal counts by sheet/building. Generate an overlay for any sheet where slab area changes by more than a small tolerance.

### 6. Suspected: drawn ring beats fill at `>= 60%` is still a one-sample judgement

Cost: medium. It can drop a broader legitimate fill when a smaller drawn ring overlaps enough of it.

Evidence:

- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/StructuralPlanClassifier.cs:1252` to `1284` keeps swallowed closed rings instead of a recovered fill when their area sum is at least 60 percent of the fill area.
- The local comment says the 60 percent line comes from one real distinction: a mezzanine case at 82 percent versus nosing at less than 1 percent.
- The multi-ring weld guard at `1239` to `1251` only refuses when the fill swallows more than one already-read floor. A one-bay drawn ring swallowed by a correct two-bay fill can still hit the 60 percent band and discard the broader fill.

Next measurement:

Log closed-ring/fill overlap ratios on both 31168 and 31138. Pay special attention to 60-82 percent. Add a synthetic two-bay case where one bay is already a closed ring and the correct fill spans both bays.

### 7. Suspected: `dxf.slab-chain-join-fraction = 0.10` looks honest for changed fixtures, but remains one-job calibrated

Cost: medium.

Evidence:

- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/StructuralPlanClassifier.cs:247` to `258` documents the cutoff: real recoveries at 1-8 percent and invented closures at 17-48 percent, measured on 31168.
- `Kor.Operations.EngineeringTools.Core.Tests/DxfToEtabs/TagGatedSlabRecoveryTests.cs:19` to `37` changed the fixture from a whole missing side to an interrupted side, matching the intent of the threshold.
- `Kor.Operations.EngineeringTools.Core.Tests/DxfToEtabs/PlateReadTwiceTests.cs:70` to `82` changed the inner ring to a 120 inch interruption, about 2 percent, also matching the intended "small gap" behavior.

Why this is not yet a broad proof:

The fixture edits look directionally honest, but the threshold is still documented as measured on 31168 only. Nothing I found proves the same separation on 31138.

Next measurement:

Record all chain-closure ratios on 31138, including refused candidates with slab tags inside. Compare against the current 10 percent threshold and the 36 inch interrupted-width rule.

### 8. Suspected: `GradeSuffix` is global/name-based and can silently choose the wrong material grade

Cost: medium-low unless the engineer relies on grade names for design checks.

Evidence:

- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/E2kGeometryComposer.cs:440` to `447` parses `(\d+)\s*MPa` from `doc.FindConcreteMaterial("Floor")` and `"Wall"`.
- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/E2kDocument.cs:1706` to `1751` finds concrete material names, prefers names containing the preferred word, otherwise chooses the most-used concrete material in wall/slab/frame sections, with fallback to first wall or first concrete material.

Why this is suspicious:

It handles `35MPa` and `35 MPa`, but it is still one global suffix per floor/wall role. With generic names like `C30`/`C35`, multiple floor materials, or wall material listed first while slabs use another material, the suffix can be absent or wrong without failing.

Next measurement:

Add fixtures with `30 MPa Floor` plus `35 MPa Transfer Floor`, generic `C30`/`C35`, and wall material listed before slab material. Compare generated `SHELLPROP` names and assigned material names to the source ETABS material actually used for each reused section.

### 9. Suspected: `E2kModelQuery.Sections` still reports only the first storey for multi-storey objects

Cost: lower for publish, medium for `/ask` or any engineer-facing query.

Evidence:

- `Kor.Operations.EngineeringTools.Core/E2kModelQuery.cs:79` to `101` handles `Storeys` by reading assignment rows and is row-storey aware.
- `Kor.Operations.EngineeringTools.Core/E2kModelQuery.cs:186` builds `storeysOf = doc.StoreysByObject()`.
- `Note(...)` at `192` to `199` increments use counts per assignment row but adds only `st[0]` to the storey set.
- `AreaSections` and `LineSections` are iterated at `201` to `202`; they contain row-storey information, but `Note` ignores that row storey.

Next measurement:

Add an `E2kModelQuery.Sections` fixture where one generated column object has section assignments on two storeys. The section summary should list both storeys, not only the first object-storey.

### 10. Suspected: slab edge closure borrowing wall linework is constrained, but still only proved inert on one job

Cost: lower unless it creates a false slab boundary.

Evidence:

- `Kor.Operations.EngineeringTools.Core/DxfToEtabs/StructuralPlanClassifier.cs:546` to `548` allows slab edge completion to use unroled segments plus prepared wall segments.
- The surrounding closure code requires exact endpoint continuity, which limits the blast radius.
- The brief says this changed nothing measurable on 31168, which is useful but does not prove safety on 31138 or on plans with wall segments touching slab interruptions internally.

Next measurement:

Log every borrowed wall segment used for slab closure by job and sheet. Add a fixture where a wall segment touches a loose slab-chain endpoint but lies through the slab interior, and assert it does not become an exterior slab boundary.

## Gate blind spots to close before sending

The current green suite most likely misses the expensive failures because the gates are mostly geometry/count gates, not attribution gates.

Before this goes to her, the minimum additional checks I would want are:

1. A stack-merge attribution invariant: generated source sheet and building attribution must survive merge, either as per-assignment metadata or as an explicit keeper attribution union.
2. A post-cut runtime physical-height invariant: generated walls/columns after tower cut and span rewrite must have plausible physical heights from the final storey table.
3. A per-storey section ratchet: compare engineer model section sizes by `(storey, plan position, size/thickness)`, not by plan position alone.
4. A sheet ledger check driven from assignment/source facts, not surviving object headers.

If only one fix lands before shipping, fix the attribution loss across `MergeStackedMembers` before the building cut. That is the path most likely to produce a model that opens cleanly while containing the wrong building's members.
