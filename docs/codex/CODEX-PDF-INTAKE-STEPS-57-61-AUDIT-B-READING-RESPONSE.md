# Intake steps 57–61 — audit B response

All findings are **source deductions, not executed reproductions**. Read the brief's documentation excerpts and source/test scope at HEAD `af7b7821`, rather than the handover's `d42ad436`; no commit-range comparison was made. For the roof trace, also inspected `MatchStories` and `StoryBelongsToBuilding`, expressly named in question C. No builds, tests, drawing/model/CSV files, database, network, other repository, or brief A source investigation was performed. Examples are constructed inputs, not observations of corpus frequency.

## 1. High — a numeric framing-over clause becomes the plan's own storey

**Claim:** §66: “what a plan is the plan *of* is said before its framing-over clause”; §69: “`NamesAPlan` judges the part before the framing-over clause”.

**Source:** `Kor.Operations.EngineeringTools.Core/Dxf/PlanSheetNaming.cs:118–174`; `Kor.Operations.EngineeringTools.Core/Intake/SheetViews.cs:158–162`; `Kor.Operations.EngineeringTools.Core/Dxf/DrawingVocabulary.cs:129,149–155`.

**Smallest input:** `S1_1_MAIN FLOOR PLAN SHOWING LEVEL 2 FRAMING OVER.dxf`, using the default vocabulary.

**Deduction:** `own` is correctly cut to MAIN FLOOR PLAN, but the numeric readers do not use it. `SingleLevel` scans the title including SHOWING LEVEL 2, populates `Levels=[2]`, and prevents the word-floor fallback from reading MAIN=1. Replacing LEVEL 2 with 2ND FLOOR makes the same plan read as level 1. The two equivalent descriptions of the framing over the plan therefore change its assigned storey. A numbered plan can similarly acquire both its own and the over-level: LEVEL 1 PLAN SHOWING LEVEL 2 FRAMING OVER yields `[1,2]`.

The chain reader has the corresponding spelling gap: `FloorWordIn` accepts ordinal-plus-noun through `WordFloor`, but does not accept LEVEL 4. With GROUND SHOWING MAIN and MAIN SHOWING LEVEL 4, using FLOOR after each floor word, it ranks GROUND=1, MAIN=2 rather than anchoring them to 2 and 3. Thus both the level assignment and the chain's anchor depend on how the same numeric floor is written. The existing word tests use ordinal framing-over clauses, avoiding this path.

## 2. High — the ladder can inherit the previous job's floor ranks while the composer resets them

**Claim:** §69: “the ladder and the composer rank from the same view names”; the surrounding rule says “a word the chain never names keeps the row's level”.

**Source:** `Kor.Operations.EngineeringTools.Core/Intake/StoreysFromPlans.cs:61–73`; `Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs:735–745`; `Kor.Operations.EngineeringTools.Core/Dxf/DrawingVocabulary.cs:128–134,146–147`.

**Smallest input:** sequential jobs in one process, with default rule rows. Job A has GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER and MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER. Job B has only `S1_1_MAIN FLOOR PLAN.dxf`. Begin B's ladder after A's composer has set the static vocabulary to GROUND=1, MAIN=2, UPPER=3.

**Deduction:** B's `Merge` starts from the static `PlanSheetNaming.Vocabulary`. Its title has no framing-over clause, so `WithFloorWordsRankedBy` returns that existing vocabulary and B's ladder names L2. B's composer subsequently starts afresh with `ApplyRules(DrawingVocabulary.Default, banked)`; with default rows and no chain, the same filename parses as L1. Identical title lists do not ensure identical interpretation when the starting vocabulary differs. The “row” retained by the ladder can actually be the preceding job's derived rank.

This is a concrete state/call-sequence counterexample, not a claim about how often the corpus runner executes that sequence. The allowed composer block does not show how `files` was selected, and the allowed ladder source does not show its caller's selection of `planFileNames`; equality of their production lists is therefore not independently established. The tests exercise the helper and ladder, not this sequential composer/ladder state transition.

## 3. High — an unknown building's roof can be placed on another building

**Claim:** §70 item 3: “A building's roof plan names that building's roof”; “a tagged roof plan names `<TAG>-ROOF` after that building's highest level”.

