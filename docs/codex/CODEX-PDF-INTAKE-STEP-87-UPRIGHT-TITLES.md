# Codex review — step 87: words written up the page are read up the page (title guess)

Commit `4f194e8b` on `develop`. **Reasoning: low. Open ONLY the lines named; no grep, no listing, no build, no
test run. Answer in at most 20 lines.**

## Read (about 9 KB)
1. `Kor.Operations.EngineeringTools.Core/SheetTitleReader.cs` lines 97–215 — `TitleText`: the field first; then the
   guess: candidates split into horizontal words and upright words (`IsUpright`, `Swapped`: a column becomes a
   line read bottom-up, the font size becomes the height), each read into blocks, the largest block that names
   a plan wins; a one- or two-glyph word is upright when it stands in an upright column at its size; a block is
   a 2-D stack of runs (a run joins the block whose last run is within two heights above it AND overlaps it).
2. `Kor.Operations.EngineeringTools.Core/Intake/TitleBlockFields.cs` lines 211–240 — `ReadingTokens`, `IsUpright`,
   `Swapped`.
3. `Kor.Operations.EngineeringTools.Core.Tests/Intake/ATitleBlocksFieldsAreItsOwnTests.cs` — the test
   `AMixedStripReadsItsUprightWordsUpThePage` only (01589 p7 and 30941 p16 at their real geometry).

## The rule under review
On a block that is NOT wholly rotated, the upright words are read as columns and their blocks compete with the
horizontal blocks; the winner is the largest block (by font size) that `SheetViews.NamesAPlan` accepts.

## The one question
Name ONE shape of title block on which this rule returns the WRONG text where the old rule (horizontal lines
only) returned the right one — or say there is none you can construct from the code. A wrong text means: a
block that names a plan but is not the sheet's title (an upright consultant tagline, an upright project name,
a section marker column) outranking the true title. Give the token layout that produces it (x, y, width,
height, text — five tokens at most) and the line of `TitleText` that lets it through.

## Not asked
The field reader; rotated-strip detection; NamesAPlan's vocabulary; anything outside the lines named.
