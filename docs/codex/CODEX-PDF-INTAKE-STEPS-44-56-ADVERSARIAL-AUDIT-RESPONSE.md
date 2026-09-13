# PDF intake steps 44–56: adversarial audit response

Audited range: `3da6f85c..44a5bd96`. All source locations below refer to **`44a5bd96`**, not the current working tree. Reviewed 2026-09-13.

These are **source deductions, not executed reproductions**. No builds, tests, drawing/model files, databases, network access, or other repositories were used. Synthetic inputs below describe counterexamples or mutations of a check's output; they were not run. Historical six-set and corpus results remain the authors' reported measurements.

Path abbreviations: `Core/` = `Kor.Operations.EngineeringTools.Core/`; `Tests/` = `Kor.Operations.EngineeringTools.Core.Tests/`; `Verbs/` = `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/`. Findings are ordered by consequence: incorrect structure, misleading measurement, then incomplete checks. There are 25 findings. No implementation changes accompany this report.

## 1. High — removing pattern cells shifts the flags that protect scheduled columns

**Claim:** §57: “Declared-size columns are never quadrants.”

**Source:** `Core/PdfToSafe/GeometryFilterService.cs:480`, `:591`, `:629`, `:633`.

**Smallest input:** Three abutting, shape-classified pattern cells followed in entity order by two equal, declared-size columns that touch at one corner. Keep the pair away from the pattern. For example, use three 900 × 1200 mm cells and two scheduled 1800 × 400 mm columns.

**Deduction:** The pattern pass removes entries from `Columns`, sizes, colours and annotation flags, but does not compact `columnByShape`. The quadrant pass then indexes that original flag array with the survivors' new indexes. The flags `true,true,true,false,false` become `true,true` for the two scheduled survivors, and the pair is discarded as `SymbolQuadrant`. Putting the scheduled pair first leaves its `false,false` protection intact. Thus a PDF re-export's path order can remove real scheduled columns. The declared-pair test does not precede its pair with cells; the path-fate fixture does not combine those cases in this order.

## 2. High — splitting a page into views resurrects stood-down tendon anchors

**Claim:** §56: “a column whose footprint holds either end is the anchor and is stood down”.

**Source:** `Core/Intake/DrawingIntake.cs:325`; `Core/Intake/SheetViews.cs:204`; `Core/PdfToSafe/DxfExporter.cs:234`.

**Smallest input:** A page split into two named views, with one column-sized block already marked `ColumnIsTendonAnchor = true` in one view.

**Deduction:** Intake marks anchors before view export. `SheetViews` copies each view's columns, sizes, colours and annotation flags, but not `ColumnIsTendonAnchor`. The exporter's exclusion checks that parallel list; an empty list excludes nothing. The block becomes a column again in the split view. The single-view path can retain the original geometry and its flag, so page layout changes the structural output even when tendon recognition is identical.

## 3. High — plan-only parkade levels below the first stated elevation collide or invert

**Claim:** §54: “parkade levels below, numbered levels up, the roof on top”; “A stated elevation stands”.

**Source:** `Core/Intake/StoreysFromPlans.cs:96`, `:117`, `:127`, `:139`, `:146`.

**Smallest input:** A stated chain `L1 = 0`, `L2 = 3000 mm`, plus a plan naming `P1` that the chain does not cover.

**Deduction:** Ranking inserts P1 before L1. The first assumed level starts at zero; reaching L1 assigns zero again. Back-interpolation only runs when `lastStated >= 0`, so it cannot repair levels preceding the first stated one. The resulting elevations are P1 = 0, L1 = 0, L2 = 3000. Adding P2 as well yields P2 = 0, P1 = 3000, L1 = 0, L2 = 3000: the parkade ladder reverses at L1. Subtracting the first datum does not repair either result. The tests cover insertion between stated levels, not this lower boundary.

## 4. High — identical tower cores can register tower A's top plan on tower B

**Claim:** §62: “Its members are the tower's”; “the displacement most of a sheet's members share with the members already placed is its frame”.

