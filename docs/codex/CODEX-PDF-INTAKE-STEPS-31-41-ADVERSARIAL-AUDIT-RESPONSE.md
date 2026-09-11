# PDF intake steps 31–41: adversarial audit response

> **Answered 2026-09-11 as intake step 42** — `docs/PdfIntake.md` §51 records what each finding became.
> F1–F3, F6–F24 fixed, each with its counterexample as a test
> (`TheAuditsCounterexamplesForSteps31To41Tests` and the named files); F4 and F5 accepted as stated
> limits; F25's weaker assertions strengthened where named. F7's first fix ("both ends within reach")
> was itself wrong on the six sets — 25 walls drawn a little longer on a second sheet came back — and
> the rule kept is "the earlier wall drew more than half of the later one". Every other fix leaves all
> six models byte-identical.

Audited 2026-09-11, on `develop` at `7e8358390de582fe8f32151bc924432d6e6f332a`, against `50823467^..HEAD` and `docs/PdfIntake.md` §0 and §40–§50. **All findings below are source deductions, not executed reproductions.** No build, test, drawing, model file, database, network, or other repository was accessed. Only this response was created; pre-existing workspace changes were left alone. Prior-art search in `docs/codex` found the requested brief and no existing response for this audit.

Paths below use `Core/` for `Kor.Operations.EngineeringTools.Core/`, `Tests/` for `Kor.Operations.EngineeringTools.Core.Tests/`, and `Cli/` for `Kor.Operations.EngineeringTools.TakeoffCli/`. Line numbers refer to the audited HEAD. Geometry examples use millimetres unless specified; raw shapes can be translated into the plan area, clear of furniture and page borders. Examples involving method inputs state the intermediate data explicitly rather than pretending to have reproduced a PDF read.

The refused clauses were checked at their former locations. The unconditional two-shape rule is absent; its rejection is recorded at `Core/PdfToSafe/GeometryFilterService.cs:479`. The join requires pattern cells, with the global-join rejection at line 675. The stipple rejection is at line 935, and there is no retained dot-counting admission there. Outside-plate members are counted without removal at `Core/Dxf/DxfToEtabsService.cs:2668`, with the rejection recorded at line 2619. The first cut's cross-sheet opening insertion is also absent. These observations do not validate the replacement heuristics; counterexamples follow.

Two limits matter for the instruments: `six_set_diff.sh` calls `members_diff.py` and **`plate_diff.py`**, not `columns_vs_yardstick.py`. Its literal **“byte-identical”** result comes from `cmp`, not from geometric matching. None of the findings below demonstrates a false `cmp` result or establishes that a historical six-set measurement was fabricated.

## 1. High — removing geometry on one storey removes it from every storey a typical plan serves

**Claim:** §43, “A storey that has a tagging sheet takes its walls from the tagging sheets”; §44, “Across the sheets of one storey” and a plate inside another sheet's floor “leaves the floors.”

**Source:** `Core/Dxf/DxfToEtabsService.cs:2507`, especially `2530`, `2548`, `2574`; `2635`, especially `2656`; placement construction at `1640`.

**Smallest input:** two parsed sheets. A typical sheet serves `[L2,L3]` and carries one wall; a tagging sheet serves only `[L2]` and has ten tags and its own replacement wall. For the floor variant, the typical sheet carries a small plate and the L2-only sheet carries a containing larger plate.

**Deduction:** `here` contains references to the same mutable `PlanGeometrySet`. The L2 iteration clears/removes the typical sheet's wall or slab. Subsequent `StoryPlacement`s for **both** L2 and L3 use that modified geometry. L3 loses its only copy although no competing L3 sheet exists. This is not §43's acknowledged incomplete-enlargement coverage on the same storey. The two passes' tests give each geometry only one storey.

## 2. High — “one base per set” discards an independently founded building

**Claim:** §50, “a ladder that reaches none of that chain is a detail's, reported as ‘not chained to a base’ and not placed.”

**Source:** `Core/Intake/SetStoreys.cs:137`–`151`, unchained reporting at `190`.

