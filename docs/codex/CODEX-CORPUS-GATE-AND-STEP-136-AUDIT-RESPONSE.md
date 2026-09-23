# CODEX RESPONSE — audit of the corpus gate and intake step 136 (2026-09-22, codex-cli 0.156.0)

Run non-interactively with the brief and the code on stdin (its sandbox could not spawn processes to open files).
Brief: CODEX-CORPUS-GATE-AND-STEP-136-AUDIT.md


The gate can approve a comparison with missing results, and a failed latest run can be hidden by an older successful row. The eight appended columns line up; the missing yardstick methods and classifier continuation limit conclusions about storey matching and final floor acceptance.

1. **False greens — high consequence.** `CorpusGate.cs:51–56, 90–101, 133–135`: missing measurements do not block banking, and filtering precedes selection of the latest run.

   Below, `O/H` means `PlatesOursSqFt/PlatesHersSqFt`; thickness agreement is unchanged unless stated. Totals are `(before area, after area, hers)`.

   | Concrete input | `Judged` / `Losses` | Totals and consequence |
   |---|---:|---|
   | Before: job A `10000/10000`; after: A absent | 0 / 0 | `(0,0,0)`. A remains in `Verdicts` with a missing side, but the summary approves banking. Missing evidence can conceal a real loss. |
   | Before: A `10000/10000`; after: A has both plate fields null following a yardstick failure | 0 / 0 | Same result. `CorpusAnalyzer.cs:390–392` records the failure in `YardstickNote`; the gate never examines that note. |
   | Before: A `10000/10000`; after ledger contains an older A `10000/10000` and a newer failed A with null figures | 1 / 0 | `(10000,10000,10000)`. The newer row is filtered out before sorting, so the failure disappears entirely. Two valid rows correctly select the newest; equal timestamps select the first encountered row. |
   | Before: A `10000/null`; after: A `9500/null` | 1 / 1 | `(10000,9500,0)`. Missing hers does **not** disable comparison: it uses a 200-square-foot tolerance. If either side supplies hers, that value is used, preferring after. |
   | Before: A `10000/10000`; after: A `5000/NaN` | 1 / 0 | `(10000,5000,NaN)`. The tolerance becomes NaN, making the loss comparison false despite losing half the area. |
   | Before: A `10000/0`; after: A `9500/0` | 1 / 1 | `(10000,9500,0)`. Zero uses the 200-square-foot tolerance; `ShareNow` is null and total percentages print as zero. |
   | Before: `JobA` `10000/10000`; after: `joba` `9500/10000` | 1 / 1 | `(10000,9500,10000)`. Case differences are handled correctly. |

   Further silent regressions:

   - **Opening loss never affects the verdict.** `CorpusGate.cs:54–56, 86, 94`: keep area and thickness unchanged, change `OpeningsHersWeHave` from 20 to 0 with `OpeningsHers=30`; `Judged=1`, `Losses=0`, and only the opening totals reveal the regression. Openings are reported, not gated.
   - **Missing thickness becomes zero in totals without becoming a loss.** `CorpusGate.cs:54, 93`: unchanged valid plate figures, `ThicknessAgree=8 → null`, produces `Judged=1`, `Losses=0`, and thickness totals `8 → 0`.
   - **Changing hers can enlarge the tolerance and conceal a loss.** `CorpusGate.cs:51, 84`: `O/H=10000/10000 → 9000/1000000` produces a 5,000-square-foot tolerance and no loss despite losing 1,000 square feet.

   A genuine 41-field legacy row is correctly excluded from judgment. The problem is that failed modern measurements can receive the same treatment.

   **Exit status is unverified:** `CorpusGateVerb.cs` was not supplied. The needed line is the return/exit expression consuming `report.Losses` and any incomplete-comparison checks.

2. **False losses — comparison scope is not checked.** `CorpusAnalyzer.cs:382, 385–386` and `CorpusGate.cs:84–85`: the gate compares aggregate areas and thickness counts without establishing that both runs judged the same storeys.

   Concrete conditional counterexample: before, ten shared storeys contribute `O/H=10000/10000`, `ThicknessAgree=10`; after, one storey is renamed only in the yardstick and ceases to match, yielding `9000/9000`, agreement 9. The gate reports both losses even though the produced model is unchanged and agrees everywhere still compared.

   The gate’s behavior on those rows is certain; whether that rename actually removes a storey cannot be established here. The missing lines are the storey-matching and inclusion predicates in `ModelYardstick.PlatesOnSharedStoreys` and `ThicknessOnSharedStoreys`, plus `PlateJudged`. A **fixed**, partially modelled scope is not itself evidence of a defect.

   **Rounding within the gate is sound:** `CorpusGate.cs:51` uses the unformatted values and a strict comparison, so exactly the tolerance is not a loss; display rounding happens afterward. Ledger precision remains unverified because the `Q` writer helper and `ParseSetRow`’s `D` helper definitions were omitted.