**Source:** `Core/Dxf/DxfToEtabsService.cs:1144`; `Core/Dxf/GridAlignment.cs:461`, `:470`.

**Smallest input:** Two already placed towers have the same four irregularly spaced column centres, separated by 50 m. A plan explicitly named for A contains those four centres and no usable grid axes. Enumerate B's placed members first.

**Deduction:** The fallback pools placed member positions without a building/storey identity constraint. Both translations receive four votes and satisfy four-of-four support. `MaxBy` takes the first equally full bin; there is no competing-fit refusal or tower-name check. B can therefore win with apparently perfect support. The half rule that refused the historical four-of-ten case does not protect this input. An empty raw placed list is refused; placing all supporting points on one storey is sufficient. This is a wrong-building translation, not the documented rotation exclusion.

## 5. High — repeated sheet endpoints can supply the entire four-member quorum

**Claim:** §62: “A placed member is a place, however many storeys stand one there”; a fit needs “at least” four members and “at least half”.

**Source:** `Core/Dxf/StructuralPlanClassifier.cs:538`; `Core/Dxf/GridAlignment.cs:442`, `:451`, `:453`, `:470`.

**Smallest input:** At the public solver boundary, four copies of `(0,0)` in the sheet list and four copies of `(10000,0)` in the placed list. A drawing can produce the sheet-side repetition when several wall axes share an endpoint.

**Deduction:** The initial minimum-count check sees four entries on each side. Placed-point deduplication then reduces the latter list to one place, without rechecking the minimum. Each repeated sheet entry still votes and is counted by `Count(p => placed.Any(...))`; the solver accepts four-of-four support from one physical coincidence. Four wall ends meeting at a junction can similarly contribute half of an eight-end sheet. `APlacedMemberOnThirtyStoreysIsOnePlace` exercises repeated placed points, not repeated sheet points or one-to-one support. The three-of-seven refusal fixture also does not isolate the half rule from the four-member minimum.

## 6. High — a rejected orphan slab reserves the place of a supported floor

**Claim:** §57: “Every member stands on the building”; §63 identifies `PlacedMembers` as the replacement for plate identity. The registry requirement in this audit is “only placed members held”.

**Source:** `Core/Dxf/E2kGeometryComposer.cs:550`, `:1285`, `:1288`, `:1300`.

**Smallest input:** Two eligible slab candidates on the same storey, centred at `(0,0)` in an inch model: first a 600-inch square, then a 1200-inch square. The only standing support is a column at `(500,0)`.

**Deduction:** The smaller slab has no support inside its bounds plus the support margin; the larger one does. However, `placedSlabs.Add` runs before `AnythingStandsUnder`. The rejected smaller slab reserves the shared centroid, and the supported larger slab is skipped as already held. Reversing the candidate order writes the supported floor. The comment “Claimed only once the plate is certain to be written” below this branch applies to the property allocation; it does not undo the earlier spatial reservation.

## 7. High — the endpoint “footprint” is a square around the column's longer side

**Claim:** §56: “a column whose footprint holds either end is the anchor”; §61 limits the overshoot clause to “fittings only”.

**Source:** `Core/Intake/TendonAnchors.cs:169`, `:176`, `:180`.

**Smallest input:** An undeclared, axis-aligned 305 × 914 mm column centred at zero, and a labelled horizontal tendon starting at `(400,0)` and continuing to `(5000,0)`.

**Deduction:** With the 50 mm slack, the actual short-side bound is 202.5 mm. The endpoint lies outside it. `Inside`, however, uses `Math.Max(hx,hy)` for both coordinates, giving a 507 mm square bound; it marks the column as an anchor. The overshoot clause's smallest-scheduled-area protection is irrelevant because the broad `Inside` predicate already succeeds. This remains a way to remove a real column without the run ending in its footprint. In the overshoot test, the declared large block is also protected by the area guard; that assertion does not independently prove the declared-size guard at an actual labelled endpoint.

## 8. Medium — nearly parallel dash offsets change under a common translation

**Claim:** §63: “DashedLineJoiner … Grouped by distance in sorted order now.”