**Smallest input:** two complete three-level building ladders: A-L2 over A-L1, A-L3 over A-L2; B-L2 over B-L1, B-L3 over B-L2, all rises 3,000 mm. The two buildings have independent foundations and explicit building prefixes.

**Deduction:** both bases reach three names; the alphabetical tie-break selects A-L1. B-L1 is not seeded and neither B storey is placed. A shorter legitimate building also loses to a taller one without a tie. No check distinguishes a detail from another building. The documented exclusions name neither independent buildings nor disconnected complete ladders.

## 3. High — a finish layer makes a concrete assembly a stud partition

**Claim:** §41, “the material and thickness the structural model takes are derived from the words”; §42, “the code's material comes from the card.”

**Source:** `Core/Intake/AssemblySchedule.cs:232`–`240`; `Core/Intake/WallTypeTagging.cs:106`.

**Smallest input:** a confirmed wall card named `EXTERIOR WALL`, with layers in printed order `15.9mm GYPSUM BOARD`, then `200mm CONCRETE WALL`. A plan wall has this card's code beside it.

**Deduction:** the name supplies no material. `MaterialOf` returns immediately on the first recognised layer, `Stud`, without reading the concrete core. Tagging consequently sends the wall to the partition layer. Reversing the layers changes the result. The existing concrete-with-furring test names the concrete in the heading, so it never exercises this branch.

## 4. High — three actual precast piers with narrow joints are deleted as cells

**Claim:** §46, “Three or more shapes read as columns by shape, of ONE size … each edge to edge with the next … are its cells.”

**Source:** `Core/PdfToSafe/GeometryFilterService.cs:504`–`565`.

**Smallest input:** three separately filled 600 × 800 mm precast piers, lower-left coordinates `(0,0)`, `(610,0)`, `(1220,0)`, with 10 mm joints and no matching declared schedule size.

**Deduction:** all three first qualify as shape columns. Their facing gaps are below 25.4 mm and their side overlaps pass. The connected component has size three, so all columns are removed and fated `PatternCell`. The algorithm has no evidence that these are pattern instances. These are neither rotated cells, exactly two cells, nor the known stall-line blocks, which stand far apart.

## 5. High — three rectangular structural walls enclosing narrow shafts are hatch stripes

**Claim:** §47, “three or more filled walls of one thickness and length, parallel, at one pitch across their width, are a hatch.”

**Source:** `Core/PdfToSafe/GeometryFilterService.cs:585`–`643`, particularly the four-thickness pitch limit at `623`.

**Smallest input:** three filled concrete walls, each 1,800 × 300 mm, running along x, with centreline y coordinates 0, 900, and 1,800 mm. They enclose two 600 mm clear service shafts/channels; no column schedule declares this size.

**Deduction:** their lengths and thicknesses agree, their pitch is 900 ≤ 4 × 300, and all three leave `Walls`. No mitre or grey pen is required. This is outside the documented Z-jog exception: these are ordinary rectangles. The test's positive “stripe” example is itself rectangular and has no distinguishing material evidence.

## 6. High — a crossing partition deletes an entire concrete wall

**Claim:** §43, “a wall whose axis midpoint lies inside … a partition footprint another sheet drew is that partition.”

**Source:** `Core/Dxf/DxfToEtabsService.cs:2542`–`2553`.

**Smallest input:** on one storey, sheet A has a horizontal concrete wall from `(0,0)` to `(6000,0)`. Sheet B has a vertical partition footprint spanning x=2950..3050, y=−2000..2000. Neither sheet needs ten tags; give B another structural object so it reaches `parsed`.

**Deduction:** the concrete wall's midpoint lies inside the crossing partition, so the whole 6 m wall is removed. This clause checks neither direction, length coverage, nor whether A has a concrete tag of its own. The crossing-wall assertion in `ASheetThatSaysWhatAWallIsWinsTests` tests the later **wall-to-wall** duplicate clause, not this partition clause.

## 7. High — a short earlier wall suppresses a much longer later wall