**Source:** `Kor.Operations.EngineeringTools.Core/Intake/StoreysFromPlans.cs:76–82,132–142`; `Kor.Operations.EngineeringTools.Core/Dxf/PlanSheetNaming.cs:266–278,463–478,562–567`.

**Smallest input:** a ladder containing only `B-L40`; a B-tagged LEVEL 40 plan; and `S2_1_ROOF PLAN BLDG C.dxf`, with no C plan naming a level and no C storey in the ladder.

**Deduction:** `highestOfBuilding` has no C entry, so `Merge` skips C's roof at line 134. In `MatchStories`, C's building filter correctly rejects B-L40, but an empty eligible list is then replaced with **all** stories at line 271. There is no named roof, and the highest-plan helper finds no matching C plan. The final fallback returns the first eligible storey: B-L40. A lack of evidence for C's roof becomes permission to place it on B.

The ordinary success path is sound: `StoryBelongsToBuilding("C-ROOF","C")` returns true because it accepts the `C-` prefix. If C-ROOF exists, it survives filtering, is found as a named roof, and `OneRoofOf` returns it. The fault is the unknown-building fallback, not rejection of the new roof spelling.

## 4. Medium — separate buildings' word chains are silently fused into one

**Claim:** §69: “The set's own order of its floor words”; “the chain ranks them from 1 upward”. Its documented exclusions mention numbered buildings, not differently named floors in letter-tagged buildings.

**Source:** `Kor.Operations.EngineeringTools.Core/Dxf/DrawingVocabulary.cs:122–147`; `Kor.Operations.EngineeringTools.Core.Tests/Intake/AStoreyMayBeNamedByAWordTests.cs:85–118`.

**Smallest input:** `GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER BLDG A` and `MAIN FLOOR SHOWING UPPER FLOOR FRAMING OVER BLDG B`. These describe separate two-floor chains: A's GROUND→MAIN and B's MAIN→UPPER.

**Deduction:** the graph is keyed only by floor word. Building tags are never consulted. It becomes GROUND→MAIN→UPPER, and the returned vocabulary is GROUND=1, MAIN=2, UPPER=3 for both buildings. Building B's MAIN can no longer retain its own level 1. This is not the known inability to read BUILDING 1…11: the input uses supported letter tags.

The chain test's own WHAT IT DOES NOT explicitly excludes “two buildings with different chains in one set”. That is a truthful test limitation; the broader §69 account does not carry it. This finding identifies the unsupported universal interpretation, rather than presenting the already disclosed test limitation as a newly failed test.

## 5. Medium — a cycle with a tail is ranked despite contradictory floor order

**Claim:** §69: “two stories about one word leave the row alone”; the rule describes titles that “chain floor words”.

**Source:** `Kor.Operations.EngineeringTools.Core/Dxf/DrawingVocabulary.cs:129–145`.

**Smallest input:** three titles stating GROUND→MAIN, MAIN→UPPER and UPPER→MAIN, each written as `<word> FLOOR SHOWING <word> FLOOR FRAMING OVER`.

**Deduction:** each source word has only one outgoing value, so the duplicate-key conflict check never fires. GROUND is the single bottom. The walk collects GROUND, MAIN, UPPER, stops when MAIN repeats, and still commits ranks 1, 2, 3. The third clause requires MAIN above UPPER, contradicting the committed order. The stop condition detects a revisit but treats the partial walk as a valid chain.

A self-reference is discarded even earlier. MAIN FLOOR SHOWING MAIN FLOOR FRAMING OVER alone leaves the row unchanged, as desired; add that typo to GROUND→MAIN and MAIN→UPPER, however, and it is silently ignored while the other clauses change MAIN to 2 and UPPER to 3. Only conflicting outgoing values that survive line 130 trigger the stated fallback. Neither cycle nor self-reference validation is asserted by the two-outgoing-values fixture.

## 6. Medium — main and elevator roofs of one building collapse to the same new storey

**Claim:** §70 item 3: “a tagged roof plan names `<TAG>-ROOF` after that building's highest level”.

**Source:** `Kor.Operations.EngineeringTools.Core/Intake/StoreysFromPlans.cs:69–82,132–142`; `Kor.Operations.EngineeringTools.Core/Dxf/PlanSheetNaming.cs:185–186,273–278,472–478,489–495`.

