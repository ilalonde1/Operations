# Codex — build task: the small jobs' title blocks read their SHEET TITLE

**Scope: this repository only, the files named below. Write code and its tests. No `dotnet build`, no
`dotnet test`, no drawing files, no database, no network.** Reading set 38 KB measured with `wc -c`.
Recommended reasoning: medium. Work on `develop` HEAD (after `98eb9107`); Claude runs the acceptance —
nine sets re-read in a few minutes — and reports back.

## The problem, measured

Run 11 of the corpus (2026-09-14): 21 of 295 sets build no model because "no storeys: the elevations
chained none and no plan names one". Nine of them are small jobs (1–12 pages) whose plan sheets came
through the reader with a SHEET NUMBER and a BLANK TITLE — so no storey word, so no storey, so no model:

| job | plan page | sheet number read | title read | what the page's title block says (`takeoff vector-words <pdf> <page> --band`) |
|---|---|---|---|---|
| 01783-01 | 3 | S2.01 | (blank) | label `SHEET` `TITLE` at y 220 (x 2339, 2365); above it in page order but at SMALLER y: `FLOOR` `PLAN` at y 199, `CEILING` `PLAN` at y 173; `SCALE:` `1/4"` `=` `1'-0"` at y 239; `CHECKED:` at 258; `DRAWN:` 277; `PROJECT` `NO:` `01783-01` 296. Page 2592 × 1728 pt. |
| 01746-01 | 1 | S2.01 | (blank) | `SHEET` `TITLE` at y 220 (x 2339, 2365); `PLANS` at y 199 (x 2355); `SCALE:` 239; `CHECKED:` 258. |
| 30996-02 | 1 | S1.01 | (blank) | `SHEET` `NUMBER` at y 132 (x 2339, 2371); `DEMO` `PLAN` at y 189 (x 2352, 2407); `GENERAL` `NOTES` at y 215; `SHEET` `TITLE` at y 236 (x 2339, 2365); `DATE:` `SEPT.` `29,` `2022` at y 255. |
| 31057-01 | 10 | S2.00 | (blank) | `SHEET` `NUMBER` at y 131; `SITE` `PLAN` at y 189 (x 2345, 2393); `LOT` `C` at y 214; `SHEET` `TITLE` at y 235; `SCALE:` `N.T.S.` at 255. |
| 31083-04 | 2 | S1.02 | (blank) | `TITLE` at fx 0.92 fy 0.10 of a 3024 × 2160 page; `PLANS` at fx 0.95 fy 0.09, h 17.3 pt. |
| 01788-01 | 8 | S2.01 | (blank) | `PLAN` at fx 0.95 fy 0.11, h 17 pt (the title block's title line; the label was not captured in the band). |
| 00904-01 | 3 | S2.01 | (blank) | same office template as 01783-01 (the file is byte-identical to 01783-01's). |
| 01589-01 | 7, 8, 9 | S2.01–S2.03 | (blank) | a 2022 set (`01589-629 E 12th (Struct Stickfile) 2022-08-08.pdf`); band not harvested — read the page yourself is not possible, so treat it as the acceptance's unknown. |
| 01375-01 | 8–11 | (none) | `S-5FOUNDATION PLAN`, `S-6BASEMENT FLOOR PLAN SHOWING MAIN FLOOR FRAMING OVER` (read as LEVEL 5!), `S-7MAIN FLOOR PLAN SHOWING 2ND FLOOR FRAMING OVER`, `S-8UPPER FLOOR PLAN SHOWING ROOF FRAMING OVER` | an older template with no labels: the sheet number `S-6` and the 48 pt title `BASEMENT FLOOR PLAN` sit on one line and come out fused; the storey reader then reads "5" or "6" from `S-5`/`S-6` as a level. |

The coordinates are PDF points, y up from the page bottom; the title block is the column at the right
edge (x > 2100 on a 2592-wide page). In every KOR title block the SHEET TITLE's value sits BELOW its
label on the page — at smaller y — "over two 16 pt lines, down to SHEET NUMBER" as
`TitleBlockFields.cs` line 8 says; on these small-job templates the label order in the column differs
(`SHEET NUMBER` above `SHEET TITLE` on 30996/31057; `SCALE:` directly under the title's lines on
01783/01746) and the value came back blank. Find out from the source why, and make the column read
these too.

## What to build

1. In `Kor.Operations.EngineeringTools.Core/Intake/TitleBlockFields.cs` (8.6 KB, whole): the rule that
   makes `Read` return `SHEET TITLE` = `FLOOR PLAN CEILING PLAN` (two lines, in page order, top line
   first) for 01783's column, `PLANS` for 01746, `GENERAL NOTES DEMO PLAN` for 30996 (a larger y is higher on the page: top line first), `LOT C SITE PLAN`
   for 31057 — WITHOUT breaking the 31138/31168 template the file was written for (its tests:
   `Kor.Operations.EngineeringTools.Core.Tests/Intake/TitleBlockFieldsTests.cs` if present — `grep -rl
   TitleBlockFields Kor.Operations.EngineeringTools.Core.Tests` — read them first; every existing
   assertion must still hold). State the rule in one sentence in the class summary.
2. The fused sheet number: in `Kor.Operations.EngineeringTools.Core/Intake/SheetViews.cs` (find
   `Titles` and the sheet-number reading; ~40 KB, read only the title/sheet-number parts, about 8 KB)
   or `Core/Dxf/PlanSheetNaming.cs` `StripSheetNumber` (find it; ~3 KB of 30) — whichever splits a
   sheet number from a title — `S-5FOUNDATION PLAN` → number `S-5`, title `FOUNDATION PLAN`;
   `S-6BASEMENT FLOOR PLAN SHOWING …` → `S-6` + `BASEMENT FLOOR PLAN SHOWING …`. A sheet number is
   `S`, an optional separator, digits with optional dots/dashes; glued to a following word that starts
   with a letter, it is still the sheet number. Do NOT let `S-5` become a level 5: the storey readers
   take numbers from the title after the sheet number is stripped (`PlanSheetNaming.Parse` — read its
   first 60 lines).
3. Tests, in the existing test classes for those files (find them by `grep -rl`), one `[Theory]` per
   rule with the rows above as `InlineData`, built from hand-made `PageContent` word lists (see how the
   existing TitleBlockFields tests build a page; `VectorPageReader.TextToken` is
   `(Text, Cx, Cy, Width, Height, …)` — read its declaration at `Core/VectorPageReader.cs` line 32).
   Each test's summary states WHAT IT COVERS and WHAT IT DOES NOT (rule 11 of `CLAUDE.md`).

## Read, in this order

1. `CLAUDE.md` rules 5, 7, 11 (3 KB of 15).
2. `Core/Intake/TitleBlockFields.cs` — whole (8.6 KB).
3. Its tests (`grep -rl TitleBlockFields Kor.Operations.EngineeringTools.Core.Tests`), whole.
4. `Core/VectorPageReader.cs` lines 28–60 (2 KB): `TextToken`, `PageContent`.
5. `Core/Intake/SheetViews.cs`: the sheet-number and title reading (grep `SheetNumber`, `Titles`,
   `StripSheetNumber`; about 8 KB of 40).
6. `Core/Dxf/PlanSheetNaming.cs`: `Parse` and `StripSheetNumber` (about 4 KB of 30).

## Rules for the change

- Warnings are errors; xUnit analyzers on. Rule 7: regexes through raw strings or the Edit tool only.
- Change nothing in `GeometryFilterService.cs`, `PdfOnlyBuild.cs`, `DrawingIntake.cs`, `Baselines/`.
- A rule, not a template: no "if the job number starts with 01". The label column and its order are
  the facts; say what the rule is in the summary.

## Acceptance (Claude runs; you do not)

Fast suite green; then `corpus-analyze --jobs 00904-01,01375-01,01589-01,01746-01,01783-01,01788-01,30996-02,31057-01,31083-04 --force`
(nine small sets, a few minutes) and the sheet ledger shows a title on every plan page above; then the
six-set gate — the reader changed, so it reads (8 min) and must stay byte-identical.

## Output

The code, in place, and `docs/codex/CODEX-PDF-INTAKE-SMALL-JOB-TITLES-RESPONSE.md`: the rule in a
sentence, the files and members changed, and which of the nine you could not make a rule for from
the words above (01589-01 has none listed; say what you need from it).