3. **Column positions are sound.** `CorpusAnalyzer.cs:506, 549–555, 593–596`: the header has 49 fields, the writer supplies 49 arguments, and the eight appended fields align exactly.

   | Zero-based index | Header | Writer / `SetRow` property | Parser |
   |---:|---|---|---|
   | 41 | `plates_ours_sqft` | `PlatesOursSqFt` | `D(f[41])` |
   | 42 | `plates_hers_sqft` | `PlatesHersSqFt` | `D(f[42])` |
   | 43 | `plates_under_half` | `PlatesUnderHalf` | `I(f[43])` |
   | 44 | `plates_beyond_sqft` | `PlatesBeyondSqFt` | `D(f[44])` |
   | 45 | `thickness_storeys` | `ThicknessStoreys` | `I(f[45])` |
   | 46 | `thickness_agree` | `ThicknessAgree` | `I(f[46])` |
   | 47 | `openings_hers` | `OpeningsHers` | `I(f[47])` |
   | 48 | `openings_hers_we_have` | `OpeningsHersWeHave` | `I(f[48])` |

   Index 40 is `yardstick_age_days` in all three. With 41 fields, every appended argument becomes null without accessing those indices. The parser’s indices 0–9 were not supplied; their assignments cannot be checked.

4. **Step 136 leaves a concrete false-candidate path; final acceptance is unverified.** `StructuralPlanClassifier.cs:1034–1050, 1074–1076, 1101–1114`: a short closure proves little about whether the enclosed region contains slab material.

   Consider a **U-shaped courtyard void**, whose boundary is represented by this open chain in feet; multiply coordinates by 12 for inches:

   ```text
   (0,0), (100,0), (100,100), (90,100), (90,10),
   (10,10), (10,100), (0,100), (0,1)
   ```

   Its enclosed area is 2,800 square feet, bounding-box fill is 28%, drawn length is 679 feet, and closure is one foot.

   - The removed 55% test refused it.
   - The invention gate passes it: `1/679 ≈ 0.147%`, without a match-line exception.
   - The size gate passes whenever `MinPlateArea ≤ 2800 sq ft`, including a 400-square-foot setting.
   - As an unbranched chain, it has exactly two loose ends; counting ends alone cannot distinguish it from a slab boundary.
   - A recognized slab-thickness annotation anchored at `(5,50)` lies inside the ring even if its leader identifies adjacent slab material. The shown `says` predicate would find it.

   This demonstrates a void outline surviving every **shown** rejection, provided it reaches this candidate block. To establish that it actually becomes a floor, the missing lines are the upstream chain eligibility/two-loose-ends predicate and the continuation consuming `says` and adding the ring. `SlabChainJoinTests.cs` was also omitted. The supplied code does not justify claiming unconditional acceptance—or that another guard catches it.

5. **Several comments overstate or misdescribe the implementation.**

   - **A wrong closure is not necessarily refused.** `StructuralPlanClassifier.cs:1042–1043` promises that a hooked ring closed across the wrong gap remains refused by the invention gate; the U-shaped void above contradicts that local guarantee.
   - **The percentage is of drawn length, not total perimeter.** `StructuralPlanClassifier.cs:1074–1076, 1101–1106` compares `joinAcross/drawnAlong`. At a 10% setting, the actual permitted undrawn share of the completed perimeter is about **9.09%**. For example, the open rectangle `(0,0),(44.75,0),(44.75,10.5),(0,10.5)` feet has 100 feet drawn and a 10.5-foot closure: it is refused although only 9.50% of its perimeter is undrawn.
   - **“Largest gains” are not first.** `CorpusGate.cs:59, 88–89`: ascending `MovedSqFt` puts a gain of 100 before a gain of 1,000.
   - **`JobsWithAYardstick` does not select the newest row.** `CorpusGate.cs:103–109`: an older row with `Yardstick="hers.e2k"` followed by a newer row with `Yardstick=null` still includes that job.
   - **The tests do not test legacy CSV parsing.** `TheGateJudgesEverySetTheEngineerModelledTests.cs:17, 82–85` claims a 41-column ledger reads as nulls, but constructs an already-null `SetRow`; no CSV parser is invoked. The shown parser supports the claim, but this test would not catch a parsing regression.
