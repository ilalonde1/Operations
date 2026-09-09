# Codex 24 — intake claims against implementation

## Findings

Static audit only. No build, test, drawing read, database, UNC access or other repository was used. Counterexamples below are deductions from source, not executed reproductions. “Stays green” means the existing assertions do not exercise the counterexample; it does not report a test run.

References use the working tree at `e7738043`. The requested historical endpoint is `40f152cd` (step 9a). The subsequent diff adds storeys, the census harness and documentation; the implementations underlying findings F1–F11 below were already present at step 9a. Question 7 inventories **all current** Intake test classes and explicitly identifies the later additions. Brief 23 is an unimplemented brief according to `docs/PdfIntake.md:6`; its WPF implementation is outside this audit's file scope.

For compact file references, `Core/` means `Kor.Operations.EngineeringTools.Core/`, `Tests/` means `Kor.Operations.EngineeringTools.Core.Tests/`, and `PI` means `docs/PdfIntake.md`.

### F1 — The wall rule never establishes that four vertices form a rectangle

**Contradicted sentence:** “a filled, non-paper, four-vertex rectangle … is a wall” (`PI:230`). The implementation tests the enclosing box and vertex count, not the shape of the enclosed polygon (`Core/PdfToSafe/GeometryFilterService.cs:188`, `:195`, `:198`). It checks neither right angles, opposite-edge parallelism, area occupancy nor self-intersection.

**Smallest input:** a grey, filled, closed trapezoid with vertices in millimetres `(0,0), (6000,0), (5800,300), (200,300)`, on a sufficiently large page, outside furniture and without a matching declared column size. Its wall-proportioned enclosing rectangle passes, so it becomes a `WallPanel` even though its sides taper. A self-crossing four-point outline can pass the same kind of check. Both named wall test classes stay green: their four-point candidates are rectangles; neither supplies a nonrectangular quadrilateral.

The converse also fails: add one collinear midpoint to a rectangle's long side, sufficiently far from adjacent vertices to survive thinning. It now has five vertices and becomes a counted “ribbon,” retaining a slab/other fate, despite drawing exactly the same rectangle. The tests do not exercise equivalent shapes with different vertex counts.

### F2 — Unlabelled footing boxes are still Read in the primary ledger and still exported

**Contradicted sentences:** “stop counting a box that no label names as that mark” (brief 21, Goal), and “the ledger files it as **unaccounted**, not read” (`PI:451`).

`FootingOutlines.Place` assigns a scheduled mark before considering labels (`Core/Intake/FootingOutlines.cs:103`). The label loop only sets `LabelledOnThePlan` (`:174`). Every consumed piece is `BecameFooting` (`Core/PdfToSafe/GeometryFilterService.cs:115`), which always maps to Read (`Core/Intake/PathFate.cs:29`). Primary ledger rows use that mapping (`Core/SheetInventory.cs:77`). The unaccounted classification is only a non-primary `Note` (`:125`); `Totals` excludes it (`:219`). Export does not inspect the label flag (`Core/PdfToSafe/DxfExporter.cs:170`, `:433`).

**Smallest input:** the five-stroke interrupted outline in question 3 below, one scheduled spread type, and no labels. It yields one unlabelled footing and five Read path fates. Its footing context note is Unaccounted when no spread mark is placed anywhere on the sheet, but it contributes zero to the primary Unaccounted total. Add a real spread label elsewhere on the sheet and even the context note becomes Read; its text still lists the unlabelled box apart. Add one ordinary emitted column to give the exporter a nonzero centring weight and the false footing is written to FOOTING.

`Tests/Intake/AFootingIsADashedRectangleTheScheduleSizesTests.cs:78` explicitly accepts an unlabelled interrupted outline. `Tests/FiveStickFilesTests.cs:285` checks counts and scheduled-mark membership, not label flags or positions. Neither establishes that the emitted object is a footing.

### F3 — A label cannot correct an ambiguous interrupted footing's scheduled type

**Contradicted sentence:** “The plan's label names the footing it is nearest to” (`PI:428`).

Pass 2 takes the first scheduled type whose side length and stub depths fit, places it, then breaks (`Core/Intake/FootingOutlines.cs:146`, `:152`, `:160`, `:166`, `:212`). Unlike pass 1's `TypeOf` (`:199`), pass 2 never uses labels to choose the type. The final loop requires the already-selected mark to equal the label; it never renames the footing (`:184`).

**Smallest input:** a 1500 mm dashed full side and two 300 mm stubs; schedule order `F1 = 1500 × 1500 × 600`, `F2 = 1500 × 1500 × 900`; one F2 label inside the outline. Pass 2 emits F1 at depth 600 with `LabelledOnThePlan=false`. Reversing schedule order emits F2 at depth 900 with the flag true. Even identical plan dimensions do not make the depth/mark choice harmless. The existing label test uses just one size-matching type (`Tests/Intake/AFootingIsADashedRectangleTheScheduleSizesTests.cs:119`), so it stays green.

There is a smaller related reach inconsistency in pass 1: ambiguous sizes use `LabelSlackMm=150` to select a mark (`:206`), while final association permits half the larger footing dimension (`:184`). Two identical-size types and an F2 label 400 mm beneath the box can select F1 despite being within the advertised label reach.

### F4 — Distinct grids merge, and the exported position is the bubble coordinate

**Contradicted sentence:** “its position [is] the rule through the bubble, the two ends of one line are one axis” (`PI:479`).