**Source:** `Core/Dxf/DashedLineJoiner.cs:41`, `:45`, `:54`, `:59`.

**Smallest input:** Two loose same-layer segments, A `(0,0)–(10,0)` and B `(20,0)–(30,0.01)`, with offset tolerance 0.15, angular tolerance 0.5 degrees and maximum gap 24, all lengths in the same unit.

**Deduction:** Their angle difference is about 0.0573 degrees. Their stored offsets are approximately 0 and −0.02, so they group and join. Translate both by `(1000,0)`: A's offset stays zero, while B's becomes approximately −1.02, so they separate. Each offset was measured against that segment's own normal; comparing such offsets is not a translation-invariant separation between nearly parallel lines. This is neither an intentionally retained coordinate cell nor a rotation of the drawing.

## 9. Medium — registration chooses the fullest rounded bin, even when another frame supports more members

**Claim:** §62: “the displacement most of a sheet's members share … is its frame”; §63: “a cell is only an index for the search”.

**Source:** `Core/Dxf/GridAlignment.cs:451`, `:457`, `:461`, `:466`, `:470`.

**Smallest input:** Five irregular, well-separated sheet positions. Their intended placed counterparts have X displacements 49, 49, 49, 51 and 51 mm. Four of those sheet positions also have unrelated placed counterparts at X displacement 10000 mm; choose irregular spacing to avoid other repeated cross-pair displacements.

**Deduction:** The intended five-member fit is split into bins of three and two; the unrelated four-member bin wins. Only that bin is refined, and its four-of-five fit is accepted. Moving just the source view 2 mm in X puts the intended displacements at 47/49, all in one bin, so the intended fit wins. A bin is deciding which physical frame is considered. The placed-point deduplication also still uses rounded 10 mm cells: positions 4.9 and 5.1 mm are separate before a 0.2 mm common shift and identical afterwards. The fixed-vector differential does not establish invariance of these other bin phases.

## 10. Medium — the member registry's “inch” includes points more than an inch apart

**Claim:** §56: “A column within an inch of one already placed stands at that one's joint”; §63: “the nearest thing within the tolerance”.

**Source:** `Core/Dxf/E2kGeometryComposer.cs:579`, `:1970`, `:1975`.

**Smallest input:** Two same-storey member placements whose corresponding endpoints differ by `(20,20)` mm.

**Deduction:** Their endpoint distances are 28.284 mm, beyond an inch. `PlacedMembers.Near` nevertheless accepts them because it independently bounds X and Y by 25.4 mm. It suppresses the second placement even though `ColumnJointNear` uses Euclidean distance and treats the locations as distinct. The former rounded inch key distinguishes `(0,0)` from `(20,20)`; this registry can lose a placement the old key kept outside the stated distance. This does not require calling the two readings a legitimate twin: the defect is that the identity tolerance is wider diagonally than specified.

For a simple same-storey chain at 0, 0.75 and 1.5 inches, rejected duplicates are not added, so that path does **not** walk through the middle point. It remains order-dependent: taking the middle point first can suppress both ends. Premature reservations are the separate fault in finding 6.

## 11. Medium — nearest-node searches still produce different graphs from different entity orders

**Claim:** §63: “PlanLoopBuilder.NodeOf — a node was the cell”; the replacement finds “the nearest thing within the tolerance”, with “the earlier one on a tie”.

**Source:** `Core/Dxf/PlanLoopBuilder.cs:45`, `:86`; `Core/Dxf/E2kGeometryComposer.cs:680`.

**Smallest input:** Three endpoint occurrences on one line at 0, 0.75t and 1.5t, where t is the join tolerance, processed in that order or with the middle occurrence first.

**Deduction:** First-end-first creates representatives at 0 and 1.5t; middle-first creates just one at 0.75t. Both nearest searches are locally correct, and neither case needs an exact tie. The resulting graph/joint count depends on arrival order because accepted representatives are never reconsidered. Even three points all pairwise within t retain whichever coordinate came first, changing the joint location. The stated earlier-on-tie convention is implemented; it does not imply permutation invariance, and translating a DXF while preserving its entity order cannot check this case.