**Claim:** §43, “two sheets drawing one wall in one place draw one wall” and “the first copy kept.”

**Source:** `Core/Dxf/DxfToEtabsService.cs:2566`–`2582`.

**Smallest input:** two sheets on one storey, neither tagging. The earlier sheet draws only a pier from `(2400,0)` to `(3600,0)`. The later complete plan draws a wall from `(0,0)` to `(6000,0)`.

**Deduction:** the later wall runs the same way and its midpoint is on the earlier pier. It is removed in full; only 1.2 m of the 6 m run remains. Reversing sheet order retains 6 m instead. No endpoint or coverage comparison establishes that these are complete duplicate readings. The current test uses nearly equal lengths.

## 8. High — an all-partition enlargement is discarded before it can override the key plan

**Claim:** §43, “A storey that has a tagging sheet takes its walls from the tagging sheets”; partitions are “footprints — never a member.”

**Source:** `Core/Dxf/DxfToEtabsService.cs:1406`–`1423`, followed by `1620`.

**Smallest input:** a key plan contributes an untagged wall. Its same-storey enlargement contains ten `KOR_WALLTYPE` tags and the matching `KOR_PARTITION` outline, but zero structural walls, columns, or slabs.

**Deduction:** the enlargement meets the “no structural outlines” condition and is skipped before `parsed.Add`. Its tags and partition footprint never reach the stand-down pass, so the key-plan wall remains. A successfully classified all-stud enlargement is precisely a sheet that can have no structural members. Tests construct `parsed` directly and bypass this admission gate.

## 9. High — a numeric wall identifier is sufficient to erase a real wall

**Claim:** §44, “a wall whose outline holds a dimension word … stating a length” is flagged; the acknowledged exception is a real wall with its **length** written inside it.

**Source:** `Core/Intake/DimensionStrings.cs:40`, `76`–`84`, `117`–`120`.

**Smallest input:** a 200 mm thick horizontal concrete wall containing the horizontal bare identifier `500`, with no grid axes. It is an element/panel identifier, not a length.

**Deduction:** the parser returns a 500 mm bare-number dimension even without a matching span. `StandDownWalls` ignores `BareNumber`, `AxisGapMm`, and `Agrees`; 500 > 200 + 25.4 flags the wall. This is a different false positive from the already disclosed length-inside-wall case. The dimension reader's own summary says a bare count/mark requires an agreeing span, but this consumer applies no such restriction.

## 10. High — a flagged dimension string still propagates a partition type to another wall

**Claim:** §44, “the tagging neither types it nor makes it a partition”; “a flagged wall neither typed nor a partition.”

**Source:** `Core/Intake/WallTypeTagging.cs:61`–`90`, with the exclusion applied only at `103`.

**Smallest input:** a flagged dimension-string wall A along `(0,0)`–`(2000,0)`, and an unflagged wall B along `(2100,0)`–`(4000,0)`. Place a stud tag at `(500,0)`, within A's reach but 1,600 mm from B; use fewer than ten tags.

**Deduction:** the temporary `codes` array types A before checking its flag. `ContinuesRun` propagates that stud code across the 100 mm gap to B. Only the final output loop clears A's code; B remains a partition and is omitted from structure. The one-wall flagged test checks A's final state, not its influence on neighbours.

## 11. High — one roof plan is placed on every storey whose name contains ROOF

**Claim:** §45, “a roof plan … goes to the storey NAMED roof”; “An elevator roof takes the storey above that”; §50 adds distinct named roof levels.

**Source:** `Core/Dxf/PlanSheetNaming.cs:238`–`243`.

**Smallest input:** eligible stories `[ELEVATOR ROOF, ROOF, L6]`, plus separate `ROOF PLAN` and `ELEVATOR ROOF PLAN` sheets with no level numbers.

**Deduction:** both sheets return `[ELEVATOR ROOF, ROOF]` from the substring match. `IsElevatorRoof` is considered only in the fallback, which is bypassed. Both plates and their members can be assigned twice. The elevator test has only numbered storeys; the named-roof test has exactly one name containing ROOF. This is not the known `UPPER ROOF` vocabulary gap or duplicated PENTHOUSE statement.