**Smallest input:** no elevation chain, and three files: `LEVEL 1 PLAN BLDG C`, `ROOF PLAN BLDG C`, `ELEVATOR ROOF PLAN BLDG C`, each with an ordinary sheet prefix and `.dxf` extension.

**Deduction:** both roofs add the same tag C to the `SortedSet`. The ladder gets one C-ROOF. Both parsed roof plans are routed to the named-roof branch, where the only eligible roof is C-ROOF; `OneRoofOf` immediately returns it for either plan. The elevator distinction never reaches the numbered-storey rule, which otherwise asks for highest+2 rather than highest+1. A main roof and the roof above the lift overrun therefore receive the same storey despite the vocabulary and matching code explicitly distinguishing them.

This is not a second roof being assigned to another building: ownership is correct, but the two vertical locations have been reduced to one. The allowed ladder test file has no roof-specific test name exercising this case.

## 7. Medium — an underlined note can consume a valid title as its supposed first line

**Claim:** §69: “an underlined line that names no plan by itself takes the line directly above it … when the two together do, and that first line is then no title of its own”.

**Source:** `Kor.Operations.EngineeringTools.Core/Intake/SheetViews.cs:78–101,143–162`.

**Smallest input:** two text lines outside the title-block region, both height 8 pt and sharing span X=400…600: `LEVEL 3 PLAN` at minimum Y=112 and an unrelated last note `CONTINUOUS TO MAIN FLOOR SLAB` at Y=100. Put full-width underlines at Y=110 and Y=98. The note contains none of the explicitly rejected NOTES, LEGEND or SCHEDULE words and has no trailing colon.

**Deduction:** the lower line alone does not name a plan. The concatenation does, entirely because the upper line already says LEVEL 3 PLAN. It is recorded as one joined title, the real upper title is consumed, and the emitted view is `LEVEL 3 PLAN CONTINUOUS TO MAIN FLOOR SLAB`, anchored at Y=98 rather than Y=110. The lower line contributed no evidence that the two belong to one title. This is an ambiguity in the semantic rule: it implements the stated concatenation test, but that test is insufficient to establish a title continuation.

The changed title height can affect the split. With another view present, a member at the same X and page Y=105 was below the original level-3 title; it is above the newly extended title and can now be assigned to it by the drop-distance rule (`SheetViews.cs:184–192`). The current negative note fixture has no adjacent plan-naming line above it, so it cannot detect this false join.

## 8. Medium — a successfully joined sole view can still be exported under a name that says no storey

**Claim:** §66: “a title with no number at all still names the view, the stem standing where the number would”; §69: “A title may run two lines, and a floor named by a word with a framing-over clause names a plan.”

**Source:** `Kor.Operations.EngineeringTools.Core/Intake/SheetViews.cs:170–176,310–326`; `Kor.Operations.EngineeringTools.Core/Intake/SheetDxfName.cs:78–83`; `Kor.Operations.EngineeringTools.Core.Tests/Intake/ASheetIsItsViewsTests.cs:136–144,220–238`.

**Smallest input:** `Titles` has found one view, `GROUND FLOOR SHOWING MAIN FLOOR FRAMING OVER`. Pass that one view to `Split` with valid positive scale, no sheet number, an empty title block, fallback stem `job-p01`, and no supplied sheet DXF name.

**Deduction:** the fewer-than-two-views branch preserves the `View` object but ignores its Title when naming the file. It calls the three-argument `SheetDxfName.For`, which sees no number/title fields and returns `job-p01.dxf`. The composer cannot recover GROUND from that name. With two views, each title instead goes through `ForView` and is included in its filename.

`Parts` has the same distinction when its independently produced `sheetName` carries no level: it passes that name into the sole-view branch, rather than using the discovered view's title. This is a conditional input case, not a claim that every single-view page lacks a sheet title. The existing one-view fixture repeats LEVEL 3 PLAN in the title block, masking the dependency. The two-line fixture contains a second FOUNDATION view and asserts `Titles`, not the subsequent single-view export.

## 9. Medium — Parse still truncates extensionless titles containing dotted sheet numbers

**Claim:** §69: “`PlanSheetNaming.Parse` takes a vocabulary outright now, and `TitleOf` strips only a `.dxf` (a sheet number holds a dot).”

