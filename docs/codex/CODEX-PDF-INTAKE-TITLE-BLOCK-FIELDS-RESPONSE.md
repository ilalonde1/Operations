# Title-block fields — response

Started on `develop` at `244ac329`, with source review only. HEAD advanced externally to `150c6ec5` during the task; the three implementation files and existing title-block test file have no committed differences between those revisions. Changed `TitleBlockFields.cs`, the `SheetTitleReader.TitleText` heuristic, `SheetDxfName.cs`, and the new `ATitleBlocksFieldsAreItsOwnTests.cs`; this response is the only other file written. Existing unrelated working-tree changes were left alone.

## Rules (one sentence per defect)

1. **Field boundaries:** every recognised office form label ends the preceding field in its column, punctuation does not change the label's identity, and an adjacent column labelled above the current field also bounds its width (30878-02 p35, 30912-01 p20, 01379-01 p77).
2. **Revision marks:** discard a single digit alone on an interior title baseline in the rightmost fifth of the field, while preserving a final lone-number line and numbers farther inside the column (30912-01 p20).
3. **Short glyphs:** establish word baselines using their tallest word before assigning punctuation to the nearest compatible baseline, then read each line by X position (30926-01 p18).
4. **Rotated strips:** three distinct tall labels aligned within a token-width X band and spread over more than three line heights establish swapped reading axes for both readers, with an unlabelled title restricted to its anchor's constant-X column (01589-01 p7).
5. **Title identity:** match compound labels before their standalone words at every line position, treat DRAWING TITLE as SHEET TITLE while retaining JOB/PROJECT TITLE separately, and exclude stated job/project names from every sheet-naming candidate (01379-01 p77).

## Implementation and tests

`TitleBlockFields` adds the brief's office labels, including `DRAWING NO`, `DRAWING NUMBER`, `DRAWING #`, `PROJ. #`, `PROJECT #`, `JOB NO`, `JOB NUMBER`, `JOB TITLE`, `JOB #`, `PLOT DATE`, `FILE`, `SHEET` and `ISSUES`. Comments distinguish labels observed on the named pages from companion spellings supplied without a page attribution. Compound labels are matched before single-token labels, including when the compound itself occupies one token. `DRAWING TITLE` now produces the canonical `SHEET TITLE` key.

Both readers use the same rotation detection and coordinate conversion: reading X is original Y, reading Y is negative original X, and bounding boxes are transformed consistently. Consumed-token coordinates are converted back to the original PDF coordinates. This keeps the existing field reader rather than introducing a separate rotated-field parser. The fallback heuristic uses font thickness after transformation and selects the anchor column, excluding nearby revision columns.

`SheetDxfName` filters candidate values against both stated project-title fields, ignoring case and repeated whitespace, including after stripping a leading sheet number from fallback text. Both sheet-naming overloads retain the existing fallback stem when no sheet title remains; a valid DRAWING TITLE remains available when another candidate repeats the project name.

Five test methods contain these required assertions; these are written expectations, **not executed results**:

| Evidence | Expected reader title |
|---|---|
| 30878-02 p35 | `ROOF PLAN - BUILDING K` |
| 30912-01 p20 | `LEVEL -4 PLAN - CONCRETE OUTLINE` |
| 30926-01 p18 | `LEVEL 5 - LEVEL 14 PLAN` |
| 01589-01 p7 | `FOUNDATION PLAN` via `SheetTitleReader.TitleText`; the supplied strip has no title label |
| 01379-01 p77 | `GALLERIA PART PLANS` |

The methods also cover companion label boundaries, a synthetic neighbouring CHECKED BY column, final/interior numeric counterexamples, reversed input order, original consumed coordinates after rotation, compound labels away from the start of a line, and rejection of project names in naming fallbacks. Each method states its page, coverage and limits. The existing two title-block tests are unchanged; source inspection found no contradiction with their expected results, but they have not been run.

## What the evidence cannot decide

- **The actual cause of the reported displaced dash is not uniquely recoverable.** With a line seed at `Cy = 210.5`, height `12.5`, and the dash at `207.1`, the old comparison accepts it: `3.4 <= 6.25`, regardless of how short the dash box is. The old code actually compares against the first token of a page-wide line, which need not be that word. A synthetic 3 pt `+` at `Cy = 214` in another column demonstrates the source failure: LEVEL joins its line using LEVEL's height, but the dash is then compared against the small seed and rejected (`6.9 > 1.5`). The test identifies that added seed as synthetic, using the `+` token from the brief's revision lettering; it does not claim that seed existed on 30926. The change removes this unstable reference, but the brief's abbreviated tokens alone cannot prove why that actual page failed.
- **Rotation direction is absent from TextToken.** The implementation assumes bottom-up along Y, as FOUNDATION before PLAN requires here; the boxes cannot establish that all strips read that way. Mixed-orientation blocks and wrapped unlabelled rotated titles are not established by this evidence. The title heuristic deliberately stays within one rotated column.
- **Several boxes and positions are missing.** Tests preserve reported centres/baselines and assume labels of 8.1 pt, titles of 12.5 pt on 30926 and 13.8 pt on 30912, and 13.8 pt for other title text; rotated labels/titles use those values as X thickness. Dash boxes are assumed 4 by 1.8 pt. Unreported column positions, CHECKED BY coordinates and individual revision-row Y positions are explicitly synthetic. The label-count, span and rightmost-fifth thresholds are general geometry choices, not measured calibration across the corpus.
- **A final lone number is ambiguous.** It is preserved, accepting that a revision digit on the last title baseline can remain; a right-edge digit between genuine title lines is excluded by the requested rule.
- **Project identity requires a stated field.** The namer can reject text matching JOB TITLE or PROJECT TITLE; it cannot infer that an otherwise unlabelled name is a project, or recognise a differently abbreviated version semantically. The supplied vertical address fragments also lack enough boxes to establish every REVISION/DATE field value.

## Row to bank

`dxf.pdf.title-block-labels` in KorStandards, extending the compiled defaults through the existing vocabulary approach in `PdfIntakeOptions`. No row, database change or options wiring was added in this task.

## Verification

Source and diff review only, with a whitespace check. No build, test execution, drawing/data-file read, database access or network access. Build, the new and existing tests, the fast suite and the six-set gate remain for the user to run as specified by the brief.