## 12. High — longest-phrase matching consumes a numbered roof level's number

**Claim:** §50, “a word or phrase … is a level named by itself,” and the motivating labels have “nothing to the right but the level line.”

**Source:** `Core/ScheduleGridReader.cs:162`–`174`, `274`–`282`.

**Smallest input:** a ladder with `LEVEL 1`, `LEVEL 2`, and `ROOF LEVEL 3`. On the top row, give separate 20 pt wide tokens ROOF, LEVEL, 3 with centres x=100,130,160; align the other LEVEL tokens at x=130. Use distinct y rows and a valid scale.

**Deduction:** at the LEVEL token the longest match is `ROOF LEVEL`. It is returned as an already supplied value, and `LadderAt` never reads the explicit `3`. The numbered ladder therefore tops out at the name `ROOF LEVEL`, not L3. The separate ROOF token lies in a different, one-row column that fails `MinRows`. Before the change, the LEVEL token matched the numeric label branch. Nothing checks the name-only premise that no level number follows.

## 13. High — splitting a tagging sheet changes its authority without undoing its decisions

**Claim:** §42, “SheetViews carries the per-wall type into each view”; §43, “A sheet with at least … (10) codes has tagged its walls,” and the storey takes walls from tagging sheets.

**Source:** `Core/Intake/WallTypeTagging.cs:98`; `Core/Intake/SheetViews.cs:167`, `222`–`225`, `248`; `Core/Dxf/DxfToEtabsService.cs:2521`–`2524`.

**Smallest input:** one sheet with two plan views, each carrying six tags, plus an untagged key plan for one view's storey. Give that view a concrete wall so it survives admission to `parsed`.

**Deduction:** intake sees twelve tags and partitions its untagged walls under the tagging-sheet rule. `Split` retains those decisions but divides the tags into six per exported view. The composer recomputes the threshold on each view and recognises neither as a tagging sheet. Thus the same source sheet is authoritative for deleting untagged intake walls but not authoritative for the storey-wide stand-down. No test traverses tagging → split → composer.

## 14. Medium — stripe removal leaves dangling doorway-to-pier indices

**Claim:** §47, “the parallel lists kept the same length,” with stripes “leaving the walls”; the audit also requires doorway index integrity.

**Source:** `Core/PdfToSafe/GeometryFilterService.cs:638`–`646`; doorway creation at `346`–`347`; `Core/Intake/SheetViews.cs:228`–`229`.

**Smallest input:** three 5,000 × 300 mm filled horizontal walls at y pitches of 600 mm. Paint an 800 mm doorway mask across each at x=1800..2600. Each becomes a 1,800 mm pier followed by a 2,400 mm pier, with doorway `FirstPier` values 0,2,4.

**Deduction:** each three-pier family meets the stripe test, so all six walls are removed. The remapping loop updates a doorway only when its first pier **survives**. All three doorway records remain, pointing at an empty wall list; their `Doorway` fates still claim read openings. With a later survivor the stale number can identify the wrong wall, and view splitting can attach the doorway to that survivor. The stripe tests have no doorway-bearing walls.

## 15. Medium — only one absorbed path receives the joined face's eventual fate

**Claim:** §47, “their paths' fates point at it”; §46, `EveryPathHasExactlyOneFateTests` carries “the re-pointed column fate,” while its class promises object indices pointing at the objects made.

**Source:** `Core/PdfToSafe/GeometryFilterService.cs:793`–`797`, `996`–`999`, `1182`–`1187`; the same one-path map appears at `1373`–`1381` for slab edges.

**Smallest input:** three 900 × 1200 cells stacked from y=30000 to 33600 at x=60000. Inside them, one face at x=60300 is split at y=31200 into touching pieces; a parallel face at x=60500 runs the full 3,600 mm. Use one light pen.

