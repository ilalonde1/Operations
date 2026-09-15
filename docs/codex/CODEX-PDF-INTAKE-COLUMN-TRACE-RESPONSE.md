# Column trace — response

## Sites and branch names

There are **four** `result.Columns.Add(` sites in `StructuralPlanClassifier.cs` at the starting `develop` HEAD, `470d85ef`.

| Member / site | Branch | Recorded source |
|---|---|---|
| `Classify`, conversion of an unjoined short wall axis | `standalone-stub` | Wall layer, loop index `-1`, wall axis length and thickness. |
| `AddColumn`, column-layer footprint (both round and rectangular arms) | `column-layer-loop` | Source loop layer/index and its fitted oriented box, before round-column sizing. |
| `AddWallOrColumn`, short solid wall-layer footprint | `short-wall-layer-loop` | Source loop layer/index and its fitted oriented box. |
| `AddWallOrColumn`, final small footprint when nothing paired up | `nothing-paired-up` | Source loop layer/index and its fitted oriented box. |

There is no separate declared-size column-add site in this checkout, so no `declared-size` branch was invented. The additional `Columns.AddRange` for supports drawn below a slab copies the recursively classified columns with `with { FromBelow = true }`, which retains Origin.

`ColumnOrigin` contains exactly Layer, LoopIndex, LoopLength, LoopThickness and Branch. Unassigned columns default to `unknown`. Loop indices are zero-based in the actual classification pass's wall/column loop traversal, after concentric-ring pairing; rejected loops also consume indices. They are not indices into the independently area-sorted `--walls` display. A recursive below-slab classification has its own traversal. Stub origins deliberately have no loop index.

## Code and tests

- `Kor.Operations.EngineeringTools.Core/Dxf/DxfModels.cs`: added `ColumnOrigin` and init-only `ColumnFootprint.Origin`. Explicit column equality/hash behavior excludes Origin and retains the pre-existing geometric fields and flags; `JsonIgnore` keeps Origin out of serialized model data. Record copies retain it.
- `Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs`: assigned indices to the loops actually classified, passed them to `AddColumn` / `AddWallOrColumn`, and attached origins at all four add sites. Classification conditions, dimensions and geometry additions remain the same.
- `Kor.Operations.EngineeringTools.Core/Dxf/LoopGeometry.cs`: added `WallContainsPoint`, using `DistanceToSegment <= Thickness / 2`, including the boundary and clamping to the finite segment's ends.
- `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/DxfInspectVerb.cs`: added `--columns`; each line prints centre, footprint size, source layer, loop index, source box length/thickness, branch, and the length/thickness of every containing wall panel. Prints an unknown-origin count. `InspectColumns` and `WriteColumns` share the listing with the corpus command. Dimensions and coordinates use the drawing's units, with classification options scaled from its DXF unit declaration as in the existing inspection code.
- `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/CorpusQueryVerb.cs`: added `columns <job>`, per-view listings and a branch-by-containment count table. Each column counts once even if several wall panels contain it. Also prints `wall-origin columns inside a wall`, unknown origins and failed sheets. Counts describe per-view classified footprints before storey replication, not final model placements. A failed sheet makes the summary explicitly partial and the exit code nonzero.
- `Kor.Operations.EngineeringTools.Core.Tests/Dxf/ColumnsSayWhatMadeThemTests.cs`: four classification rows covering all add sites, including the three requested dimensions; five segment-containment boundary rows; and a metadata test for default unknown, equality/hash, JSON and record copying. The class summary states coverage, exclusions and faults it cannot catch.
- This response file.

The classification fixtures use hand-made segments in millimetres. They set a 500 mm panel-overlap floor to isolate the unmatched 203 x 398 loop, disable wall connection/floor recovery, and cap wall thickness only in the extra short-wall-branch case. They do **not** establish the production-default branch of 31162's actual clipped fill. That is what the new inspection output is intended to measure.

## Work-folder lookup

Uses the work-folder convention supplied in the session: `<root>/<job>/dxf`, where root is `--ledger <dir>` when given, otherwise `Path.Combine(DrawingMirror.Root, "corpus")`. If the existing `--ledger <sets.csv>` form is used, its parent directory is the root. For 31162-01 with the default mirror this resolves to `%LOCALAPPDATA%/Temp/kor-drawings/corpus/31162-01/dxf`.

The command enumerates only that folder's top-level DXF views. It does not need ledger CSV files to be present and reports a missing or empty work folder with a nonzero exit code. No cache or drawing was opened in this session.

## Verification and remaining acceptance

Read the requested files in order, counted and reviewed every add site, read the edits back, and checked the scoped diff for whitespace errors. Tests were written before the production edits. No build, test execution, drawing-file access, database access or network access was performed.

The fast suite, six-set byte-identical gate and both real 31162 inspection commands remain **unrun**, per the task. Zero `unknown` is established by assignment at each source add site, not yet by observed corpus output. Provenance is never used to decide classification; containment and branch grouping run only for diagnostics. Byte-identical final composition still requires Claude's acceptance gate.

Only the seven files listed above were changed for this task. Existing unrelated edits, including the preceding small-job-title work, were left in place. `GeometryFilterService.cs`, `Baselines/` and `PlanLoopBuilder.cs` were untouched.