`GridBubbles.On` tests only whether rule lengths near the bubble coordinate sum to the required reach (`Core/PdfToSafe/GridBubbles.cs:80`). `Axes` clusters consecutive **bubble** coordinates within 1.5 pt and averages them (`:100`, `:105`, `:108`); it does not retain a rule identity or its coordinate. Labels distinguish neither separate lines nor views during clustering.

**Smallest input:** two long vertical rules at x=1000 and x=1001.5 pt, one labelled bubble on each, staggered far enough in y that each circle contains only its own word. They produce one axis `C|C.2` at x=1000.75, marked as disagreeing ends, instead of two axes. The spacing is 50.8 mm at 1:96. Such an offset-grid construction is possible; the code has no constraint excluding it. This audit does not assert that one occurs in the banked drawings. A chain of coordinates 1000, 1001.5, 1003 also merges, although the outer pair exceeds the tolerance.

For position alone, one bubble at x=1000 with its only long rule at x=1001 produces an axis at 1000, not 1001. For view identity, disjoint views whose vertical grids happen to share x also merge; the duplicate-name note at `Core/SheetInventory.cs:179` cannot recover the already-merged axes. The specific separated F/A example in the doc can survive; this is not a general no-merge guarantee.

No current synthetic test fails on these absent inputs. `Tests/SheetFurnitureIsNotStructureTests.cs:344` puts the vertical grids 400 pt apart and each bubble exactly on its rule. `Tests/Intake/DrawingIntakeTests.cs:41` has one axis. `Tests/Intake/AGridAxisIsANamedLineTests.cs:44` supplies already-built axes to the exporter. The banked test would catch a changed count on its five pages, but not a pre-existing merge baked into that count, a position error, or wrong nonempty names (`Tests/FiveStickFilesTests.cs:264`).

### F5 — A title saying PLAN outranks an explicit NOTES or KEY qualifier

**Contradicted semantic claim:** “Only a plan is taken off to a DXF” (`PI:334`). The actual type means a regex match, not a verified takeoff plan.

`DrawingIntake.SheetTypes` checks SCHEDULE, then any whole-word PLAN/PLANS, then section, details and notes (`Core/Intake/DrawingIntake.cs:404`). The negative lookahead after FOUNDATION only protects immediately following DETAILS/SCHEDULE/NOTES; it does not protect `FOUNDATION PLAN NOTES`.

**Smallest input:** the bookmark or SHEET TITLE `FOUNDATION PLAN NOTES`. It is `plan`, not `notes/general`. `KEY PLAN` and `DESIGN LOAD PLAN` also type `plan`. No level or structural-content check is required. Conversely, `FOUNDATION NOTES` is notes and `FOUNDATION PLAN SCHEDULE` is schedule. An early recognisable bookmark wins even if the field contradicts it (`:110`, `:421`).

These titles pass the documented `pdf-takeoff` plan-only gate. With ordinary exportable geometry, they are eligible to write DXFs; an otherwise empty page is not a guaranteed file. The CLI implementation is outside the explicit file list, so that consumer conclusion is conditional on its stated gate (brief 17, Change 2), rather than claimed as a separately inspected CLI branch. `DxfExporter.Export` itself has no sheet-type argument.

In particular, `PI:337` **explicitly endorses DESIGN LOAD PLANS as a plan**. Its inclusion follows the documented policy; it is not an implementation contradiction by itself. `FOUNDATION PLAN NOTES` is the clearer wrong-type example. No assertion in the scoped Intake tests independently checks these titles or the title-source precedence.

### F6 — Footing-only and grid-only geometry can produce no DXF

**Contradicted sentences:** “the DXF gets a FOOTING layer” (`PI:379`), and “one LINE per axis … and the name as TEXT at both ends” (`PI:482`).

The exporter computes its centring weight from slabs, columns, lines and walls, then returns immediately if the weight is zero (`Core/PdfToSafe/DxfExporter.cs:49`, `:67`). Footings and grids are absent from that calculation.

**Smallest input:** `ExtractedGeometry` containing one valid `FootingOutline`, with all four weighted geometry lists empty. Export returns before opening the output file. One grid axis alone has the same result. A page whose recognized dash pieces were all consumed as footings can reach the first shape of this problem. The footing tests never export a footing-only result; the grid export fixture always supplies a slab (`Tests/Intake/AGridAxisIsANamedLineTests.cs:47`).

There is a separate layer-table omission even with a nonzero weight: FOOTING polylines are emitted at `DxfExporter.cs:433`, but `:372`–`:392` declares only the four structural families, optional text and GRID. **Smallest input:** one ordinary column plus one footing. FOOTING is an entity layer name without a corresponding LAYER-table entry. This is a structural omission in the output; no claim is made here about which CAD readers repair it. The scoped tests do not check a FOOTING layer-table entry.

### F7 — The “exactly one” remap can conceal a missing or duplicate decision

**Contradicted explanatory sentence:** “what thinning removed is `CollapsedByThinning`” (`PI:178`; also `Core/Intake/PathFate.cs:10`).

The remapper writes into one slot per full-path index and fills **all** empty slots with CollapsedByThinning (`Core/Intake/DrawingIntake.cs:258`, `:274`, `:277`). It does not prove that an empty slot corresponds to an ordinal absent from the thinned read, nor reject a second assignment.

**Smallest inputs:** both kept-ordinal lists `[0]`, both path counts `1`, with (a) no thinned fates, or (b) two fates for index 0, e.g. BecameSlab followed by TooShort. Case (a) labels a retained path “collapsed”; case (b) silently retains only TooShort. A larger equivalent input preserving one column fate passes the additional “some column exists” assertion in the banked gate. These are remapper API/upstream-error counterexamples, not evidence that the current normal classifier emits duplicate decisions.