**Deduction:** the pieces join, then pair into a wall with the other face. Both original piece fates initially reference the joined line, but `Dictionary<int,int> lineToPath` overwrites the first path with the last. Only the last becomes `BecameWallFace`; the other stays `EmittedAsLine`/Unaccounted, even though the exporter suppresses that line as a wall face. This does not necessarily throw, but corrupts what the ledger says was read. The join test checks fates on a line that remains a line; the pairing test uses unsplit faces.

## 16. Medium — the “cut-short end cell” clause also consumes a larger adjoining column

**Claim:** §46, “a shape of the run's width abutting it is the run's last cell, cut short where the wall ends.”

**Source:** `Core/PdfToSafe/GeometryFilterService.cs:524`–`529`, `544`–`550`.

**Smallest input:** three 600 × 800 mm cells side by side at x=0,600,1200, y=0; append a shape column 600 × 1000 at x=1800,y=0. No declared sizes.

**Deduction:** the fourth shape shares width 600 and abuts the run, so it is removed although it is **larger**, not cut short. `SharesASide` accepts either dimension, without tying it to the contact direction or bounding the other dimension. Because newly accepted cells can recruit later items, this expansion can continue beyond one end cell and depend on input order. The existing test supplies only a genuinely shortened end.

## 17. Medium — the new filled-wall rule still admits a substantial taper

**Claim:** §47, “the long edges are opposite and parallel (a taper is not)” and “a taper … refused.”

**Source:** `Core/PdfToSafe/GeometryFilterService.cs:53`–`77`, particularly `69`; `Tests/Intake/TheAuditsCounterexamplesTests.cs:39`.

**Smallest input:** a filled tapered paving/ramp stripe with points `(0,0)`, `(6000,0)`, `(6000,400)`, `(0,300)`, away from furniture and without a matching column size.

**Deduction:** the long-edge direction cosine is about 0.999861, above the 0.9998 threshold. The ends' along projections are small and the area, 2.1 m², exceeds half its box. It is accepted as a filled wall despite a 100 mm, 33% change in thickness. F1's test increases thickness by 150 mm over 6 m, just beyond the angular threshold, and does not establish rejection of the broader taper class. This is a different non-wall input from the known accessible-aisle rectangle.

## 18. Medium — ordinary remarks before the first layer disappear from a “whole” card

**Claim:** §41, “the card is read WHOLE — every layer, both ratings, the references, the remarks.”

**Source:** `Core/Intake/AssemblySchedule.cs:214`–`225`.

**Smallest input:** a confirmed card whose first body line is `INSTALL AFTER SURVEY`, followed by `- 200mm CONCRETE WALL`. Put both lines inside the card box on separate baselines.

**Deduction:** the first line is neither a rating, a column heading, nor a layer. It does not start with NOTE, and both `remarks` and `layers` are empty, so it is dropped without a record. Moving the exact same remark below the first layer preserves it. The card test has no ordinary remarks assertion, and its layer assertions mostly count strings rather than checking their content.

## 19. Medium — a metric-only decimal thickness is not read

**Claim:** §41, “the thickness is the name's, else the thickest layer of the material.”

**Source:** `Core/Intake/AssemblySchedule.cs:76`–`77`, `260`–`282`.

**Smallest input:** a concrete card named `WALL` with one layer `203.2mm CONCRETE WALL` and no inch equivalent. Equivalently put that decimal thickness in a concrete heading.

**Deduction:** the millimetre regex accepts only two-to-four **integer** digits immediately before mm, and its lookbehind prevents matching a suffix of the decimal. There is no inch fallback in this input, so thickness is null. The decimal test covers 7.5 inches; the 63.5 mm example happens to carry `(2.5")`, which masks the unsupported metric decimal.

## 20. Medium — the advertised vocabulary wiring stops at two consumers

**Claim:** §50, “pdf-levels, pdf-takeoff and pdf-assemblies read through them”; §41, thickness is derived from the material words.

**Source:** `Cli/Program.cs:279`–`283`; `Core/Intake/AssemblySchedule.cs:85`, `228`, `260`–`265`; extension construction at `Core/PdfToSafe/PdfIntakeOptions.cs:173`.

