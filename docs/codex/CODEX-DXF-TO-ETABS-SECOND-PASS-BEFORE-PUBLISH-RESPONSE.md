# CODEX-DXF-TO-ETABS-SECOND-PASS-BEFORE-PUBLISH - RESPONSE

Scope obeyed: no build, no tests, no publish, no share access, no commit.

## Bottom line

The merge is now in the right place. I did not find a downstream publish reader that still needs
pre-merge object identity and is reading the post-merge side by mistake.

I did find one publish-cost risk in the tower-only span rule, and one same-class reader bug outside
the publisher. The stack-merge harness is useful, but its name now overclaims: it proves a specific
placement/count/ledger invariant for the site model, not "everything the engineer can see" and not
the tower-C deliverable.

## 1. Measured - tower-only span reset can shorten columns when surviving C storeys are not adjacent

Cost to ship: high, because this can put a generated column between the wrong two floors while still
looking like a normal-height member.

Files:
- `Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs:1899-1936`
- `Kor.Operations.EngineeringTools.Core/Dxf/E2kDocument.cs:721-738`
- `Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs:744-750`
- `Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs:793-865`
- `Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs:927-1074`
- `Kor.Operations.EngineeringTools.Core.Tests/StoreyCutTests.cs:20-39`
- `Kor.Operations.EngineeringTools.Core.Tests/StoreyCutTests.cs:183-222`
- `Kor.Operations.EngineeringTools.Core.Tests/ModelIntegrityTests.cs:449-561`
- `Kor.Operations.EngineeringTools.Core.Tests/StackMergeChangesNothingButLabelsTests.cs:50-64`

The constructible case is already in the tests' 31168-shaped fixture: `C-ROOF`, then unprefixed
`LEVEL 10`, then `C-LEVEL 9`. `KeepStoreysUpTo("C-ROOF")` keeps both `LEVEL 10` and `C-LEVEL 9`;
only explicit `DropStoreys(["LEVEL 10"])` removes the foreign unprefixed level.

The composer sets a generated column's span to the count of global storeys it crosses. A C member
assigned to `C-ROOF` and rising from `C-LEVEL 9` needs span 2 while `LEVEL 10` remains in the storey
list. `SpanEveryGeneratedMemberOneStorey()` then rewrites generated `LINE "KC..." COLUMN ... 2` to
span 1 whenever `TowerOnly` is set. In that surviving-list shape, span 1 means the column is between
`LEVEL 10` and `C-ROOF`, not between `C-LEVEL 9` and `C-ROOF`.

The current gates do not catch that:
- `StackMergeChangesNothingButLabelsTests` never sets `TowerOnly`, `TopStorey`, or `DropStoreys`.
  It builds the site model only.
- `ModelIntegrityTests.ConnectivityFlagsMatchTheFormsAnEngineersModelUses` rejects spans that are
  too large across a same-building floor; it cannot reject a span that was made too small.
- `ModelPlausibilityTests.NoColumnIsShorterThanAPerson` only rejects very short columns. If
  `LEVEL 10` to `C-ROOF` is 120 in, the wrong member is plausible and passes.

What I would measure next: add a no-share fixture with the exact storey order
`C-ROOF / LEVEL 10 / C-LEVEL 9`, a `KC` assigned to `C-ROOF` with span 2, then run the tower-only
post-cut path. Assert either that `LEVEL 10` must be dropped before unspanning, or that the column
keeps the span needed to reach the nearest retained C/shared floor below. Then add a tower-C mode to
the merge harness.

## 2. Measured - the span reset says "walls and columns", but only changes columns

Cost to ship: high if the tower-only policy is correct, because walls can keep multi-storey area
flags through a tower-only cut; medium if the policy is withdrawn/replaced by the fix above.

Files:
- `Kor.Operations.EngineeringTools.Core/Dxf/E2kDocument.cs:721-738`
- `Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs:976-1003`
- `Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs:1911-1921`

`SpanEveryGeneratedMemberOneStorey()` matches only `LINE` connectivity rows:
`LINE "K[CW]..." ... span`. Generated walls are not `LINE` rows. They are `AREA "KW..." PANEL`
rows with the span in the first two panel flags, written by the composer at `E2kGeometryComposer.cs:976-981`.

So the implementation can reset `KC` columns, but it cannot reset `KW` wall panels. The service
warning says it broke "generated column and wall object(s) into one-storey spans", which is false
for walls.

What I would measure next: add a fixture containing `AREA "KW1" PANEL ... 2 2 0 0` with an
`AREAASSIGN` on a tower storey, call the same tower-only post-cut step, and assert the intended
policy. If tower-only members must all become one-storey ETABS objects, wall panel flags must become
`1 1 0 0`; if that is not the real policy, remove the blanket reset and make the span calculation
own-building-aware instead.

## 3. Measured - `e2k-ask openings` still uses the first storey of an object, not the row storey

Cost to ship: medium. It is engineer-facing query output, not the published geometry, but it is the
same class as the fixed `Sections` bug and it can mislead an engineer asking where openings are.