## 12. Medium — curve provenance still uses origin-anchored coordinate identity

**Claim:** §63: “a place is decided by distance, never by a cell”.

**Source:** `Core/Dxf/StructuralPlanClassifier.cs:575`, `:2022`, `:2059`.

**Smallest input:** A retained loop vertex at `(0,0)` and a curve-marked endpoint at `(0.004,0)` that snaps to it. Move both by 0.003 in the same drawing unit. Place this occurrence in an eight-vertex near-round loop with six other curve-marked vertices.

**Deduction:** The curve-point set uses `(Round(x*100), Round(y*100))`. Initially both occurrences have the same key. After translation their X keys differ, although their separation and snapped loop are unchanged. The retained vertex loses curve credit. At the 80% curved-point boundary, seven of eight credited vertices can become six of eight, changing round-column recognition. This is separate from the explicitly retained `seenEdges` and `SameWall` keys; the unit differential does not compare the full section shape either (finding 24).

## 13. Medium — drafted thresholds still have strict or unrounded boundary decisions

**Claim:** §63: “Every distance or size the reader compares with a tolerance a drafter could draw … is compared to the micron”.

**Source:** `Core/Dxf/PlanLoopBuilder.cs:61`; `Core/Dxf/E2kGeometryComposer.cs:702`; `Core/Dxf/DashedLineJoiner.cs:115`; `Core/Dxf/GridAlignment.cs:470`.

**Smallest inputs and deductions:** A first candidate exactly at `joinTol` is not accepted by the node/joint search: the initial best distance is `joinTol`, a strict improvement is required, and the tie branch requires an already selected candidate. Thus the nominal boundary is excluded. Two loose collinear dashes with a gap equal to the configured 14 inches reach raw `gap <= maxGap`; a drafted 355.6 mm gap represented just above that value is refused rather than rounded through `Within`. Registration support similarly compares squared distance directly against the 100 mm radius squared. These are remaining threshold sites, not a claim that a particular historical drawing exhibited floating-point drift there.

## 14. Medium — two loaded PDF slab rules do not reach the reader

**Claim:** §55: “tier one reads rows — three shared with the DXF side, two seeded by migration 085”.

**Source:** `Core/PdfToSafe/PdfIntakeOptions.cs:209`; `Core/Intake/DrawingIntake.cs:232`; `Core/PdfToSafe/GeometryFilterService.cs:171`, `:1445`.

**Smallest input:** Override `SlabEdgeBridgeMm` to 300 mm and give the slab boundary a 200 mm break; alternatively lower `MinSlabAreaMm2` to 1 m² for an otherwise eligible supported 10 m² loop.

**Deduction:** Options loading reads these values, but `DrawingIntake`'s `Classify` call omits the corresponding optional arguments. Classification uses its compiled defaults, including the 152.4 mm bridge and approximately 37.16 m² minimum area. The override therefore cannot change the PDF reader's decision. Equality between banked rows and defaults masks the missing wiring; `EveryReaderConstantIsTriaged` explicitly does not assert that a row is wired into the caller.

## 15. Medium — a grid drawn heavier than its tendons still consumes the tendon

**Claim:** §61: “the grid is drawn with one pen”; “A tendon the reader never saw reaches no anchor.”

**Source:** `Core/PdfToSafe/SheetFurniture.cs:205`; `Core/PdfToSafe/GeometryFilterService.cs:263`.

**Smallest input:** A page with one consistent 3 pt grid pen, and a labelled 1 pt tendon lying along an axis and ending at an anchor.

**Deduction:** `IsGridPen` accepts any width no greater than twice the axis pen. The 1 pt tendon is classified `GridAxis`, so it does not enter `StrokesOnGrid` for tendon reading. The section excludes tendons drawn with the grid's **same** pen and grids with **two** pens; this input has neither property. The one-bubble test also does not establish the claimed median across several bubble measurements. The heavier-than-grid positive case is covered; the opposite ordering is not.

## 16. Medium — an exported generated model bypasses the yardstick self-output guard

