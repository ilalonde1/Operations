Applied.

What changed:

- Added a finished-file readback snapshot (`E2kModelContents`) in `E2kDocument`: storeys, generated walls/columns/floors, generated joints, per-storey member/plate counts, object source sheets, referenced joints, and orphan generated joints.
- Pruned unreferenced generated `KP...` joints after cut-away generated objects are removed, with reference-model point names exempted.
- Recounted `DxfToEtabsReport` from the saved model snapshot and rebuilt the sheet table from objects that survived the cut, so placed sheets, columns and slabs no longer come from pre-cut outcomes.
- Split floor-gap reporting into real no-plate storeys, mostly-uncovered storeys, and unsupported plates. The no-plate row now says "no floor plate at all"; the partial-coverage row is separate and measured after cuts.
- Filtered workbook S7 against saved-model storeys and removed the fabricated J1 "6, 12 and 18 inches" tolerance claim.
- Extended `ShippedModelInvariants` to block on report count drift, report/workbook storey names absent from the `.e2k`, and orphan generated joints. `verify-e2k`, `JobPublisher`, and `tools/Publish-EtabsModel.ps1` now pass report/workbook text into that gate.
- Added focused core tests for saved-model pruning/readback, floor-gap splitting, report/workbook invariant failures, and S7 saved-storey filtering.

Per the audit instruction, I did not run `dotnet build` or `dotnet test`, and I did not touch the 31168 job share.
