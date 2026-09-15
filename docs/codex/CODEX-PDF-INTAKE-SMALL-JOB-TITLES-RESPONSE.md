# Small-job title blocks — response

## Rules

**Title field:** SHEET TITLE (also labelled TITLE) reads beside its label or down its column to the next label, whatever that label is, joining value lines from larger to smaller PDF y.

**Sheet number:** a leading S, optional separator, and digits with internal dots/dashes is a sheet number even when the next title word is glued to it; remove that prefix and any exported view index before reading the title's storey.

## Source findings

Read the requested sources in the prescribed order. The checkout is `develop` at `470d85ef`, a descendant of `98eb9107`.

The current `TitleBlockFields.Read` already sorts by descending y and ends a field at the next lower label in its column, without requiring that label to be SHEET NUMBER. Consequently, the supplied words for 01783, 01746, 30996 and 31057 do **not**, by source inspection, explain their reported blank titles. Their original token bounds and complete neighbouring words were not supplied. I have not claimed to reproduce or resolve those corpus blanks, reversed the y direction, or invented a template-specific cause.

Two concrete gaps are visible in the permitted source:

- `TitleBlockFields.Labels` omitted standalone `TITLE`, `CHECKED` and `DRAWN`. TITLE now names the canonical SHEET TITLE field; CHECKED and DRAWN now delimit fields. Longer labels still match first.
- `PlanSheetNaming.StripSheetNumber` searched for LEVEL or ROOF instead of recognizing a sheet prefix. `TitleOf` only removed an exported `_view_` prefix. A raw fused title therefore retained sheet digits and hid the initial floor word from word-boundary matching. The new prefix expression removes those digits before numeric and word-based storey reading, and preserves the full title when stripping a recognized prefix.

## Files and members changed

- `Kor.Operations.EngineeringTools.Core/Intake/TitleBlockFields.cs`: class summary, `Labels`, label canonicalization in `Read`.
- `Kor.Operations.EngineeringTools.Core/Dxf/PlanSheetNaming.cs`: new `SheetNumberPrefix` regex; `TitleOf`, `Parse(string, DrawingVocabulary)`, `StripSheetNumber`.
- `Kor.Operations.EngineeringTools.Core.Tests/Intake/TheAuditsCounterexamplesTests.cs`: one new theory, `ATitleFieldReadsDownItsColumnRegardlessOfTheSurroundingLabelOrder`, with eight rows covering the four supplied column orders, standalone TITLE, the conventional column order, and CHECKED/DRAWN boundaries. It asserts title order, sheet-number separation, beside-label values and consumed title tokens.
- `Kor.Operations.EngineeringTools.Core.Tests/Intake/AStoreyMayBeNamedByAWordTests.cs`: one new theory, `ASheetNumberGluedToATitleIsRemovedBeforeReadingItsStorey`, with seven rows covering all four 01375 titles and unseparated, dotted and dashed sheet numbers. It checks raw/exported titles, the exported number/title split, storeys, foundation/roof flags and cleaned labels.
- This response file.

Both theories use hand-made `PageContent` word lists and state their coverage, exclusions and a same-class fault they cannot catch. Tests were written before the production edits; they have not been executed against either version. Existing assertions were retained.

## What the nine sets establish

| Set | Evidence and remaining limit |
|---|---|
| 01783-01 | Synthetic row expects `FLOOR PLAN CEILING PLAN`; the given words already fit the existing column rule. Actual blank remains unexplained. |
| 00904-01 | Same supplied column as 01783; covered by that row, with the same limit. |
| 01746-01 | Synthetic row expects `PLANS`; actual blank remains unexplained. |
| 30996-02 | Synthetic row expects `GENERAL NOTES DEMO PLAN`; actual blank remains unexplained. |
| 31057-01 | Synthetic row expects `LOT C SITE PLAN`; actual blank remains unexplained. |
| 31083-04 | Synthetic TITLE/PLANS row uses the supplied normalized positions and title height; the new label alias addresses this missing label spelling. Corpus result unverified. |
| 01375-01 | All four fused titles are theory rows; sheet digits are removed before floor interpretation. Corpus result unverified. |
| 01788-01 | No field-association rule can be established from a lone PLAN token with no captured label. Need the adjacent title-column labels and words, their bounds and page dimensions to distinguish its field from another caption. |
| 01589-01 | No words supplied, so no evidence-based rule can be established. Need the right-column word lists for pages 7–9, page dimensions, and each token's Text/Cx/Cy/MinX/MinY/MaxX/MaxY, including labels, title lines and sheet numbers. |

For the unexplained blanks, the same complete token data would distinguish a line-grouping or column-boundary failure from text missing before `Read` is called. The present synthetic cases are regression coverage, not evidence that those corpus failures were reproduced.

## Verification

Read the edits back and checked the diff for whitespace errors. No build, test execution, drawing access, database access or network access was performed. Acceptance remains with Claude: the fast suite, nine-set reread and byte-identical six-set gate are **not verified**. In particular, recognizing prefixes changes cleaned labels as well as storey parsing; the six-set gate must check that effect.

Only the five files listed above were changed for this task. Unrelated changes already present in the working tree were left in place.
