# Intake steps 63–71: adversarial source audit

Audited `develop` at `f23a9dd2`. All findings below are **source deductions**, not executed reproductions or observed drawing/model failures. No build, tests, drawings, model/data files, database or network were accessed. Only this response was edited. The brief's known defects are excluded.

Paths below use **Core/** for `Kor.Operations.EngineeringTools.Core/` and **Tests/** for `Kor.Operations.EngineeringTools.Core.Tests/`.

## 1. High — The open-face-pair cap makes its wall floor impossible to satisfy in millimetres

**Contradicted claim:** §72, lines 3641–3644: “The composer now carries the same half inch as the reader” and “`WallFloor` = the row less its slack, at the decomposer's face-pair separation, the classifier's ribbon band, open face pairs and the filled rectangle”.

**Source:** `Core/Dxf/StructuralPlanClassifier.cs:2236–2237`; compare the rectangle gate at `2353–2355` and `Core/Dxf/WallOutlineDecomposer.cs:93–94`.

**Smallest input:** At the open-face-pair gate, a parallel pair separated by 203.2 mm, with sufficient overlap and millimetre options: minimum thickness 152.4, slack 12.7, maximum thickness 1524. The equivalent inch input has separation 8, minimum 6, slack 0.5, maximum 60.

**What the code does:** The millimetre floor is about 139.7, but `Math.Min(options.MaxWallThickness, 18.0)` makes the ceiling **18 mm**. The pair is refused; indeed no separation can satisfy both limits. The inch input has a 5.5–18 inch interval and passes this thickness gate. The rectangle and decomposer gates do not have this literal-18 ceiling. This is a disagreement between representations of the same wall, far from any floating-point boundary; it does not depend on whether the later overlap tests accept the pair.

## 2. High — The read-cache whitelist omits code that determines the cached read

**Contradicted claim:** §76, lines 3882–3883: “The cache is honest by construction: it cannot serve a read made under different reader code.”

**Source:** `Tests/Intake/SixSetReadCache.cs:22–31,46–62,81–82,89–96,114`; `Tests/Intake/SixSetsBuildAsBankedTests.cs:89–100`. §76, lines 3855–3859, explicitly identifies `VectorPageReader.Walk` and `Derive` as the source of the page reads.

**Smallest input:** One valid cached set, unchanged PDF bytes, scale, options, sheet CSV and DXF filenames; a reader change confined to `Core/VectorPageReader.cs` that changes a derived word or path.

**What the code does:** The hash enumerates `PdfToSafe/`, selected `Intake/`, and eleven named `Dxf/` files. It never hashes the Core-root page reader. The expected fingerprint therefore remains equal to the saved fingerprint, `Read` accepts the old views, and the gate takes `Recompose` instead of reading the changed page representation. A matching old model can pass without exercising the reader change.

There is also a directly visible excluded dependency: `CorpusAnalyzer.cs` is excluded, although cache saving calls its `SheetRows`/`WriteSheetCsv` and cache loading calls its `ReadSheetRows`/`DxfFilesOf`. A change solely to the saved-row projection does not invalidate an existing manifest either; hashing the old CSV's bytes verifies those old bytes, not the current projection. The full transitive read call graph was outside the permitted source ranges, so this is not an exhaustive missing-dependency list.

## 3. High — Starting recovery deletes the killed run's surviving partial ledger

**Contradicted claim:** §76's heading: “a run that dies keeps its ledger”; lines 3887–3889: “every finished set appends its row to `ledger-sets.partial.csv`”.

**Source:** `Core/Intake/CorpusAnalyzer.cs:178–182,266,475–483`.

**Smallest input:** A killed run has successfully appended one finished set. A subsequent recovery or selected-job run starts in the same work root and is killed before its first set finishes.

**What the code does:** The second run unconditionally deletes `ledger-sets.partial.csv` at line 181, before entering the per-set loop. Nothing is appended until a new set reaches line 266. The previously durable row is lost even though the recovery produces no replacement row. If recovery instead completes one selected job, its partial file contains only that new run's row. The lock protects appends within a run; it does not preserve the previous run's evidence. This is separate from the known sorted-ledger overwrite and the known seventeen pre-loop omissions.

## 4. High — Recognized dimension strings can make a concrete page qualify as wood

**Contradicted claim:** §77, lines 3913–3916: “a sheet with two thirds or more of its walls read from unfilled pairs, and at least twenty of them, is a wood plan” whose stud walls are moved to the partition layer.

**Source:** `Core/Intake/DrawingIntake.cs:259–272`; `Core/Intake/WallTypeTagging.cs:65,108,135–147`.

**Smallest input:** Geometry with twenty face-pair entries already marked `WallIsDimensionString = true`, plus one genuine filled 140 mm wall; the twenty entries remain indexed in `WallFaceLines`. Partition flags initially contain twenty-one false values; the minimum wall thickness is 152.4 mm.

**What the code does:** `StudWallsOfAWoodPlan` counts twenty distinct face-pair indices against twenty-one wall-list entries. It passes both thresholds and marks the genuine 140 mm wall as a partition. It never consults `WallIsDimensionString`. The sheet's actual walls are entirely filled: its unfilled objects have already been identified as dimension strings. The call order explicitly performs dimension-string stand-down first, and `Apply` explicitly excludes those flagged entries from ordinary wall tagging, so their exclusion is understood elsewhere in this same path. The new sheet-kind decision reintroduces them as evidence for removing real walls.

## 5. High — A dotted sheet number supplies a false numbered-building tag

**Contradicted claim:** §78, lines 3944–3946: “`PlanSheetNaming` strips a sheet number glued to its title … before every numeric reader”; §79, lines 3989–3991, describes the added numbers as building tags.

**Source:** `Core/Dxf/DrawingVocabulary.cs:245–247`; `Core/Dxf/PlanSheetNaming.cs:127–147`.

**Smallest input:** The title `S2.3-LEVEL 2`.

**What the code does:** Building detection runs on the unstripped name. `PrefixBuilding` matches `3-LEVEL 2`: the preceding dot satisfies `(?<![A-Z0-9])`. `Parse` therefore records building tag **3**. Only afterwards does `SheetNumberPrefix` correctly remove `S2.3-`, leaving `LEVEL 2` for the storey reader. The returned sheet has level 2 and building tag 3, contaminating subsequent building restrictions with a digit that belonged to the sheet number throughout.

## 6. High — A building-prefixed parkade is recognized by the ladder but cannot match its plan

**Contradicted claim:** §79, lines 3992–3995: “`ModelYardstick.BuildingPrefix` … is any tag before a storey word” and `NamedForAnotherBuilding` “uses that one definition”; lines 4000–4002 say tagged sheets keep to their building's storeys when the model names buildings.

**Source:** `Core/Dxf/ModelYardstick.cs:96,458–478`; `Core/Intake/StoreysFromPlans.cs:98–102,121`; `Core/Dxf/DrawingVocabulary.cs:274–276`; `Core/Dxf/PlanSheetNaming.cs:342–349,392–419`.

**Smallest input:** A chain with storey `1-P1` and a plan titled `LEVEL P1 PLAN BLDG 1`.

**What the code does:** The shared normalizer recognizes building 1 and strips the chain name to `P1`. The ladder considers the plan's parkade covered and retains `1-P1`. The composer, however, applies the anchored `ParkadeStory` regex to the **unstripped** name `1-P1`; it fails. Because the sheet has parkade levels but no ordinary levels, line 349 skips ordinary-level matching. The tagged fallback repeats the same failed parkade match and skip, then returns an empty list at line 419. This outcome does not depend on the earlier building-eligibility helper: even an eligible `1-P1` has no successful parkade-matching path.

## 7. High — A shared roof disappears when its lower plan has no building tag

**Contradicted claim:** §80, lines 4048–4051: “on a plan-named ladder every building's roof is the shared ROOF”.

**Source:** `Core/Intake/StoreysFromPlans.cs:77–82,121–123,141–156`.

**Smallest input:** No elevation chain; two plans: `LEVEL 1 PLAN` and `ROOF PLAN BLDG 1`.

**What the code does:** The level plan adds L1, but adds nothing to `highestOfBuilding` because it has no tag. The roof adds tag 1 to `roofOfBuilding` without setting the untagged `roof` flag. The roof loop then fails `highestOfBuilding.TryGetValue` at line 145 and continues **before** reaching the shared-storey branch. The ladder is just L1; no ROOF is inserted. A building tag on the lower plan changes the result even though both inputs describe the shared ladder that step 70 says should govern roof creation. The numbered-buildings test supplies a tagged level plan for every roof tag, so it does not exercise this case.

## 8. Medium — Sharing building roofs also discards the distinct elevator-roof storey

**Contradicted claim:** §72's resolved B6 finding, line 3660, fixes “a building's main roof and elevator roof collapsed to one `<TAG>-ROOF`” by placing “`<TAG>-ELEVATOR ROOF` above `<TAG>-ROOF`”; §80 changes building ownership of roofs to shared ownership on a plan-named ladder.

**Source:** `Core/Intake/StoreysFromPlans.cs:79–82,142–156`.

**Smallest input:** No chain; `LEVEL 1 PLAN BLDG 1`, `ROOF PLAN BLDG 1`, and `ELEVATOR ROOF PLAN BLDG 1`.

**What the code does:** Both roof sets contain tag 1 and its highest ordinary plan exists. With no building-prefixed storey, the first pass inserts shared ROOF. In the elevator pass, `suffix == "ROOF"` is false; execution reaches the unconditional `continue` at line 156. The ladder is L1, ROOF, with no elevator-roof storey. The correction to roof ownership thus suppresses the distinct upper storey that B6 required; the distinction survives only in the building-prefixed branch below that continue.

## 9. Medium — A disconnected floor-word cycle does not leave the row standing

**Contradicted claim:** §80, lines 4043–4046: “Two bottoms, a cycle, a word shown over itself, two stories about one word still leave the row standing”.

**Source:** `Core/Dxf/DrawingVocabulary.cs:132–162`.

**Smallest input:** Default vocabulary and three titles: `GROUND FLOOR SHOWING LEVEL 4 FRAMING OVER`; `MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER`; `UPPER FLOOR SHOWING MAIN FLOOR FRAMING OVER`.

**What the code does:** The map contains GROUND → 4 and the disconnected cycle MAIN → UPPER → MAIN. GROUND is the sole bottom, so the bottom-count guard passes and the walked chain is only GROUND. The validation at line 153 checks an edge only when its upper word is in that walked chain; both cycle edges escape it. The one-word numeric-anchor exception passes, and the returned vocabulary changes GROUND from 1 to **3**, despite the cycle. The cycle check covers cycles attached to the walked chain, not every cycle in the set's map.

## 10. Medium — The wood-plan tests do not test rejection by the two-thirds condition

**Contradicted claim:** §77, lines 3925–3926: “`AWoodPlansStudWallsArePartitionsTests`: the sheet-kind decision by share and count”.

**Source:** `Tests/Intake/AWoodPlansStudWallsArePartitionsTests.cs:44–57,63–70`; production gate at `Core/Intake/WallTypeTagging.cs:139`.

**Smallest missing input:** Twenty unfilled 139.7 mm pairs and twenty filled 152.4 mm walls: the count qualifies, but the 50% share must not qualify.

**What the code and checks do:** Production currently rejects this input correctly. The existing positive fixture has 32 unfilled walls out of 40. Both negative fixtures—15 of 43 and 4 of 5—fail the twenty-pair minimum before the share condition matters. Removing the share condition altogether would preserve every assertion in this file, while incorrectly partitioning the twenty unfilled walls in the missing input. Exactly twenty of thirty, the inclusive two-thirds boundary, is also absent. The file does cover six filled 140 mm bands in the slack window; that is not the gap.

## 11. Medium — The vocabulary test can pass with a plain static when the barrier times out

**Contradicted claim:** §81, lines 4092–4094: “two flows set different words, meet at a barrier, and each reads its own through `Parse`; proved by breaking it — with a plain static it fails every time”.

**Source:** `Tests/Intake/TheVocabularyInForceIsTheCallersTests.cs:24–45`, especially the ignored return value at line 34.

**Smallest input:** The existing fixture with the plain-static fault, under this schedule: flow 1 (`MAIN=2`) starts first; flow 0 (`MAIN=1`) is not scheduled until flow 1's ten-second barrier wait has timed out and flow 1 has read its value.

**What the code does:** `SignalAndWait(TimeSpan)` returns false on timeout, and the test proceeds. Flow 1 records its own words and level 2. Flow 0 later installs its own vocabulary, also times out without a partner, and records its words and level 1. The caller finally sees the last installed MAIN=1. All five assertions pass with the faulty process-wide static. The timeout result must be successful for the test's simultaneous-setting argument to hold; the present assertions do not establish that the flows met.

Scope limits: the allowed ranges did not include `PdfOnlyBuild.WriteSheets`/`WriteLevels`, the vocabulary setters/restorer in `DxfToEtabsService.Run`, the analyzer's vocabulary initialization, the page-record/cache bodies, `TryJoinByExtending`, the definitions of `LoopGeometry.Within`/`Beyond`, or the column-construction/deduplication and CLI-listing sites. Their bodies were not reviewed. The omitted page-reader dependency in finding 2 is identified by name from §76, as the brief permits; a complete read call graph, execution-flow initialization trace, extension-bound proof, exact floating-point boundary comparison and end-to-end origin count remain unestablished. The visible BridgeChains candidate list is sorted and searches the 3 × 3 neighborhood; no ordering defect is established by that excerpt. The explicit roof insertion logic also prevents a shared-roof insertion from newly introducing a building prefix, so no ladder/composer disagreement was asserted merely from their tests occurring at different times.