Files:
- `Kor.Operations.EngineeringTools.Core/E2kModelQuery.cs:160-178`
- `Kor.Operations.EngineeringTools.Core/E2kModelQuery.cs:194-207`
- `Kor.Operations.EngineeringTools.Core/Dxf/ShippedModelInvariants.cs:387-401`

`Openings()` iterates `foreach (var (obj, storeyOfRow, _, isOpening) in AreaSections(doc))`, then
ignores `storeyOfRow` and reports `StoreysByObject()[obj][0]`. That is the exact shape just fixed in
`Sections()`, whose comment says the row storey is the only honest value once one object has
assigns on several storeys.

This is not hypothetical for ETABS-style openings. The invariant comments call out engineer models
with many opening assigns from far fewer opening objects.

What I would measure next: add an `E2kModelQueryTests` fixture with one opening object assigned on
two storeys. `Openings()` should return both row storeys. The fix should be the same as `Sections()`:
use `storeyOfRow`.

## 4. Suspected - stack merge harness does not cover the properties its name claims

Cost to ship: medium-to-high as a regression blind spot. The harness caught the sheet ledger fault
and protects a real invariant, but it would not have caught the current tower-only span issue and it
does not compare several properties the engineer can see.

Files:
- `Kor.Operations.EngineeringTools.Core.Tests/StackMergeChangesNothingButLabelsTests.cs:50-64`
- `Kor.Operations.EngineeringTools.Core.Tests/StackMergeChangesNothingButLabelsTests.cs:73-101`
- `Kor.Operations.EngineeringTools.Core.Tests/StackMergeChangesNothingButLabelsTests.cs:117-153`
- `CLAUDE.md:157-164`

The harness builds merge on/off for the same site job. Common-mode defects are invisible by design.
It also compares only generated assignment placement keyed by `(kind, plan points, storey, section)`,
then report summary counts, foreign-cut count, and sheet placement.

Missing from the harness:
- connectivity span/height
- section definitions behind a section name: thickness, material, frame area, frame dimensions
- area opening flag
- pier/spandrel labels
- diaphragm/mesh flags
- generated point count and orphaned-point shape
- workbook text and one-page summary text
- any `--tower C` deliverable

The property most worth adding is span/height, because it is both deliverable-shaping and was the
root of prior wafer failures. The next one is full area/line assignment signature, because section
name equality does not prove material/thickness equality.

What I would measure next: extend the harness into two layers. Keep the current fast placement
diff, then add a delivered-artifact diff that parses generated connectivity plus assignments into
`(kind, points, storey, section, material/thickness-or-frame-size, opening, pier/spandrel,
mesh/diaphragm, span)`. Run it for the site model and for the publish plans, especially tower C.
Also read the questionnaire workbook and summary page text, since the current test only reads the
report.

## 5. Low - quantity-takeoff UI calls assignment rows "objects"

Cost to ship: low. It is not a model defect, but it is the same vocabulary trap: after stack merge,
one object label can carry many assignment rows.

Files:
- `Kor.Operations.EngineeringTools.Core/E2kQuantityTakeoff.cs:22-29`
- `Kor.Operations.EngineeringTools.Core/E2kQuantityTakeoff.cs:152-336`
- `Kor.Operations.App/EngineeringTools/StructuralTakeoff/StructuralQuantityTakeoffWindow.xaml.cs:143`

`E2kQuantityTakeoff.Read()` increments `read` for each priced area/line assignment row. The result
field is named `ObjectsRead`, and the WPF status displays it as "`N objects`". In a merged model
that number is member/assignment rows priced, not ETABS object labels.

What I would measure next: rename the result field/display to `MembersRead` or `AssignmentsRead`,
or compute a distinct object-label count separately if the UI really wants objects.

## Direct answers to the brief

Merge placement: right now. The publish path takes `provenance = doc.ReadContents(...)` before the
merge, then merges, then drops empty generated objects/orphan points and takes `saved =
doc.ReadContents(...)` after the merge. That split matches the two consumers: the sheet ledger needs
pre-merge source identity, and the delivered counts/model questions need the post-merge file.

`summary`, `provenance`, `saved`: the split is right. I did not find a downstream object-identity
reader that needs the pre-merge names and currently reads `saved` instead.

Ratchet reset: the reset looks honest on the evidence in code. The per-storey comparison is the
right denominator for a storey-driven generator. I did not find a remaining code path that credits a
global object count as if it were per-storey coverage.

Rule 11: sound rule, real failure mode. "On the second instance, name the class and build the
check" is exactly right for this codebase. The failure mode is naming the class broader than the
check. Here, "everything the engineer can see" became a narrower site-model placement/count/ledger
diff. Rule 11 should require the check to state its covered properties and its explicit omissions,
and at least one example of a same-class fault it would not catch.

## Notes on non-findings

I did not reopen slab thickness zones or `LEVEL P1 MEZZ`; both are explicitly out of scope for this
gate.

I also did not re-report the first-pass merge-order, sheet-ledger, or per-storey ratchet findings.
The current code addresses those specific faults.