**Smallest input:** an extension adding the structural word `RAMMED EARTH`, and a confirmed card with generic heading `WALL` and layer `300mm RAMMED EARTH`.

**Deduction:** `pdf-assemblies` calls the default-only `ReadSet(args[1])` overload and never loads the options, so it cannot recognise the added word. Even when `Read` receives the extended vocabulary directly, `Card` can recognise the material but `ThicknessOf` calls `MaterialIn(layer)` without that vocabulary; thickness remains null. Applying the pending rows would not repair these call paths. This is independent of the known fact that migration 082 has not been applied.

## 21. Medium — the geometric diff can report zero changes for different members or plates

**Claim:** §46, “lost/gained members per storey with positions”; §47, “six_set_diff.sh … is the one-line-per-set reading that follows every run”; §0 describes `plate_diff.py` as saying which plate moved.

**Source:** `docs/etabs-handoff/members_diff.py:23`–`32`, `56`–`65`; `docs/etabs-handoff/plate_diff.py:48`–`53`; `docs/etabs-handoff/six_set_diff.sh:17`.

**Smallest inputs:** (a) replace a horizontal wall from `(0,0)`–`(2000,0)` with a vertical one from `(1000,-1000)`–`(1000,1000)` on the same storey; (b) add a duplicate column at an existing column's base; (c) add a new storey containing only a column; (d) translate a floor plate without changing its area.

**Deduction:** (a) has the same wall centroid; (b) collapses into the same set entry; (c) is never visited because iteration uses only the first file's storeys; (d) has the same rounded square-foot area string. These can give zero geometric changes despite different models. Wall endpoints, member sections, spans, and multiplicity are not compared. The `cmp` branch still correctly refuses to call such files byte-identical; the defect is the apparent all-zero explanation after that branch.

## 22. Medium — modal alignment hides actual movement and invents movement after a frame shift

**Claim:** §46, the member instrument is “tolerant of the one-unit re-rounding a moved offset causes”; §48 says the six-set diff shows model shifts and columns “moved.”

**Source:** `docs/etabs-handoff/members_diff.py:40`–`55`.

**Smallest inputs:** for a false negative, move the only column from `(0,0)` to `(100,0)` while leaving its grid fixed. For a false positive, use columns at x=0,1000,2000, y=0 and translate the entire model **and grid** +1000 mm in x.

**Deduction:** in the first case, +100 is inferred to be a frame change and subtracted, leaving no lost/gained column. In the second, nearest-neighbour votes are +1000,0,0, so the inferred shift is zero; the script reports a loss at 0 and gain at 3000 although column-to-grid positions are unchanged. It reads no grid and cannot distinguish member movement from origin movement. This is not two-unit rounding tolerance.

## 23. Medium — the yardstick comparison conflates buildings and grid systems

**Claim:** §48, “frames matched by grid name” and “2,211 columns across the shared storeys.”

**Source:** `docs/etabs-handoff/columns_vs_yardstick.py:45`, `49`–`59`, `74`–`84`.

**Smallest input:** two otherwise identical multi-building files with A-L1 carrying a column at `(0,0)` and B-L1 one at `(10000,0)`. Give both files two shared X and two shared Y axes with identical coordinates, so the grid offset is unambiguously zero; enumerate B-L1 last in the yardstick.

**Deduction:** both storeys normalise to L1 and the dictionary retains only B's columns. A's unchanged column is compared with B's and acquires a 10,000 mm residual. Conversely, moving A's column to B's position produces zero residual despite being wrong. The grid dictionary also ignores the quoted grid-system name, so repeated `(label,direction)` pairs across systems overwrite each other. This proves a possible measurement error, not that the published 31168 residual numbers used colliding names.

## 24. Medium — the six-set member totals count a capped sample of printed lines

**Claim:** §47, “six_set_diff.sh … is the one-line-per-set reading that follows every run”; §46 calls the input instrument “lost/gained members per storey.”