**Claim:** §53: “our own published output is never the yardstick”; §56: “`IsKorGenerated` already refuses it”.

**Source:** `Core/Intake/CorpusAnalyzer.cs:61`, `:64`, `:77`.

**Smallest input:** Place a generated model with the recognised KOR member names at the preferred export location `<yardstickFolder>/<job>.e2k`.

**Deduction:** `YardstickFor` returns an existing preferred export immediately. `IsKorGenerated` and `HasColumns` are only applied while searching the fallback model directory. The corpus can therefore measure generated output against itself and report perfect agreement. The guard's existence does not substantiate the quoted refusal for the preferred export path. No claim is made here about the actual contents of the historical 31168 export.

## 17. Medium — ModelDiff cannot see a wall turn through ninety degrees in place

**Claim:** §63: “all six sets build the same structure shifted” with “no column or wall lost or gained”.

**Source:** `Core/Dxf/ModelDiff.cs:135`, `:168`; `Tests/Intake/TheSameDrawingsShiftedOnThePageBuildTheSameStructureTests.cs` (lost/gained and registration assertions).

**Smallest input:** With unchanged grids and storey, replace a horizontal wall from `(−3000,0)` to `(3000,0)` with a vertical wall from `(0,−3000)` to `(0,3000)`.

**Deduction:** Both wall descriptors are `(centroid X=0, centroid Y=0, max bounding-box span=6000)`. No orientation or axis endpoints survive into matching; both lost/gained lists are empty. Making this change only in the translated model, while translating its grids correctly, passes the differential's registration and member checks. This is a wall-orientation fault triggered by a translation, not a request to cover rotation of the entire drawing. A byte comparison would catch it; the geometric differential would not.

## 18. Medium — ModelDiff's independent nearest matches hide added duplicates

**Claim:** §63: “no column or wall lost or gained”.

**Source:** `Core/Dxf/ModelDiff.cs:33`, `:135`.

**Smallest input:** One wall on a storey before, two coincident copies after; keep the grids and plates identical. The same issue occurs with one column before and two columns at zero and 1 mm after.

**Deduction:** Each member merely needs `Any` counterpart within tolerance. Both new members match the same old member, and the old one matches either new one, so both unmatched lists are empty. `Changed` does not independently compare before/after counts. The differential can pass an added member despite the stored count fields showing the discrepancy. This does not weaken `SixSetsBuildAsBankedTests`' separate byte assertion.

## 19. Medium — yardstick summaries drop a complete recall miss when all our columns lie outside her footprint

**Claim:** §58: “Her columns are all judged against ours as before”; §53 reports “residuals both ways”.

**Source:** `Core/Dxf/ModelYardstick.cs:164`, `:235`, `:319`, `:327`; `Core/Intake/CorpusAnalyzer.cs:292`.

**Smallest input:** Both models have the same sufficient grid labels and L1. She has one column at `(0,0)`; ours has one at `(10000,0)` mm.

**Deduction:** The grid registration is identity. Ours is beyond her footprint, so `OursCompared = 0`. Her one unmatched column still contributes to the raw reverse residuals, but the per-storey figures are built from forward pairs and omit L1. The summary says “no columns on a storey both models name” and suppresses the reverse percentage. Corpus aggregation filters out the entire set on `OursCompared > 0`, losing its zero-of-one recall. The raw reverse result is present; its reporting and population aggregation are the contradiction.

## 20. Medium — “on her wall” means inside its axis-aligned box, even metres away from a diagonal wall

**Claim:** §60: “of which N stand on a wall she modelled”.

**Source:** `Core/Dxf/ModelYardstick.cs:198`, `:282`, `:287`.

**Smallest input:** Her wall runs diagonally from `(0,0)` to `(10000,10000)` mm; her columns at its ends establish the footprint. Ours has an unmatched column at `(0,10000)` under an identity grid registration.

**Deduction:** The column lies in the wall's bounding box, so `DistanceToBox` returns zero and the instrument counts it as standing on her wall. Its distance from the actual wall axis is approximately 7071 mm. It remains unmatched in the main result, but the section diagnostic incorrectly attributes that miss to a column-versus-pier modelling choice.

