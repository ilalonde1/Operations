# Codex review — step 91: a label's own colon is not its value; a right-aligned label's value lies to its left

Commit `4f194e8b` on `develop`. **Reasoning: low. Open ONLY the lines named; no grep, no listing, no build, no
test run. Answer in at most 20 lines.**

## Read (about 7 KB)
1. `Kor.Operations.EngineeringTools.Core/Intake/TitleBlockFields.cs` lines 120–205 — the field loop: beside-tokens
   (the marks before the first word are dropped), `right`, `rightAligned` → `left = -∞`, `FloorWithin`, the
   neighbour-column bound (`o.Cy > floor + 1`), the value lines (`x.t.MinX >= left && x.t.MinX < right`), a value
   line of marks alone skipped.
2. Same file lines 260–273 — `IsValueToken`, `Set`.
3. `Kor.Operations.EngineeringTools.Core.Tests/Intake/ATitleBlocksFieldsAreItsOwnTests.cs` — the tests
   `ALabelsOwnColonIsNotItsValue` and `ARightAlignedLabelsValueLiesToItsLeft` only.

## The rule under review
A label whose last token ends within 15 pt of the strip's rightmost token, with no label after it on its line,
is right-aligned: its column runs from x = −∞ to `right`, and the floor is the next label below anywhere left
of `right`.

## The one question
On a TWO-column block (the kind 30912-01 p20 has: SHEET TITLE in the left column, CHECKED BY / SCALE in the right
column, the right column's labels ending at the strip's edge), does `left = -∞` on a right-column label pull the
LEFT column's title words into that label's value? Trace it: which label is right-aligned, what `right` and the
floor become, and which tokens pass the value filter. Answer "yes, these tokens" with the line numbers, or "no,
because …" citing the bound that stops it.

## Not asked
The colon rule (it is tested); rotated strips; the letter-spaced labels; anything outside the lines named.