**Source:** `Kor.Operations.EngineeringTools.Core/Dxf/PlanSheetNaming.cs:93–104,122`; `Kor.Operations.EngineeringTools.Core.Tests/Intake/AStoreyMayBeNamedByAWordTests.cs:98–108`.

**Smallest input:** `PlanSheetNaming.Parse("S2.01_1_MAIN FLOOR PLAN", DrawingVocabulary.Default)`.

**Deduction:** `TitleOf` itself preserves this name correctly. But `Parse` first calls `Path.GetFileNameWithoutExtension`, interpreting `.01_1_MAIN FLOOR PLAN` as an extension and reducing the name to S2. Its later `TitleOf(name)` cannot recover the discarded title, and the parsed level list is empty. Adding `.dxf` makes the same view parse as level 1.

This qualifies the documented integration: only calling `TitleOf` directly achieves the advertised extension handling. The chain fixture uses extensionless dotted names for ranking but explicitly appends `.dxf` before `Parse` and `Merge`, so its assertions do not exercise this mismatch. Whether production supplies extensionless names to `Parse` is not established here.

## 10. Low — the two-line join accepts more than the documented two heights, and its test misses that boundary

**Claim:** §69: the first line is “within two heights, sharing its span”.

**Source:** `Kor.Operations.EngineeringTools.Core/Intake/SheetViews.cs:85–87`; `Kor.Operations.EngineeringTools.Core.Tests/Intake/ASheetIsItsViewsTests.cs:222–238`.

**Smallest input:** two aligned lines of height 8 pt: GROUND FLOOR SHOWING at minimum Y=117 and MAIN FLOOR FRAMING OVER at Y=100, with a valid underline at Y=98.

**Deduction:** the 17 pt separation exceeds two heights (16 pt), but satisfies the code's `2.2 * line.Height` window (17.6 pt), so the lines join. The window is also based solely on the lower line's height. The fixture uses 12 pt separation with 8 pt tokens, safely inside both limits, and therefore cannot distinguish the stated rule from the implemented one.

The same test does not assert the interposed-line rule or consumption of an independently underlined first line: its upper line has no underline, and the Y=98 stroke is 14 pt below that line, already outside its own underline window of 9.6 pt. Removing the interposed-line check or the `consumed` check would not be exposed by this particular layout.

**Other requested cases and limits.**

- **Single-word anchor:** a row MAIN=2 plus MAIN FLOOR SHOWING 2ND FLOOR FRAMING OVER returns MAIN=1: `DrawingVocabulary.cs:141–145` uses `2 − 1`, overriding the row. That agrees with the stated rule that the over-number anchors the chain; it is not a finding that the old row loses. The existing MAIN=1/over-2 fixture cannot demonstrate a changed rank caused by anchoring. Numeric anchors are not checked for a sensible floor range: MAIN→UPPER→1ST produces MAIN=−1, UPPER=0. This is a definite helper output; its downstream interpretation depends on storey-name normalisation outside this reading set, so no crash is claimed.
- **Basement endpoints:** a default basement word is absent from `WordFloor`, so a MAIN→BASEMENT clause supplies no `overWord` and is ignored. A BASEMENT→MAIN clause likewise supplies no recognised `ownWord`. This is the explicitly excluded basement-in-chain case, not a new finding.
- **Two-line examples:** LEVEL 3 above PLAN is joined when the lower PLAN line is underlined and lies in the span/window. **PLAN alone does not satisfy `NamesAPlan`**: it says PLAN but names no floor or other accepted kind. In contrast, BLDG C above LEVEL 3 PLAN is not joined because the lower line already names a plan; that upper building tag is lost unless another source supplies it. This is a real limitation, but follows the explicitly documented restriction to lower lines that do not name a plan alone.
- **Incomplete first line:** MAIN FLOOR SHOWING alone passes `NamesAPlan`: the mere framing-over marker shortens `OwnStoreyPart`, and MAIN FLOOR then satisfies `WordFloor` (`SheetViews.cs:147–162`). There is no requirement for a complete phrase after SHOWING. `consumed` suppresses it only when a qualifying lower line actually joins it; an independently underlined first line survives when the lower line is missing, too distant, inadequately overlapping, or independently plan-naming. No all-layout suppression is established by the fixture.
- **Three-line titles:** there is no recursive merge of joined text. Candidate joins use `above.Text`, not its entry in `joined`, so three-line reconstruction is not supported. §69 explicitly excludes it. Two complete vertically stacked titles are not merged merely for being close, since both skip the join pass; unrelated lower text that is not plan-naming remains vulnerable to finding 7.
- **Vertical stacking coverage:** the class remarks still exclude it, but `PlansStackedOneAboveTheOtherSplitByTheDropToTheirTitles` (`ASheetIsItsViewsTests.cs:167–187`) now checks three vertically stacked plans and 3/2/1 column ownership. The exclusion is stale. That fixture uses widely separated, single-line titles; it does not establish interaction between vertical stacking and the new close-line join.
- **Number versus word:** under defaults, both MAIN LEVEL 2 PLAN and LEVEL 2 MAIN FLOOR PLAN parse as level 2, because the numeric pass prevents the word fallback. UPPER PARKADE PLAN names no level: PARKADE is not a configured floor noun. These implement §66's stated restrictions.
- **Office convention:** GROUND FLOOR maps to the configured value, default 1, regardless of geographical convention or the presence of a basement. No country inference occurs. A different office meaning is explicitly a rule-row matter in §66; no external claim about British practice was needed or investigated.
- **Ordinal bounds:** `DrawingVocabulary.cs:92–108,199–201` recognises written English ordinals FIRST through TWELFTH. THIRTEENTH FLOOR PLAN supplies no level under defaults, while 13TH FLOOR PLAN supplies 13; numeric suffix ordinals are limited to one or two digits, so 100TH is also outside that pattern. `LevelOfWord` first consults configured floor-word entries, which can extend the named words. The broad §66 wording “an ordinal names its level … English, compiled” should be read with these concrete bounds.