tokens used
25,443
1. **False greens — high consequence.** `CorpusGate.cs:51–56, 90–101, 133–135`: missing measurements do not block banking, and filtering precedes selection of the latest run.

   Below, `O/H` means `PlatesOursSqFt/PlatesHersSqFt`; thickness agreement is unchanged unless stated. Totals are `(before area, after area, hers)`.

   | Concrete input | `Judged` / `Losses` | Totals and consequence |
   |---|---:|---|
   | Before: job A `10000/10000`; after: A absent | 0 / 0 | `(0,0,0)`. A remains in `Verdicts` with a missing side, but the summary approves banking. Missing evidence can conceal a real loss. |
   | Before: A `10000/10000`; after: A has both plate fields null following a yardstick failure | 0 / 0 | Same result. `CorpusAnalyzer.cs:390–392` records the failure in `YardstickNote`; the gate never examines that note. |
   | Before: A `10000/10000`; after ledger contains an older A `10000/10000` and a newer failed A with null figures | 1 / 0 | `(10000,10000,10000)`. The newer row is filtered out before sorting, so the failure disappears entirely. Two valid rows correctly select the newest; equal timestamps select the first encountered row. |
   | Before: A `10000/null`; after: A `9500/null` | 1 / 1 | `(10000,9500,0)`. Missing hers does **not** disable comparison: it uses a 200-square-foot tolerance. If either side supplies hers, that value is used, preferring after. |
   | Before: A `10000/10000`; after: A `5000/NaN` | 1 / 0 | `(10000,5000,NaN)`. The tolerance becomes NaN, making the loss comparison false despite losing half the area. |
   | Before: A `10000/0`; after: A `9500/0` | 1 / 1 | `(10000,9500,0)`. Zero uses the 200-square-foot tolerance; `ShareNow` is null and total percentages print as zero. |
   | Before: `JobA` `10000/10000`; after: `joba` `9500/10000` | 1 / 1 | `(10000,9500,10000)`. Case differences are handled correctly. |

   Further silent regressions:

   - **Opening loss never affects the verdict.** `CorpusGate.cs:54–56, 86, 94`: keep area and thickness unchanged, change `OpeningsHersWeHave` from 20 to 0 with `OpeningsHers=30`; `Judged=1`, `Losses=0`, and only the opening totals reveal the regression. Openings are reported, not gated.
   - **Missing thickness becomes zero in totals without becoming a loss.** `CorpusGate.cs:54, 93`: unchanged valid plate figures, `ThicknessAgree=8 → null`, produces `Judged=1`, `Losses=0`, and thickness totals `8 → 0`.
   - **Changing hers can enlarge the tolerance and conceal a loss.** `CorpusGate.cs:51, 84`: `O/H=10000/10000 → 9000/1000000` produces a 5,000-square-foot tolerance and no loss despite losing 1,000 square feet.

   A genuine 41-field legacy row is correctly excluded from judgment. The problem is that failed modern measurements can receive the same treatment.

   **Exit status is unverified:** `CorpusGateVerb.cs` was not supplied. The needed line is the return/exit expression consuming `report.Losses` and any incomplete-comparison checks.

2. **False losses — comparison scope is not checked.** `CorpusAnalyzer.cs:382, 385–386` and `CorpusGate.cs:84–85`: the gate compares aggregate areas and thickness counts without establishing that both runs judged the same storeys.

   Concrete conditional counterexample: before, ten shared storeys contribute `O/H=10000/10000`, `ThicknessAgree=10`; after, one storey is renamed only in the yardstick and ceases to match, yielding `9000/9000`, agreement 9. The gate reports both losses even though the produced model is unchanged and agrees everywhere still compared.

   The gate’s behavior on those rows is certain; whether that rename actually removes a storey cannot be established here. The missing lines are the storey-matching and inclusion predicates in `ModelYardstick.PlatesOnSharedStoreys` and `ThicknessOnSharedStoreys`, plus `PlateJudged`. A **fixed**, partially modelled scope is not itself evidence of a defect.

   **Rounding within the gate is sound:** `CorpusGate.cs:51` uses the unformatted values and a strict comparison, so exactly the tolerance is not a loss; display rounding happens afterward. Ledger precision remains unverified because the `Q` writer helper and `ParseSetRow`’s `D` helper definitions were omitted.