**Source:** `docs/etabs-handoff/members_diff.py:68`–`70`; `docs/etabs-handoff/six_set_diff.sh:16`–`17`.

**Smallest input:** one storey loses thirteen distinct walls whose centroids have no nearby survivors; leave a column unchanged to hold the inferred offset at zero.

**Deduction:** the member script prints twelve `LOST wall` lines and `... and 1 more`. The shell counts only lines matching `LOST wall`, and announces **12 walls lost**, not 13. Column changes have the same cap, separately for lost and gained per storey. This is a proven Rule 12 failure in the current population-summary command, even though the visible `grep -c` itself looks like a census.

The historical prose needs a narrower conclusion. §48's “tower storeys 96% within 100 mm” is not an all-tower aggregate emitted by the committed yardstick script: it prints an overall aggregate and at most eight individual storeys (`columns_vs_yardstick.py:91`). No quoted complete derivation establishes whether that phrase came from a separate census or the displayed sample. The permitted sources do **not** prove that historical figure false or sample-derived. Likewise, `pdf_words_near.py` truncates the displayed page list but counts the complete page set; that display truncation alone does not invalidate §45's 5-of-11 numerator. Drawings/logs would be needed to verify the historical measurements.

## 25. Medium — several promised test assertions are absent or materially weaker

**Claims:** §40, “the request's scale is the fallback” and conflicting scales count as none; §41, cards “each read whole”; §43, “the first copy kept”; §44, a ring “not becoming an opening”; §47, “the parallel lists kept the same length”; §49, grid labels “at those coordinates in the model's unit.”

**Source and smallest demonstration:** these are source-level mutation counterexamples, not executed mutations. On the existing fixtures, give every exported grid axis a common translation, corrupt A/B coordinates, or change axis 3's direction: `Tests/Intake/TheDrawingsOwnGridIsWrittenToTheModelTests.cs:67`–`76` still has five labels, checks only axis 2's direction and the **difference** between axes 2 and 3. It does not assert the advertised coordinates. Replace card layer contents while keeping their counts, or insert the removed ring into the **container's** openings instead of the enlargement's: the named assertions below do not check those properties. The production grid writer is not alleged to perform those mutations.

The complete comparison of the **14 test files named in §40–§50** is below; a missing adversarial case is distinguished from an assertion the summary actually promises.

