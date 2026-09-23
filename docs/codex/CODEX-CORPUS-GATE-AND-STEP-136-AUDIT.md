# CODEX — audit the corpus gate and intake step 136

**Read-only.** Do not edit any file, do not run the test suite, do not build. Report findings only.

## Why you are being asked

Tonight two things landed in the PDF→ETABS intake. One is an **instrument** that will judge every future rule;
the other is a **rule** that changes what counts as a floor across 55 sets. An instrument that lies is worse than
no instrument, and this one is about to be trusted. Audit them adversarially.

## Open ONLY these files

- `Kor.Operations.EngineeringTools.Core/Intake/CorpusGate.cs`
- `Kor.Operations.EngineeringTools.Core.Tests/Intake/TheGateJudgesEverySetTheEngineerModelledTests.cs`
- `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/CorpusGateVerb.cs`
- `Kor.Operations.EngineeringTools.Core/Intake/CorpusAnalyzer.cs` — only `SetRow`, `SetHeader`, `SetLine`,
  `ParseSetRow`, and the block that fills the yardstick figures (search `PlatesOursSqFt`)
- `Kor.Operations.EngineeringTools.Core/Dxf/ModelYardstick.cs` — only `PlatesOnSharedStoreys`,
  `ThicknessOnSharedStoreys`, `PlatesByStorey`, `PlatePointsByStorey`, `PlateJudged`, `PlatesBeyondSqFt`
- `Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs` — only the slab-chain block around the
  comment "A RING'S SHAPE DOES NOT JUDGE IT" and the invention gate that follows it
- `Kor.Operations.EngineeringTools.Core.Tests/SlabChainJoinTests.cs`

## What the code claims

1. **The gate** (`CorpusGate`): given two corpus ledgers, it judges every set the engineer has an ETABS model of,
   on her plate area, her slab thicknesses and her openings; a set LOSES when its plate area falls by more than
   the larger of 200 sq ft and 0.5% of hers, or when the count of thicknesses we agree with falls. Exit 1 on any
   loss. It is meant to be run between a rule and its bank.
2. **The ledger columns**: `plates_ours_sqft, plates_hers_sqft, plates_under_half, plates_beyond_sqft,
   thickness_storeys, thickness_agree, openings_hers, openings_hers_we_have` are appended to the set row. A
   ledger banked before they existed has 41 fields and must parse with those as null, and must then be judged on
   nothing rather than as a loss.
3. **Step 136**: a candidate floor was refused when it filled under 55% of its own bounding box ("a thin or
   hooked shape"). That refusal is removed. The gate that follows — the share of the ring's perimeter that nobody
   drew, 10%, with a match-line exception — is kept and is claimed to be the real test.

## The questions, in order of how much they would cost if the answer is bad

1. **Can the gate report a green when a set truly lost?** Consider: a set missing from one ledger; two runs in one
   ledger for the same job; `plates_hers_sqft` null but `plates_ours_sqft` present; a set whose yardstick failed
   to load that run; NaN or zero `hers`; a job name differing in case. What does `Judged`, `Losses` and the
   totals do in each case, and is silence ever possible where a loss happened?
2. **Can it report a loss that is not one?** Rounding, sets she models only partly, a storey renamed between runs.
3. **Are the ledger columns written and read at the same positions?** Count the header names against the writer's
   argument order against `ParseSetRow`'s indices, field by field, and say whether index 41..48 line up. A column
   silently one place out would be a number that judges everything and means something else.
4. **Step 136**: with the box-fill refusal gone, name a ring shape that now becomes a "floor" and should not be —
   be concrete about the geometry (a balcony band? a stair shaft? two rooms joined by a corridor?) — and say
   whether the invention gate, the minimum plate area, the two-loose-ends rule or the thickness call-out would
   still catch it. If nothing would, that is the finding.
5. **Is the comment true?** Both files carry long comments asserting what the code does and why. Name any comment
   that no longer matches the code.

## How to answer

A numbered list. For each finding: the file and line, one sentence of what is wrong, and the concrete input that
would show it (a ledger row, a ring's coordinates). Rank by consequence. If a question's answer is "sound", say
so in one line and move on — do not pad. If you find nothing in a section, say that plainly; a clean audit is a
useful result, an invented one is not.