3. **Column positions are sound.** `CorpusAnalyzer.cs:506, 549–555, 593–596`: the header has 49 fields, the writer supplies 49 arguments, and the eight appended fields align exactly.

   | Zero-based index | Header | Writer / `SetRow` property | Parser |
   |---:|---|---|---|
   | 41 | `plates_ours_sqft` | `PlatesOursSqFt` | `D(f[41])` |
   | 42 | `plates_hers_sqft` | `PlatesHersSqFt` | `D(f[42])` |
   | 43 | `plates_under_half` | `PlatesUnderHalf` | `I(f[43])` |
   | 44 | `plates_beyond_sqft` | `PlatesBeyondSqFt` | `D(f[44])` |
   | 45 | `thickness_storeys` | `ThicknessStoreys` | `I(f[45])` |
   | 46 | `thickness_agree` | `ThicknessAgree` | `I(f[46])` |
   | 47 | `openings_hers` | `OpeningsHers` | `I(f[47])` |
   | 48 | `openings_hers_we_have` | `OpeningsHersWeHave` | `I(f[48])` |

   Index 40 is `yardstick_age_days` in all three. With 41 fields, every appended argument becomes null without accessing those indices. The parser’s indices 0–9 were not supplied; their assignments cannot be checked.

4. **Step 136 leaves a concrete false-candidate path; final acceptance is unverified.** `StructuralPlanClassifier.cs:1034–1050, 1074–1076, 1101–1114`: a short closure proves little about whether the enclosed region contains slab material.

   Consider a **U-shaped courtyard void**, whose boundary is represented by this open chain in feet; multiply coordinates by 12 for inches:

   ```text
   (0,0), (100,0), (100,100), (90,100), (90,10),
   (10,10), (10,100), (0,100), (0,1)
   ```

   Its enclosed area is 2,800 square feet, bounding-box fill is 28%, drawn length is 679 feet, and closure is one foot.

   - The removed 55% test refused it.
   - The invention gate passes it: `1/679 ≈ 0.147%`, without a match-line exception.
   - The size gate passes whenever `MinPlateArea ≤ 2800 sq ft`, including a 400-square-foot setting.
   - As an unbranched chain, it has exactly two loose ends; counting ends alone cannot distinguish it from a slab boundary.
   - A recognized slab-thickness annotation anchored at `(5,50)` lies inside the ring even if its leader identifies adjacent slab material. The shown `says` predicate would find it.

   This demonstrates a void outline surviving every **shown** rejection, provided it reaches this candidate block. To establish that it actually becomes a floor, the missing lines are the upstream chain eligibility/two-loose-ends predicate and the continuation consuming `says` and adding the ring. `SlabChainJoinTests.cs` was also omitted. The supplied code does not justify claiming unconditional acceptance—or that another guard catches it.

5. **Several comments overstate or misdescribe the implementation.**

   - **A wrong closure is not necessarily refused.** `StructuralPlanClassifier.cs:1042–1043` promises that a hooked ring closed across the wrong gap remains refused by the invention gate; the U-shaped void above contradicts that local guarantee.
   - **The percentage is of drawn length, not total perimeter.** `StructuralPlanClassifier.cs:1074–1076, 1101–1106` compares `joinAcross/drawnAlong`. At a 10% setting, the actual permitted undrawn share of the completed perimeter is about **9.09%**. For example, the open rectangle `(0,0),(44.75,0),(44.75,10.5),(0,10.5)` feet has 100 feet drawn and a 10.5-foot closure: it is refused although only 9.50% of its perimeter is undrawn.
   - **“Largest gains” are not first.** `CorpusGate.cs:59, 88–89`: ascending `MovedSqFt` puts a gain of 100 before a gain of 1,000.
   - **`JobsWithAYardstick` does not select the newest row.** `CorpusGate.cs:103–109`: an older row with `Yardstick="hers.e2k"` followed by a newer row with `Yardstick=null` still includes that job.
   - **The tests do not test legacy CSV parsing.** `TheGateJudgesEverySetTheEngineerModelledTests.cs:17, 82–85` claims a 41-column ledger reads as nulls, but constructs an already-null `SetRow`; no CSV parser is invoked. The shown parser supports the claim, but this test would not catch a parsing regression.
