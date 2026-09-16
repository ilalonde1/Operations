# Codex review — step 86: a letter-spaced label is a label; the floor label is not a neighbour column

Commit `4f194e8b` on `develop`. **Reasoning: low. Open ONLY the lines named; no grep, no listing, no build, no
test run. Answer in at most 20 lines.**

## Read (about 6 KB)
1. `Kor.Operations.EngineeringTools.Core/Intake/TitleBlockFields.cs` lines 83–122 — the label loop: one or two
   tokens spelling a label, else a run of three or more single-letter tokens on one line compared, joined, to
   each label with its spaces removed (the longest run that spells a label wins).
2. Same file lines 165–185 — the neighbour-column bound: a label counts as a neighbour only when
   `o.Cy > floor + 1` (on the floor line it is the floor).
3. `Kor.Operations.EngineeringTools.Core.Tests/Intake/ATitleBlocksFieldsAreItsOwnTests.cs` — the test
   `ALetterSpacedLabelIsALabelAndAnEmptyTitleBoxIsNoTitle` only (30980 p16 at its real geometry).

## The rule under review
Single letters in a row spell a label. The run may start at any single letter and the longest spelling wins.

## The one question
A row of grid bubbles at the top of a plan reads as single-letter tokens "A B C D E …" on one line; the
right fifth of a sheet can hold the end of that row. Does any Labels entry, with spaces removed, equal a run of
consecutive alphabet letters or a run the bubbles could produce (e.g. "SEAL", "DATE", "FILE", "REV")? Answer
by listing the Labels entries of four letters or fewer and stating for each whether a bubble row could spell
it; if one can, say what stops the false label from cutting a field (the reach to a line end? nothing?).

## Not asked
The empty-title-box rule; `TitleText`; rotated strips; anything outside the lines named.
