# Calgary land-use vocabulary — result, 2026-10-08

Implemented the vocabulary addition in `StructuralRelevanceGate.BuildingSignals`.
The existing case-insensitive word-boundary keep-signal regexes apply. No source
configuration, provider composition, Edmonton logic, or deployment changed.

## Terms shipped

The reproducible candidate file is
[`candidate-calgary-vocabulary.txt`](../../tools/RelevanceGateDiff/candidate-calgary-vocabulary.txt).
District spellings were checked against the
[City of Calgary district list](https://www.calgary.ca/planning/land-use/districts.html).

| Group | Exact terms (matched case-insensitively) |
|---|---|
| Multi-residential | M-CG, M-C1, M-C2, M-G, M-1, M-2, M-H1, M-H2, M-H3, M-X1, M-X2, H-GO |
| Mixed use | MU-1, MU-2 |
| Industrial | I-G, I-B, I-E, I-C, I-R, I-O, I-H |
| Commercial | C-N1, C-N2, C-C1, C-C2, C-COR1, C-COR2, C-COR3, C-O, C-R1, C-R2, C-R3 |
| Centre city | CC-MH, CC-MHX, CC-X, CC-COR, CC-ET, CC-EIR, CC-EMU, CC-EPR, CC-ERR, CC-ER, CR20-C20/R20 |
| Special purpose | S-UN, S-SPR, S-CS, S-R, S-CI, S-CRI, S-URP, S-FUD, S-TUC |
| Contextual direct control | accommodate DC, direct control |
| Deliberate infill exception | R-CG |

**Rejected candidate: bare DC.** The initial differential produced nine false
admissions: eight timber-sale titles such as `TA1882 - TPL - DC - Redwillow`, and
`PA(23)DCS-001 Site Operator for Harmon and Kane Lake Rec Site`. The latter also
demonstrates the existing morphological-suffix matching (`DC` -> `DCS`). Replacing
bare DC with the two planning phrases removed all nine false gains.

**R-CG retained as the brief's infill exception.** It contributes 52 newly kept
R-CG-only rows across the 600-row sample, including 39 in the first 400. The other
40 R-only rows have zero gains. R-C1, R-C1s, R-C2, R-1s, R-1N, R-G, R-Gm and R-2M
are not added. A district alone cannot establish an individual application's
built form; the harness does not claim these are 52 confirmed rowhouse projects.

## Differential evidence

Read the existing harness and searched `tools/`, `docs/`, and the neighboring
`KOR.Drafter/db/tools` before extending `RelevanceGateDiff`. Added `--calgary` to
score a local feed sample and classify gains, `--snapshot` to save both verdicts,
and `--verify-snapshot` to verify the promoted gate offline against those exact
proposed verdicts. All database access was read-only; credentials stayed in the
process environment and were removed afterwards.

Final pre-promotion run, using the actual compiled gate with and without the delta:

| Arm | Rows | Before kept | After kept | Gains | Keep -> reject |
|---|---:|---:|---:|---:|---:|
| Existing observations, title + description | 13,069 | 12,756 | 12,756 | 0 | **0** |
| Existing reject corpus, title only | 18,571 | — | — | 0 | **0** |
| Calgary first 400 | 400 | 6 (1.5%) | 238 (59.5%) | **232** | **0** |
| Calgary remaining 200 | 200 | — | — | 116 | **0** |

The 313 stored observations already refused by the pre-change gate are existing
keep-drift, not regressions introduced here. The initial candidate run saw 13,068
observations; the final run saw one more as ingestion continued. Final verification
uses the saved 13,069-row snapshot, not another live query.

**The brief's 36/400 baseline was not reproduced.** This run scores the published
`address` as title and `description` as description, without applicant or district
composition. Both arms use the same first 400 records from the same local response.
The compiled pre-change gate kept six. Do not report 36 as this run's baseline.
There is no wired Calgary source whose mapping could establish a different input
contract. As a diagnostic, adding the applicant prefix gave 15 baseline keeps;
that is not the input used for the reported differential.

Across the full 600-row sample, **343/343 rows with an eligible proposed district
code visible in the description are kept**. There are no newly kept rows among
the 40 R-only rows outside the R-CG exception.

After promotion, `--verify-snapshot` reported **0 mismatches / 32,240 rows**,
comparing the complete decision (Keep and RejectReason), not just counts.

## Ceiling and sample identity

Feed: `https://data.calgary.ca/resource/33vi-ew4s.json`.

- Keep-rate sample query: `?$limit=600&$order=applieddate%20DESC`; first 400 used
  for the headline rate. Latest applied date: `2026-10-07T17:45:14`.
- That sample has 555 coded rows: 367 contain at least one proposed code in the
  description; **188/555 (33.9%)** do not. Another 45 rows have no proposed code.
- Separate 600-coded-row query:
  `?$limit=600&$order=applieddate%20DESC&$where=proposedlud%20is%20not%20null`.
  **398/600 (66.3%)** contain a proposed code; **202/600 (33.7%)** do not.
  This is the newly measured counterpart to the brief's 388/600, or 35% absent.

Vocabulary cannot use a code absent from the text the gate sees. Some such rows
can already pass on prose; this is a code-visibility limit, not an assertion that
every absent-code row must be rejected. Composing `proposedlud` remains separate work.

Local evidence directory: `%TEMP%/kor-calgary-land-use-gate` (not committed):

| File | SHA-256 |
|---|---|
| calgary-600.json | A8EA540C3A02C65C2FB6F53427E4171D23962821220675BEE2379727BB14D03A |
| calgary-coded-600.json | D30E1CEB1E98A2CD027C8153438F96921EA2F2CE3529A4AF9AAAFDB374892459 |
| proposed-snapshot.json | 96FE22C09DB40B23BA185A65F24A8D4ACA6F738F260AA8EE7DFB9842BF98AF1C |

The directory also holds `candidate-diff.txt`, `contextual-diff.txt`, and
`tests-before.txt`. Existing reject rows have no stored descriptions, so that arm
cannot detect collisions occurring only in reject descriptions. Neither arm proves
lead quality or coverage of future sources; a code in an unseen commodity title
could still be a false positive. No upstream mapping or persistence behavior is
tested by this vocabulary harness.

## Tests

Before the gate edit, the focused test run had **15 failures / 24 passes**: all
14 planning-code cases and the case-insensitive title case failed as intended.
After the edit, the Data test project passed **231/231** in Debug.

New test names:

- `CalgaryDistrictInPlanningDescription_IsKept`
- `CalgaryLowDensityDistrict_AloneIsNotAKeepSignal`
- `CalgaryCodeLookalikes_AndBareDc_DoNotPass`
- `CalgaryDistrict_DoesNotOverrideAlwaysIrrelevantPermit`
- `CalgaryDistrict_IsCaseInsensitiveAndWordBoundedInTitle`
- `CalgaryDistrictInBuyerOnly_DoesNotPass`

Commands run successfully after promotion:

```powershell
dotnet run --project tools/RelevanceGateDiff/RelevanceGateDiff.csproj -c Debug -- --verify-snapshot "$env:TEMP/kor-calgary-land-use-gate/proposed-snapshot.json"
dotnet test Kor.Opportunities.Data.Tests/Kor.Opportunities.Data.Tests.csproj -c Debug -nologo
```