## 21. Medium — grid-names infers source provenance from a zero displacement

**Claim:** §62: a sheet already left in the model “registers that sheet on itself at (0, 0); it says so and prints the fit against the other members as well”.

**Source:** `Verbs/GridNamesVerb.cs:71`.

**Smallest input:** A correctly grid-registered plan with four irregular column positions already coincident with legitimate model columns on another storey; there is no misplaced copy of the inspected sheet.

**Deduction:** A near-zero member fit triggers the self-standing diagnosis. The follow-up excludes geometrically coincident model positions, without source-sheet provenance, removing the legitimate supports as well. The resulting second fit can be empty or worse despite the original placement being correct. Position alone cannot tell a misplaced sheet's own output from the lower storey's intended support. The printed diagnosis is stronger than the evidence it computes.

## 22. Medium — corpus-query splits view names on a different delimiter from the ledger writer

**Claim:** §55: “the ledger readers through `corpus-query`”; the table attributes its unplaced-view counts to “`corpus-query plan-titles`”.

**Source:** `Core/Intake/CorpusAnalyzer.cs:261`; `Verbs/CorpusQueryVerb.cs:89`.

**Smallest input:** A sheet row whose `DxfFiles` field is `nameless-p01.dxf | S2_1_LEVEL 2 PLAN.dxf`.

**Deduction:** The writer joins multiple names with ` | `, and the recompose path understands that separator. `plan-titles` instead splits on `;`, parsing the entire field as one filename. LEVEL 2 can make that combined string appear named, hiding the nameless first view and counting one item instead of two. This is a source-level flaw in the counting instrument, not proof of any particular historical total and not the already documented run-7 verb/scratch discrepancy.

## 23. Medium — the known vocabulary flake has an unprotected concurrent writer

**Claim:** §56 includes “the unit differential”; §55 claims “the six, byte for byte, after every package”. These checks assume a stable rule vocabulary while they execute.

**Source:** `Core/Dxf/DxfToEtabsService.cs:720`; `Tests/AModelIsTheSameInInchesAndMillimetresTests.cs:27`, `:61`; `Tests/Intake/TheHandoffIsInMemoryAndTheModelIsTheSameTests.cs:25`, `:62`; `Tests/AnotherOfficesWordsTests.cs:97`; `Tests/SheetNamingVocabularyCollection.cs:28`.

**Smallest input:** xUnit runs the office-vocabulary test concurrently with either newly added composing class. Interleave the composing call after `AnotherOfficesWordsTests` installs its office words and before its naming assertions.

**Deduction:** `DxfToEtabsService.Run` writes the process-wide naming vocabulary. These composing classes lack the vocabulary collection, while that collection does not disable parallelism with other collections. The concurrent call can overwrite the office words before FOOTING/LIFT OVERRUN/MEZZ assertions read them. Serialising the vocabulary test with other collection members does not serialize these outsiders. This supplies a concrete source-supported race explaining how the known test can pass alone and fail in a suite; it does not prove that this was the actual historical interleaving.

## 24. Low — the unit differential compares one section dimension and joint counts, not the stated structure

**Claim:** §56: “the unit differential (columns, walls, piers, sections, joints across inches and millimetres)”; “must be the same structure”.

**Source:** `Tests/AModelIsTheSameInInchesAndMillimetresTests.cs:70`, `:74`, `:94`.

**Smallest missed fault:** In the fixture's millimetre output, leave a rectangular section's width `B` at its inch value while converting `D` correctly; retain object and joint counts.

**Deduction:** The section regex extracts only `D`, `WALLTHICKNESS` or `SLABTHICKNESS`, taking one match per line. Width and section shape are absent from the comparison. Joint verification compares the number of coordinate records, not their positions or connectivity. Consequently the proposed width fault, or a displaced joint with unchanged counts and section values, leaves every assertion green. This is a missing assertion, not evidence that the composer currently makes that exact conversion error.