The returned list still has exactly one fate per index. The defect is **loss of evidence and false attribution**, not returned-list multiplicity. `Tests/Intake/ThePopulationIsTheUnthinnedReadTests.cs:23` only supplies complete, consistent mappings; `Tests/FiveStickFilesTests.cs:197` checks counts, distinct-index cardinality and one column, not ordinal correctness or why each path disappeared. Its claims to check real-page ordinal correctness in the population test's remarks (`:17`) exceed those assertions.

### F8 — The scale fallback undoes the scale reader's ambiguity refusal

**Contradicted sentence:** “When a title block carries several SCALE fields that parse to DIFFERENT values the sheet is ambiguous — also null, never a guess” (`Core/SheetScaleReader.cs:21`). `PI:347` describes the fallback without this exception.

`SheetScaleReader.FromPage` deliberately returns null for conflicting ratios (`:93`). `DrawingIntake` then substitutes `RatioOf(scaleStatement)` (`Core/Intake/DrawingIntake.cs:107`), and `TitleBlockFields.Set` keeps the first SCALE field (`Core/Intake/TitleBlockFields.cs:120`). Null carries no reason with which to distinguish “not found” from “conflict.”

**Smallest input:** two labelled fields in the bottom-right region, on separate baselines, the upper `SCALE: 1 : 100`, the lower `SCALE: 1 : 50`. Both are within the original scale reader's position limits. That reader refuses; the field reader retains the upper value; the intake reports a ratio read. Neither scoped test class for fate accounting nor `DrawingIntakeTests` supplies conflicting scale fields.

This changes the reported scale statement/ratio, not the request's geometry scale: classification still uses the caller's denominator (`DrawingIntake.cs:42`). A null request denominator is not filled from the parsed scale.

### F9 — “Report over the record” still re-reads footing schedules and placements

**Contradicted sentence:** “`SheetInventory` is a report over the record and derives nothing itself” (`PI:168`; also `Core/Intake/SheetRecord.cs:55`).

`SheetInventory.Of(record)` calls `FootingScheduleReader.ReadSchedule(record.Content)` and `CountPlacements` itself (`Core/SheetInventory.cs:106`, `:110`). Intake previously reads the same schedule for typed schedule tables and again for footing geometry (`Core/Intake/DrawingIntake.cs:121`, `:144`; `Core/PdfToSafe/PdfPlanReader.cs:222`). It retains neither the typed footing table's box nor the placement collection as the report's authority. The report reinterprets unthinned Content; the original footing pass received thinned content.

**Smallest input demonstrating the second authority:** an otherwise valid retained record with one footing schedule row and footing object, but Content containing no schedule text. The ledger's schedule-row context counts the retained row while its footing context says “no spread footing scheduled on this sheet.” There is no reconciliation check. This is a public-record consistency counterexample; occurrence through normal intake is not established here.

Similarly, the original brief's “one vector read per sheet” was superseded by the explicitly documented unthinned/thinned two-read design (`DrawingIntake.cs:56`, `:63`). It should not be presented as a remaining one-read guarantee.

### F10 — Some Read/Unread statements describe recognition opportunities, not consumed data

**Contradicted statement:** the claimed accounting of what readers actually read, including “the axis is in the record” (`PI:485`).

`DrawingIntake.WordFates` declares all words in a schedule region Read if the corresponding reader returned **any** rows for that kind, even on another table (`Core/Intake/DrawingIntake.cs:320`, `:342`). Mark-shaped words are Read without proving a successful column check (`:358`). All title-block words remain Unread even when `TitleBlockFields` consumed them (`:339`), and the bookmark remains a primary Unread item despite typing the sheet (`Core/SheetInventory.cs:61`).

**Smallest inputs:** one successfully parsed column row plus an unread reinforcing note in the same table gives the note a Read fate; one recognised bookmark `FOUNDATION PLAN` is used for typing but reported as having “no reader.” A sheet with one recognised grid bubble read with `MarkupOnly=true` (or no positive scale) gives its axis-name word a Read fate (`DrawingIntake.cs:349`) although `Geometry.GridAxes` remains empty (`:138`, `:153`). The grid context note still describes the DXF layer (`SheetInventory.cs:180`).

The word partition remains arithmetically complete; these are overclaims/underclaims about meaning. Exact path and word cardinality cannot catch them.

### F11 — Several prose rules omit conditions that materially narrow or widen them

These are additional claim/code discrepancies with minimal inputs, not measured corpus regressions:

