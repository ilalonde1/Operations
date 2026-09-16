# Codex review — step 90: a title that is a storey's name and nothing else is that storey's plan

Commit `4f194e8b` on `develop`. **Reasoning: low. Open ONLY the lines named; no grep, no listing, no build, no
test run. Answer in at most 20 lines.**

## Read (about 5 KB)
1. `Kor.Operations.EngineeringTools.Core/Intake/SheetViews.cs` lines 150–192 — `NamesAStoreyAlone`: a leading
   sheet number is dropped; every word must be a storey token (`^(?:[A-Z]{1,2})?\d{1,2}[A-Z]?$` starting with a
   digit or a level/parkade letter; a floor word; a basement, top-floor or roof word) or a joining word (level,
   parkade, floor-noun, range, mezzanine words; AND, &, PODIUM, PARKING, PARKADE, MECH, MECHANICAL, -); at
   least one storey token.
2. `Kor.Operations.EngineeringTools.Core/Intake/DrawingIntake.cs` lines 576–618 — `SheetTypes` (the kinds by
   their words, asked first) and `SheetTypeOf` (the new rule after them).
3. `Kor.Operations.EngineeringTools.Core.Tests/Intake/ASheetIsItsViewsTests.cs` — the test
   `ATitleThatIsAStoreysNameAloneIsThatStoreysPlan` only.

## The rule under review
After SCHEDULE / PLAN / SECTION|ELEVATION / DETAIL / NOTES / COVER have had their say, a title made only of
storey words types the sheet "plan".

## The one question
Name ONE title a structural set gives a sheet that is NOT a plan and that this rule types "plan" — e.g. a wall
elevation sheet titled "LEVEL 2 - 5" alone, a loading sheet "LEVEL 3 LOADS"? (LOADS is not a storey word — say
so if it is refused.) Give the title and the word-by-word trace. If none can be built from the word lists,
say so and name the storey token regex's widest accidental match (a mark like "SW1"? "C12"?) and whether
`char.IsDigit(w[0]) || starts with a level/parkade letter` refuses it.

## Not asked
The composer's use of the type; the vocabulary rows; anything outside the lines named.