| Named test file (under `Tests/`) | Summary versus assertions |
|---|---|
| `Intake/ASheetIsReadAtTheScaleItStatesTests.cs` | Lines 22–25 promise a conflict case, but the only three tests at 44–61 check imperial, metric, and null. No conflict fixture calls `StatedScaleDenominator`; no test observes the request fallback or scaled geometry. The older F8 tests the underlying reader, not this new call path. |
| `Intake/AnAssemblyScheduleIsALegendOfCardsTests.cs` | Lines 63–94 use three cards, not §41's “one below each” of two upper cards. Lines 76,88,93 assert layer counts, not their exact text or order. B8 has no rating or thickness assertion; S8.1 no thickness assertion; ordinary remarks and Page are not checked. The test therefore does not establish “each read whole.” Material precedence, mismatch reporting, no-confirmation refusal, kind, and the specific thickness examples do have assertions. |
| `Intake/AWallIsWhatItsTagSaysItIsTests.cs` | Basic nearest/out-of-reach/material flags are asserted. The claimed run “through doorways” uses 100 mm gaps at 87–99, not doorway records or a normal door opening; the first continuation is also close enough to one direct tag. The last pier does exercise propagation. Tags are counted, not checked for exact code/coordinates; “modelled” is inferred from flags, with export expressly excluded. |
| `ASheetThatSaysWhatAWallIsWinsTests.cs` | The class promises “inside (or a hand's width from)” at 15–17, but the partition fixture at 64–77 tests inside only. “First copy kept” checks only one surviving first-sheet wall at 90, not its endpoints or identity. Crossing-wall preservation checks one endpoint at 92. Tagging and no-tagging clauses and warning strings are asserted for one storey each. |
| `Intake/ADimensionStringIsNotAWallTests.cs` | The four fixtures assert the stated flags, counts, thickness exception, vertical/horizontal distinction, and final untyped/nonpartition state. Export is honestly excluded. They do not exercise bare identifiers or propagation through a flagged neighbour (findings 9–10); those are missing adversarial cases, not claims that these tests contain them. |
| `StructureStandsOnAFloorTests.cs` | “Not becoming an opening” checks only `enlargement.Openings` at 51, not the containing key plan's openings. “Left where it stands” checks member counts and diagnostic coordinates at 58–77, not unchanged stored geometry. No-plate silence and outside-largest wording are asserted. No multi-storey fixture exists. |
| `Intake/ARoofPlanDrawsTheStoreyAboveTheHighestPlanTests.cs` | All enumerated simple numbered-roof, fallback, named-roof, numbered-elevator and explicitly tagged-building cases have assertions. The named-roof case has one matching name and the elevator case none; their interaction in finding 11 is not covered. The building test has no untagged higher plan. |
| `Intake/APatternsCellsAbutAColumnStandsAloneTests.cs` | Runs, short end, two shapes, separation, corners and the survivor index are asserted. `ADeclaredColumnBesideARunIsNeverACell` at 90 contains three declared columns and **no shape-cell run**, despite its name and summary. No test checks every parallel list or an oversized end shape. |
| `Intake/EveryPathHasExactlyOneFateTests.cs` | Checks individual fixture fates and object associations, enum/disposition coverage, mode cases and appending. The joined-face composition in finding 15 is absent. The append case has one existing and one new column, so it never exercises the new three-column removal pass. |
| `Intake/AWallIsWhatAFillPatternFillsTests.cs` | The join, gap, pen, no-pattern and stripe cases have assertions. Line 62 checks only widths, colours and annotation lengths, not `LineSectionHints`; joining and becoming a wall are separate fixtures. Doorway remapping is untested. Thus §47's parallel-list wording is wider than the asserted subset. |
| `Intake/TheAuditsCounterexamplesTests.cs` | Revised F1 asserts the particular taper/bow-tie/long-end refusals and positive rectangle/chamfer/mitre fates; it does not establish every taper's rejection (finding 17). The other named F3/F4/F5/F6/F7/F8/F11 assertions exist; they are not new gates for joined paths, new partition layers or per-sheet scale application. |
| `Intake/AColumnStandsAtTheCentreOfItsOutlineTests.cs` | The three tests assert the described rectangle centroids, two-point midpoint and shape-column centre. The class summary is narrower than its broad test name and accurately says rectangle. No additional defect in the stated rectangle-centre fix is established by this audit. |
| `Intake/TheDrawingsOwnGridIsWrittenToTheModelTests.cs` | Checks label presence/count, one direction, one spacing, and warning. It does not assert the advertised absolute coordinates, Y spacing, remaining directions, or grid/member registration; the minimal mutations above escape those assertions. |
| `Intake/ALevelMayBeNamedByAWordAloneTests.cs` | Roof rise, ROOF LEVEL membership, stacked-name rise and absence of the stated phantom, and one selected base with an unchained detail are asserted. Number-following-name and independent buildings are not fixtures. The real two-line PENTHOUSE issue is explicitly excluded and is not re-reported here. |

For index/state scope, the normal classification order removes cells/stripes and joins lines **before** tagging and before face/slab index maps are built. It would therefore be wrong to call every uninitialised optional metadata list a new removal-induced index bug. In particular, `DrawingIntake` does not call tagging without assemblies (`Core/Intake/DrawingIntake.cs:222`), so wall type lists may be empty by design, and exporter accesses are guarded. `SheetViews.Split` pads copied wall flags; `DxfExporter` appends each accepted wall's colour, markup and partition flag together after skipping dimension strings (`Core/PdfToSafe/DxfExporter.cs:166`–`177`). The source does not establish the blanket invariant that every named parallel list has equal length at every exit; findings 14–15 identify concrete broken relationships instead.