| Contradicted sentence | Implementation and smallest counterexample |
|---|---|
| “Ribbons (L and U cores drawn as one outline) are counted” (`PI:234`). | `GeometryFilterService.cs:195` increments `:207` only after the **whole enclosing box** passes wall limits. A 300 mm thick L with two 6000 mm legs has a broad box and is not counted. Local leg thickness is never measured. |
| “the declared-column-size rule … wins” (`PI:232`). | `GeometryFilterService.cs:174` tests world-axis bounding dimensions; the wall rule uses an oriented box (`:190`). Rotate a declared 18 × 60 inch column 30 degrees: its axis-aligned dimensions no longer match the schedule, but its oriented box qualifies as a wall. The declared-column wall test is unrotated (`Tests/Intake/AWallIsAFilledRectangleOfWallProportionsTests.cs:85`). |
| “non-paper” (`PI:230`). | `GeometryFilterService.cs:153` discards paper fill only when **unstroked**. A white-filled, stroked 12 inch × 20 foot rectangle can become a wall. This may be intentional visible-outline handling; it is broader than the doc adjective. |
| “drops a letter whose value, size and origin equal an earlier letter's” (`PI:330`). | `VectorPageReader.cs:139` rounds position and size to tenths of a point. Equal-value glyphs at x=100.01 and 100.04, same y and size, collapse despite distinct origins; font identity and orientation are absent from the key. Brief 18 explicitly specifies rounding to 0.1 pt, so this is a compressed-doc mismatch, not disobedience to that brief. |
| “the SCALE field verbatim” (`PI:347`). | `TitleBlockFields.cs:91`, `:108`, `:113` trim and join tokens/lines with spaces. A wrapped `AS` / `NOTED` becomes `AS NOTED`; line breaks and original spacing are not retained. A raw numeric string that fails parsing is still called “the sheet says its scale varies” (`SheetInventory.cs:58`): e.g. field value `123`, for which no ratio is read. |
| A field value is below its label “to the next label” (`PI:319`), in the label's column (`TitleBlockFields.cs:7`). | `TitleBlockFields.cs:100` stops at the next lower label **anywhere** in the right fifth; it does not restrict the stop to that column. Place SHEET TITLE at x=820,y=300, a REV label at x=950,y=280, and the intended title value at x=820,y=260 on a 1000-pt-wide sheet: the REV cuts off the title. Label words anywhere in a value are also treated as new labels (`:66`), despite the “start of a line” claim at `:18`. |
| “collinear within 12 mm” (`PI:374`). | `FootingOutlines.cs:253` tests successive coordinate gaps, not total cluster width. Three pieces at y=0,12,24 join a cluster even though the first and last are 24 mm apart. This follows brief 21's explicit adjacent-gap design, but is not a bound on the resulting side's collinearity. |
| “The build proves the compiled defaults and the banked rows agree” / “every public numeric property” (`PI:269`, `:293`). | The evidence in scope is a Slow test (`Tests/Rules/CompiledDefaultsAreTheBankedRowsTests.cs:30`), not a compile-time assertion. Its orphan detector accepts only double/int/bool and nullable forms (`:118`); a new `decimal` or `long` option escapes. It checks key-name presence (`:121`), not that a mapping reads the intended property. Parity covers the two mapping dictionaries, not every hard-coded threshold or all rules a reader might acquire. No build integration outside the scope was inspected. |

## 1. Does every path have exactly one fate?

For a successful `RemapToPopulation` call with consistent nonnegative indices and counts, **yes, every index in `[0, fullPathCount)` occurs once**: there is one array slot per index, assignment rewrites that index, and the final loop fills every null slot. There is no normal returned-list input producing two rows or an unfilled slot. Invalid negative indices/counts can throw; that is not a returned ledger with duplicates or holes.

F7 states the missing/duplicate **upstream decisions** the guarantee hides. Wrong but unique ordinal mappings also preserve cardinality while attaching a correct reason to the wrong shape. The banked test uses the same vector reader for its independent count and never checks source-ordinal correspondence or geometry identity.

There are two scope exceptions to the slogan:

- **No positive requested denominator:** `DrawingIntake.cs:137` leaves PathFates empty. One retained line and `IntakeRequest(null, options)` give a path with no fate. The banked and remap tests do not exercise that mode; `DrawingIntakeTests.NoScaleRetainsContentButDoesNotInventClassification` explicitly expects it (`:79`). The ledger uses provenance counts instead.
- **Before the retained population:** the “unthinned” reader still omits a PDF subpath with fewer than two points (`Core/VectorPageReader.cs:202`), such as a lone moveto. It is absent from both the record and the banked comparison. This is a definition boundary, not evidence of a missing visible wall.

## 2. Which drawn walls cannot this rule read?

The necessary shape is a surviving, closed, filled, non-annotation, four-point subpath whose oriented box passes the limits, after mode, no-ink, furniture, paper, frame and declared-size gates. Half-inch slack makes the dimensional limits 3.5–60.5 inches in thickness and at least 47.5 inches in length; aspect still has no slack. Those are acceptance bands, not merely a floating-point epsilon.

| Wall representation | What happens | Which of the two named wall classes can stay green? |
|---|---|---|
| L/U/T core or ribbon as one multi-vertex outline | Never becomes a wall; may become slab/column/discard. Only enclosing boxes passing the window increment the ribbon counter. | **Both.** They explicitly bank one narrow L staying a slab (`AWall…:108`, `TheWallRule…:47`); neither requires ribbon splitting or tests a broad core. |
| Wall with a doorway/notch represented as a nonrectangular multi-vertex contour | No reconstruction into wall runs; follows the old branches. | **Both.** No opening fixture. |
| Compound filled wall outline with an inner hole | `VectorPageReader.cs:163` emits subpaths separately with inherited fill flags. There is no retained hole/fill-rule relationship, so a qualifying outer rectangle can become a solid wall; it does not preserve the opening. | **Both.** No compound-fill fixture. |
| Door opening represented by two independent filled rectangular wall segments | Each segment can be read if it independently passes length/aspect; a short return can be missed. It is incorrect to say all walls with openings are rejected. | **Both** can pass despite a missing short segment. Their short-rectangle tests actually expect rejection. |
| Two parallel wall-face strokes, open/dashed outline, or a closed unfilled outline | The wall branch never runs for open/unfilled paths. Faces become unknown lines or are discarded; a sufficiently large closed unfilled outline becomes a slab. No face pairing occurs. | **Both.** The shape test explicitly expects a stroked unfilled wall-sized rectangle to remain a slab (`AWall…:102`); neither pairs faces. |
| Curved wall, tapered contour with more than four vertices, rectangular outline with redundant vertices | No curved/tapered wall model or simplification to four corners. A four-point taper can instead become a false positive (F1). | **Both.** Rotation of a true rectangle is tested; curves and redundant corners are not. |
| Real wall outside the dimensional/aspect window, inside detected furniture, paper-filled without stroke, or below the thinning resolution | Rejected by the corresponding gate. A visible wall drawn only as annotations follows the pre-existing annotation branch, not this wall rule. | **Both** can stay green: these are either asserted exclusions or absent inputs, not ground-truth recall tests. |