**Checks versus their coverage statements (E).** No tests were run. For `ASetsStoreysAreWhatItsPlansNameTests`, the brief permits only the class summary and names, plus bodies whose names claim a roof or a word. None of its five current test names does, so its other bodies were not inspected and their assertions are not inferred from their names.

| Test file | Assertion coverage and limits |
|---|---|
| `AStoreyMayBeNamedByAWordTests.cs` | The class summary promises MAIN, GROUND, UPPER, 2ND, SECOND, BASEMENT and LOWER. The parser theory includes MAIN, UPPER, 2ND, THIRD and 7TH; separate tests assert BASEMENT and LOFT. GROUND is exercised by the chain checks, but LOWER and SECOND are not asserted in this file. Framing-over separation is checked only with word/ordinal over-floors, leaving finding 1. The ladder fixtures assert storey-name sequences, not composer placement or heights. The filename fixtures cover the specified hyphenated and absent-number cases. The chain fixture checks ranks and one explicitly supplied-vocabulary parse, then the ladder; it does not run the composer or prove its starting state/input list agrees. Its sole numeric anchor leaves the existing default unchanged, so an implementation ignoring that anchor can pass it. The conflicting-outgoing-edge fixture supports only that particular conflict form, not finding 5's cyclic constraints. |
| `ASheetIsItsViewsTests.cs` | Tests title text and selected extents; split counts and selected indices; axis names; the anchor flag; filenames; heading exclusions; building propagation; stroke requirement; and the new two-line title. The ownership fixture generally checks counts, not member coordinates/identity, so exchanging equal numbers of members between views can evade those assertions. “Indices survive” checks mapped values and slab mapping count but does not assert every face-line key or geometric association. The two-line case never asserts emitted parts/filenames, first-line-underlined consumption, the interposed-line rule, or the window boundary. The class's exclusions of vertical stacking and two baselines are stale now that individual fixtures cover selected examples of each. |
| `ASetsStoreysAreWhatItsPlansNameTests.cs` | Its summary claims parkade/numbered/roof ordering, assumption reporting, preservation of stated heights, merging stated and plan-only storeys, and a foundation-only case; word storeys are explicitly excluded. Its five names cover no-elevation plans, stated/intermediate elevations, an already-covered ladder, foundation-only input, and plans below the first stated level. There is no test named for building roofs, two roofs, or word chains. Within the authorised reading, this file supplies no inspected assertion proving the new building-roof behaviour; the remaining summary-to-body comparison is intentionally unverified. |

Ten findings are reported, below the cap of twelve. Only this response document was created; no implementation changes were made.
