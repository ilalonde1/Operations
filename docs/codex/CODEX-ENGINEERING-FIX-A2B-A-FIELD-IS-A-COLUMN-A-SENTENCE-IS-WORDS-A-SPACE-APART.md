# Engineering fix A2b: a field label stands in its own column; a word in a sentence does not

**Budget.** This brief is about 3 KB, and you read two ranges you have already seen.

**Rules for this run:**
- Open nothing else. No repo search, no git.
- Do **not** build or test; Claude verifies.
- Reasoning: **medium**.

**Response:** `docs/codex/CODEX-ENGINEERING-FIX-A2B-A-FIELD-IS-A-COLUMN-A-SENTENCE-IS-WORDS-A-SPACE-APART-RESPONSE.md`,
under 20 lines.

## Measured 2026-10-19 on your A2 build

The fast Core suite gives 1,626 passed and 4 failed. Your three new gates pass. Your colon rule broke four existing
fixtures, each a real office's block:

| Test | Office | The label your rule refused |
|---|---|---|
| `AnOfficeLabelEndsTheTitleBeforeTheNextFields` | 30878-02 p35 | `PROJ. #  30878-02  DRAWING NUMBER` |
| `ARotatedStripsLabelsStandInTheStripNotInOneColumn` | rotated strip | `SHEET TITLE` lost |
| `ARightAlignedLabelsColumnStartsWhereTheLabelBeforeItEnds` | two-column block | `SHEET TITLE  CHECKED BY` |
| `AJobTitleCannotReplaceTheDrawingsTitleOrNameAnUntitledSheet` | 01379-01 p77 | `DATE  DRAWN`, and the compound-label variants away from the line's start |

In every one, the colonless mid-line label stands in a **new column**:
- 30878's `DRAWING NUMBER` starts about 38 pt after `30878-02` ends, at an 8.1 pt height;
- 01379's `DRAWN` starts about 100 pt after `DATE`.

A105's `SCALE` stands one ordinary word-space after `NOT`, in `DO NOT SCALE THE DRAWINGS.`.

## The fix, in `TitleBlockFields.Read` only

Replace your colon-only condition. A label found after the first token of its line (`i > 0`) is accepted when **any**
of these holds:
1. **A column gap:** the horizontal gap from the previous token's `MaxX` to this label's first token's `MinX` is at
   least `2.0 ×` the larger of the two tokens' heights.
2. **A label before it:** the previous token on the line is itself part of a label already found on this line.
3. **A colon:** the label's last token ends with `:`, or the next token is `:`.

Otherwise it is a word in a sentence, and is not a label.

Keep:
- line-start labels and letter-spaced runs exactly as they are;
- your three new gates, unchanged.

Rewrite your comment to state this rule and cite both A105's sentence and 30878's `PROJ. #  30878-02  DRAWING NUMBER`.

**Acceptance.** All four fixtures above pass **unchanged**, along with your three gates. If a fixture places its
tokens so that the gap rule cannot tell, report which one and its coordinates. Do not edit the fixture.

## Read

- `Kor.Operations.EngineeringTools.Core/Intake/TitleBlockFields.cs` lines 85-130, where your change is
- `Kor.Operations.EngineeringTools.Core.Tests/Intake/ATitleBlocksFieldsAreItsOwnTests.cs`: the four tests named
  above, and the `Line(...)` / `At(...)` helpers they use (to read the token widths and gaps)

## Do not touch

Any other file.