These are exhaustive branch-level representation limits, not a catalogue proving which explains the PDF/Revit shortfall. Counts alone cannot apportion that gap; the docs already acknowledge view multiplicity and some of these unsupported forms.

## 3. Can pass 2 place a dashed non-footing?

Yes. On a large structural plan, outside furniture, let the schedule declare F1 as 1500 × 1500 × 600 mm. Draw a dashed slab-depression/step edge at y=0 from x=0 to 1500 with three strokes `[0,300]`, `[600,900]`, `[1200,1500]`. At x=0 and x=1500 draw perpendicular 300 mm strokes from y=0 toward positive y. Nothing else closes the box. Translate the construction away from sheet furniture if necessary.

The 300 mm dash gaps are within 355.6 mm, the full side has three pieces, the stubs start at both ends and are shorter than the scheduled depth in plan. Pass 2 invents the opposite side at y=1500 and emits F1 centred at `(750,750)` (`FootingOutlines.cs:142`). The same linework can describe a cropped opening below or depression whose continuation is obscured. This is a plausible drafting counterexample, not a claim about an inspected banked sheet. The parser retains no linetype semantics with which to distinguish these meanings (`PdfGeometryModels.cs:40`). In fact, three abutting solid strokes also satisfy the “dashed” side; no positive gap is required (`FootingOutlines.cs:260`).

The label rule does **not** rescue the geometry. No label leaves it emitted and flagged. An unrelated F1 label inside it, or within 750 mm of its edge and nearer to it than to any emitted true footing, flags it as labelled. A missed true footing is absent from the competition, so the false box can steal its label. Existing tests assert this geometry's positive recognition, not its semantic uniqueness.

The ledger records the false box exactly as described in F2: consumed paths Read, unlabelled-box text in the footing context, context Unaccounted only if the whole sheet has no placed spread marks. It does not make the primary accounting “unaccounted.” Midpoint filtering also does not validate the **inferred interior** against furniture: a furniture rectangle strictly inside the completed 1500 mm box, touching none of the five input strokes, cannot stop placement (`FootingOutlines.cs:91`).

## 4. Which test protects nearby grid axes?

None of the inspected assertions protects distinct axes at or below 1.5 pt separation. F4 supplies the construction, the 50.8 mm conversion and the exact coverage boundaries. Only a new independently expected count/identity for that construction would fail; the existing banked count cannot establish that the original banked axes were correctly separated. Name-and-direction uniqueness is also not a cross-sheet identity scheme: `GridAxis` has only Name/Vertical/AtMm (`Core/PdfToSafe/PdfGeometryModels.cs:71`), with no view/building identity or cross-sheet reconciliation in these readers.

## 5. Which KOR-vocabulary title types wrongly, and does it export?

`FOUNDATION PLAN NOTES` is the direct notes-sheet counterexample; `KEY PLAN` demonstrates lack of a takeoff-purpose distinction. Both are `plan`. `DESIGN LOAD PLANS` is expressly included by the docs. The exact source ordering is implemented: bookmark, SHEET TITLE (or DRAWING TITLE), then parsed storey, title text, region words (`DrawingIntake.cs:100`, `:110`). The failure is the meaning assigned by the regex, not a different order. Export eligibility and the explicit CLI scope limit are recorded in F5.

## 6. Does the ledger arithmetic partition everything once?

For a consistent record produced by intake, **each retained word and retained path contributes once to a primary row**. WordFates loops once over each word (`DrawingIntake.cs:332`); text extraction is independent of thinning. Classified paths group by reason (`SheetInventory.cs:77`). Without classification, annotation/no-ink/paper/remaining-ink counts form the partition (`DrawingIntake.cs:159`; `SheetInventory.cs:147`). Context notes do not contribute to totals.

`SheetInventory.Of` does not enforce this invariant for arbitrary public records. Duplicate/missing WordFates are counted as supplied, without checking WordIndex; positive-scale records with missing PathFates simply undercount. One word plus two identical WordFates produces two primary word items. Classified path dispositions are recomputed from Reason, rather than read from each fate's stored Disposition (`SheetInventory.cs:78`).

For ordinary records, the total of **all five** disposition buckets is:

`retained words + retained paths + 2 + bookmark-item + rotation-item + markup comments + links + images`.

The fixed two items are title and scale. Bookmark contributes one if a bookmark title is present or OutlinesPresent triggers its fallback; nonzero rotation contributes one. See `SheetInventory.cs:51`, `:61`, `:63`, `:159`.

Thus totals can change without any PathFate or WordFate changing: add a bookmark to a page previously without outlines (+1), set rotation from zero to nonzero (+1), or add an image/link/comment (+1 each). A title becoming readable moves one primary item between Unread and Read; a scale statement becoming readable does likewise. Changes to footing labels, footing/grid counts or other context can be semantically large and move **no** primary total. Equal totals are evidence of accounting stability, not geometry identity or correct interpretation.