## 25. Low — the ledger “round trip” never calls the typed readers or the claimed manifest-hit path

**Claim:** §53: “the ledger's rows through their CSV”; the named test's WHAT THIS COVERS expands that to “read back equal (through the private readers the analyzer uses, exercised via a manifest hit)”.

**Source:** `Tests/Intake/TheCorpusLedgerRoundTripsTests.cs:13`, `:38`.

**Smallest missed fault:** Keep the fixture's single set and sheet rows and their CSV writers unchanged, but have the typed reader map a numeric field to the wrong property, or have a manifest hit fail to restore the cached sheet rows.

**Deduction:** The test invokes only the CSV string-field parser, checks field counts and selected string values, and never exercises typed row reconstruction or a manifest hit. Either proposed reader/cache fault is invisible. Even record equality is not asserted. The quoting and selected null-field checks are real; the stronger coverage sentence is not supported by the assertions.

---

**Other requested checks and limits of this audit.** These notes qualify coverage; they are not additional findings.

- **Refused clauses:** The centroid fallback is guarded by the small-area/bounding-box checks. Heavy on-grid strokes stay in `StrokesOnGrid`, not `Lines`. The overshoot predicate has its smallest-scheduled-column guard and the rejection note beside it; finding 7 concerns the separate endpoint predicate. Registration uses wall axis ends rather than outline corners. Closed-polyline edges bypass the joiner, and loose touching/overlapping segments still merge, with the rejected “no touching merges” rationale alongside the code. The original broad versions of those refused clauses were not found still active.
- **Closed shapes and wall pooling:** `OfClosedOutline` is provenance from a closed polyline, not proof that the drafter intended a structural outline. Such edges bypass joining regardless of whether the closed object depicts a hatch or pattern. Unused open-chain edges are pooled; concrete-side probes and final wall deduplication also apply. Static review did not establish a smaller complete counterexample for a spurious thinner duplicate or a corridor-spanning wall after all those stages. A centreline alone is not proof of the requisite concrete-containing loop. No drawing-dependent failure is asserted here, and the documented one-face/three-loops loss is not re-reported.
- **Path fates and the bank:** The synthetic path ledger does reach both `StrokeOnGrid` and `SymbolQuadrant` and asserts a fate per path with object references. The issue in finding 1 is their interaction with earlier list compaction, not an unreachable enum value. The six-set bank compares bytes; the comparator gaps in findings 17–18 apply to the shifted-model differential. The frozen reader comparison excludes the new reasons and cannot substitute for interaction coverage. Its unknown-pen setup also does not establish the real heavy-grid-stroke case.
- **Other named checks:** The centroid fixtures assert containment rather than the exact claimed vertex-mean fallback. The no-axis fixtures establish their stated positive displacement cases and low-support refusal; they do not establish building identity or unique sheet support (findings 4–5). The title, unique-view-name, carrier-scale, page-origin, reissue, and synthetic differential/render checks provide their particular positive examples; no additional production fault is inferred merely from broader possible inputs. The vocabulary override and schematic-title exclusions already stated in the brief are not re-reported as discoveries.
- **Instrument scope:** `dxf-inspect --members` creates default classification options in the sheet unit (`Verbs/DxfInspectVerb.cs:17`) and calls the classifier without sheet words or tags (`:63`), as its comment explicitly acknowledges. Its output is therefore a geometry-only observation, not a complete reproduction of rule-loaded, tagged composition. Equal printed members do not prove equal final composition when those inputs differ. `ModelDiff`'s fallback label GUESS likewise does not establish the identity of the inferred translation; findings 17–18 survive even an exact grid registration.
- **Rule 12:** No definite instance of a population number produced by a truncated command was established from the permitted source/document evidence. The explicit four-set diagnostic sample in §53 does not by itself invalidate the separately stated corpus count. The run-7 scratch/verb discrepancy is already disclosed. Finding 22 concerns a delimiter bug, not proof of truncation. Historical census, harness and corpus totals cannot be independently certified without the raw outputs/data that this audit forbids accessing; no reported total has been silently treated as a newly verified measurement.