## 7. Test names against assertions; rule 11 summaries

Each row covers one current test class. File names below are under `Tests/Intake/` unless noted. Helper classes `FateFixture`, `WallFixture` and the frozen `PreWallClassifier` contain no tests and are not additional harnesses.

| Class and source | Does its name promise more than the assertions? |
|---|---|
| `EveryPathHasExactlyOneFateTests` (`:7`, `:11`, `:45`) | **Yes if universal.** One synthetic branch fixture plus mode cases checks direct classifier fates and selected object indices. Collapsed/footing reasons are manually appended to enum coverage; no remap corruption, real-page identity or no-scale coverage. The summary states coverage but **no exclusions**. |
| `ThePopulationIsTheUnthinnedReadTests` (`:14`, `:23`, `:53`) | **Yes.** Two hand-supplied mappings, not PDF population/ordinal correctness; no duplicates, omitted decisions or invalid mappings. Two-sided summary exists, but its claim that FiveStickFiles checks real ordinals is false (F7). |
| `TheWallRuleChangesNothingElseTests` (`:7`, `:25`, `:39`) | **Qualified by a good summary.** Compares one cloned fixture to a frozen classifier and specified changes. It checks neither all possible inputs nor rendered/exported/ETABS results; those exclusions and a shared-parser fault are explicit. No footing or grid comparison in the shared equality helper. |
| `AWallIsAFilledRectangleOfWallProportionsTests` (`:8`, `:18`, `:141`) | **Yes as a definition of wallhood.** Good rectangle dimensions/rotation/precedence/export coverage, but no rectangle predicate test, alternate vertex encoding, openings, or independently established wall truth. The summary explicitly excludes proof that wall-shaped fills are structural. |
| `TheLedgerChangesNothingButTheLedgerTests` (`:7`, `:21`, `:43`) | **Yes.** Compares Classify with/without fate recording; it never calls SheetInventory. Its equality helper omits Footings and GridAxes altogether and many compared fields remain empty/default in the fixture. An implementation adding a bogus footing only when fates are requested would escape. Summary has both sides and a same-class escape, but does not disclose the newer missing collections. |
| `DrawingIntakeTests` (`:11`, `:19`, `:79`, `:97`) | **Broad name, qualified scope.** One synthetic PDF tests record fields, accounting, no-scale and current reader/export equivalence. The “old” reader is current PdfPlanReader, sharing production rules; a common fault passes. No independent typing, title-field, deduplication or scale-fallback regression assertion. Two-sided summary exists, but only broadly excludes the stick-file baseline. |
| `APathThatDrawsNothingIsNotGeometryTests` (`:7`, `:23`, `:78`) | **Qualified.** Fill/stroke combinations, annotations and precedence are covered for synthetic candidates; “draws nothing” does not mean transparency, clipping or occlusion. Its summary explicitly excludes parser flags, real counts and exports and names a shared-error escape. |
| `AFootingIsADashedRectangleTheScheduleSizesTests` (`:15`, `:44`, `:78`, `:119`, `:133`) | **Yes.** Squares and interrupted squares, a boundary cluster, isolated label reach and consumed pieces. No rotated/nonsquare ambiguity, furniture fixture, false-positive semantic check or footing export. The “emits nothing else” assertion omits Columns; result.Footings is never populated in that classifier-only test. Two-sided summary exists but predates the pass-2/label assertions and omits the semantic escape. |
| `AGridAxisIsANamedLineTests` (`:15`, `:44`, `:58`) | **Yes.** Tests already-constructed axes' exported counts/names, relative line positions and fate mapping; it does not call GridBubbles. It also does not check the TEXT entities' coordinates at the line ends. Two-sided summary exists but delegates to a banked test that only asserts nonempty names. |
| `ADxfCensusSaysWhichLayerMovedTests` (`:13`, `:34`, `:44`, `:75`) — **post-9a** | **Qualified.** Covers synthetic per-layer counts, one missing-before file and selected baseline names; “moved” excludes coordinate changes. The summary explicitly says so. Missing-after is not exercised despite the plural “files present on one side only.” |
| `AStoreyHeightIsTheDistanceBetweenLevelLinesTests` (`:16`, `:26`, `:34`) — **post-9a** | **Yes.** Its “level lines” fixture contains words and an **empty path list**; asserts height from label positions and uses production NormalizeLevel for expected names. No proof of drawn line positions or sheet-type gate. Two-sided summary exists, but calls these inputs level lines. |
| `TheDrawingsStoreysAgainstTheModelsTests` (`:14`, `:24`, `:44`) — **post-9a** | **Qualified.** Synthetic reconciliation and pair-wise comparison, tolerance and summary; no actual drawing/model parsing or export. Two-sided summary exists. |
| `TheLiveSetsStoreysAgreeWithTheirModelTests` (`:8`, `:19`, `:28`, `:44`) — **post-9a** | **Yes, plural and unconditional.** One job, floors of 18 matches/3 sheets and no off-tolerance matches; unmatched pairs are unchecked. Unreachable share returns normally, giving a passing test, not an xUnit skipped result as the summary says. Has coverage/exclusions in prose; no network was accessed here. |
| `FiveStickFilesTests` (`Tests/FiveStickFilesTests.cs:20`, `:186`, `:216`, `:260`, `:279`) | **Name alone is neutral; individual claims are wider.** Banked volume/mark sets/rounded wall rows, self-check floors, cardinality and selected geometry counts. Correct mark sets do not prove sizes; correct counts do not prove locations, label ownership or topology. Grid names are only required nonblank, not equal to banked names, and the grid method uses PdfPlanReader despite claiming to read through the retained record. A two-sided summary exists but is stale: it omits the added path/wall/footing/grid checks and says storeys are excluded despite the later storey test. |

**Rule 11's requested two-sided summary is absent only from `EveryPathHasExactlyOneFateTests` among these classes.** Equivalent prose counts; lack of the literal uppercase heading alone is not a defect. For rule 11's stronger requirement of an explicit same-class fault the harness would miss, DrawingIntake, the footing harness, the grid harness and the later synthetic storey-comparison harness give broad exclusions rather than a concrete fault. The population harness supplies a specific omission but incorrectly delegates its protection to FiveStickFiles. These are weaker disclosures, distinct from having no DOES NOT statement at all.

The additionally scoped `SheetFurnitureIsNotStructureTests` has a two-sided summary and an explicit white-filled-column escape (`Tests/SheetFurnitureIsNotStructureTests.cs:29`). Several tests assert counts rather than identity, and `ATitledBoxIsFurnitureByItsLastWordAndAPlanIsNot` (`:245`) does not test a title containing a furniture word followed by PLAN: production `SheetFurniture.TitledBoxes` accepts the furniture word **anywhere** (`Core/PdfToSafe/SheetFurniture.cs:251`). A small ruled `SLAB PLAN NOTES PLAN` box can therefore be furniture despite its last word. `AnUnderlineIsARegion…` (`:297`) actually asserts that Regions is empty (`:313`).

The additionally scoped `CompiledDefaultsAreTheBankedRowsTests` has both sides and the “reader honours the row” escape (`Tests/Rules/CompiledDefaultsAreTheBankedRowsTests.cs:21`). F11 records its numeric-type and mapping coverage limits. No database-backed result is asserted here.

## 8. Doc against code, §§7–15

This checklist covers the executable claims; numerical measurements, visual identifications of real objects, historical test results and manual downstream proofs are the verifier's evidence and were not revalidated. **Not verified in scope** means the necessary consumer/source is outside the listed files; it does not mean the claim is false.

| Section and claim | Static disposition |
|---|---|
| §7 `PI:166`: document entry returns typed per-sheet records with path and word fates. | **Found**, `DrawingIntake.cs:19`, `:214`, `SheetRecord.cs:12`; with the no-scale fate exception in question 1. “Everything today's readers produce” is limited to those adapters, not every Core reader. |
| §7 `PI:168`: inventory derives nothing itself. | **Contradicted**, F9. |
| §7 `PI:168`: pdf-takeoff and pdf-inventory consume the record. | **Not verified in scope**: their CLI entry points are excluded. The Core entry and reporting API exist. |
| §7 `PI:177`: Content unthinned, classifier thinned, ordinal remapping and collapsed remainder. | **Found with qualification**, `DrawingIntake.cs:56`, `:63`, `:152`; “collapsed” also conceals missing decisions (F7). Annotation paths map by appended position, not content ordinal. |
| §7 `PI:179`: every path on the page checked once, page by page. | **Overstated**: selected CoverFloors pages only, retained population only, cardinality rather than mapping correctness (`FiveStickFilesTests.cs:192`); no-scale exception. |
| §7 `PI:188`, `:198`: primary partition, unknown lines Unaccounted. | **Found for consistent intake records**, question 6; `PathFate.cs:32`. Historical invisible-slab behaviour is superseded by §8, not a new current defect. |
| §8 `PI:206`: content with no fill/stroke is NoInk; annotations exempt. | **Found at the classifier gate**, `GeometryFilterService.cs:124`, `:130`; markup-only exclusion and prior thinning can give another fate. It is not a promise that every original invisible subpath gets NoInk. |
| §8 `PI:220`: closed outlines will become their proper objects in wall/footing steps. | **Not a completed general capability**: rectangle walls only, and solid closed footing paths are explicitly excluded by `FootingOutlines.cs:89`. The sentence is forward intent; §13's exclusions narrow it. |
| §9 `PI:230`: filled non-paper rectangles of the stated proportions, declared columns first, wall outline/axis/thickness/fate. | **Data and branch order found**, `GeometryFilterService.cs:174`, `:200`; rectangle, paper, rotation and tolerance qualifications in F1/F11 and question 2. “On a plan” is not a classifier gate; §9 itself admits this at `PI:260`. |
| §9 `PI:234`: WALL export and ribbon count. | **WALL export found** under normal structural layering (`DxfExporter.cs:424`); optional colour/layer settings alter layer names. **Ribbon claim narrower in code**, F11. No ribbon splitting exists. |
| §9 `PI:242`: “The schedule decides what is a column.” | **Not exclusively**: `GeometryFilterService.cs:211`, `:243` still emits undeclared columns by shape. Minimal input: undeclared grey 600 × 600 mm square. The nearby prose's “rule decides the rest” makes this a shorthand, not removal of the shape fallback. |
| §9 `PI:250`: PDF-derived walls reach ETABS through the existing DXF path. | **Partly supported** by the synthetic export/readback test (`AWall…Tests.cs:141`); the claimed real-job convergence proof is **not verified in scope**. |
| §10 `PI:280`: changed DXF defaults, internal mapping, migration applied. | Mapping use is visible in the parity test; actual DXF service/default files and other-repo migration/application are **not verified in scope**. No database was consulted. |
| §10 `PI:285`: parity fails for unset connection; shared mappings agree, missing rows require declaration and stale exemptions fail. | **Found**, `CompiledDefaultsAreTheBankedRowsTests.cs:58`, `:74`, `:84`, `:101`. It is a Slow test, not itself a guarantee of every build. |
| §10 `PI:293`: every numeric option mapped or declared NotARule. | **Narrower**, F11: double/int/bool only, selected records, key spelling rather than property-binding proof. The explicitly deferred corpus-measurement paragraph is not a landed capability. |
| §11 `PI:308`: title-source priority. | **Found**, `DrawingIntake.cs:100`, `:110`, `:421`; false semantic types in F5. |
| §11 `PI:317`: title-block fields read by labels, beside or below; retained on record. | **Found with layout restrictions**, `TitleBlockFields.cs:28`, `:45`, `:86`; F11 supplies a column-boundary counterexample. It uses position/size heuristics, not a general form parser. |
| §11 `PI:326`: glyph deduplication before word extraction. | **Found with rounded equality**, `VectorPageReader.cs:135`; F11. Changes in word count cannot prove unchanged word meaning or furniture recognition. |
| §11 `PI:334`: plan-only CLI and differential; non-plan emitted-geometry ledger note. | **CLI/differential not verified in scope**; false-positive plan types in F5. **Note found**, `SheetInventory.cs:92`, but its “geometry” count is walls+columns+slabs only, excluding footings, grids and lines. |
| §12 `PI:346`: retained scale statement, ratio fallback, dropped-equals repair. | **Found**, `DrawingIntake.cs:106`, `SheetScaleReader.cs:104`; verbatim and conflict qualifications F8/F11. |
| §12 `PI:356`: AS NOTED counts as read; none stated means no scale statement. | **Found for that recognised field**, `SheetInventory.cs:55`; any unparseable nonblank field is treated the same, and no recognised field is an observation of reader failure/absence, not proof the PDF states none. |
| §13 `PI:373`: dash chaining, collinearity, three pieces per side, scheduled size. | **Found as a shape heuristic**, `FootingOutlines.cs:49`, `:89`, `:99`, `:199`, `:245`; no linetype test, tolerance is 60 mm rather than exact size, chained collinearity is not a 12 mm cluster bound. Four full sides describes pass 1; §14 deliberately adds stubs. |
| §13 `PI:377`: footing pass before classifier, consumed dash fate/object index, no second branch, retained dimensions. | **Found**, `DrawingIntake.cs:144`, `GeometryFilterService.cs:115`, `FootingOutlines.cs:103`. This proves ownership of consumed pieces, not semantic footing identity. |
| §13 `PI:379`: FOOTING layer and per-mark ledger. | **Conditional export / layer-table omission**, F6; per-mark context found at `SheetInventory.cs:115`. Overlay colours are **not verified in scope**. |
| §13 `PI:410`: five-page test covers rectangles either orientation. | **Count gate only**: `FiveStickFilesTests.cs:279` does not assert outline orientation, placement identity or per-mark read counts. Its explicit inability to distinguish a sump is accurate. |
| §14 `PI:424`: one full side/two same-direction stubs; sorted clusters; one stub insufficient. | **Found**, `FootingOutlines.cs:142`, `:227`, `:245`. No test of actual occluding structure or semantic exclusivity; question 3. |
| §14 `PI:428`: nearest label names footing within half size. | **Partial**, F3: flagging only, no type correction, ties use the first footing, nearest is sought before mark compatibility. |
| §14 `PI:431`: nothing inside furniture is footing/placement; intake, ledger, overlay, harness pass furniture. | **Point filters and Core/harness callers found**, `FootingOutlines.cs:91`, `FootingScheduleReader.cs:173`, `PdfPlanReader.cs:225`, `SheetInventory.cs:110`, `FiveStickFilesTests.cs:290`. No inferred-box interior check; unruled furniture is not recognised. Overlay and pricing callers are **not verified in scope**. |
| §14 `PI:446`: labelled/placed per-mark row, unlabelled boxes apart, ledger unaccounted. | Per-mark text **found**, but row Count is all boxes and the primary totals still say Read (F2). Overlay output and unmatched-label listings are **not verified in scope**. |
| §14 `PI:465`: footing gate counts boxes/labels and checks scheduled marks, not labels or position. | **Found**, `FiveStickFilesTests.cs:285`, `:292`, `:294`. This paragraph accurately states the principal gate limitation. |
| §15 `PI:479`: named axes, rule position, both ends one axis, same name across sheets one line. | Names/data **found**; true rule positions and line identity are **not established**, F4. Cross-sheet alignment by name is explicitly deferred at `PI:522`, so it is not current code behaviour. |
| §15 `PI:481`: joined disagreeing names, mm geometry, GRID line plus two names, common recentering. | **Found with qualifications**, `GridBubbles.cs:108`, `DrawingIntake.cs:154`, `DxfExporter.cs:497`; F4/F6. Exported extent is computed from retained geometry, including objects the export exclusions might remove (`DxfExporter.cs:485`), not original grid-rule endpoints. |
| §15 `PI:484`: pieces/names Read, ledger direction names, repeated names listed without merging. | Mapping/listing **found**, `PathFate.cs:29`, `DrawingIntake.cs:349`, `SheetInventory.cs:179`; consumption/mode and pre-listing merge qualifications F4/F10. Repetition alone does not prove a second view. |
| §15 `PI:524`: banked check covers “count and naming” on the five pages. | **Overstated if naming means correct labels/directions**: it asserts total axes and nonblank names only (`FiveStickFilesTests.cs:266`). Replacing every name with `X`, preserving counts, passes that gate. The separate synthetic tests check a few supplied/constructed names. |

Only this response file was created. The pre-existing Architecture edits and test-result files were left untouched.
