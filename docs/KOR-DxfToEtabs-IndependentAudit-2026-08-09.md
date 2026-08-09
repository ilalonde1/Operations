# Independent audit of the DXF→ETABS generator — 2026-08-09

An external read-only review, run before the models went to the engineer. Kept verbatim because the
findings are the record of what an independent reviewer catches that the author does not: it found
five real defects in an hour, two of which would have reached her.

All five were reproduced independently before anything was changed, and all five are fixed in
commit `7777b290`. The suspected finding is now flagged by the tool rather than shipped silently.

**Why it worked:** the reviewer did not share the author's assumptions. Both of the expensive bugs
this week came from a reader and a writer agreeing with each other — see
`feedback_never_validate_output_through_your_own_reader` in the session memory.

---

## Verbatim report

```
Read-only audit completed. I did not build, run tests, modify repo files, commit, or run ETABS.

Confirmed Findings

1. 31168 has duplicated generated walls and columns in the shipped .e2k.

File: \\Kor-fs01\...\31168-FROM-DRAWINGS.e2k

Evidence:
- Independent text parse found 24 duplicate generated wall panel storey-locations and 19 duplicate generated column storey-locations.
- Example column duplicate:
  - line 1131: POINT "KP617"  -899.0405 3572.8254
  - line 4843: LINE  "KC249"  COLUMN  "KP617"  "KP617"  1
  - line 6694: LINE  "KC2100"  COLUMN  "KP617"  "KP617"  1
  - line 8763: KC249 assigned to "A-LEVEL 33"
  - line 10810: KC2100 also assigned to "A-LEVEL 33"
- Example wall duplicate:
  - lines 515-516: KP1/KP2
  - line 7159: AREA "KW77" ... "KP1" "KP2" "KP2" "KP1"
  - line 8067: AREA "KW809" ... "KP1" "KP2" "KP2" "KP1"
  - lines 11672 and 12644 both assign that same wall segment to "A-LEVEL 33".

Engineer impact: duplicated stiffness and self-weight where those members overlap, especially around A-LEVEL 33.

Adversarial pass: this is not just multi-storey assignment. The duplicate pairs are distinct KOR objects, same points, same section, same storey. This survives.

2. The shipped dossier/PDF counts are stale and contradict the shipped models.

File: \\Kor-fs01\...\KOR-Model-From-Drawings-DOSSIER.pdf extracted text lines 22-30.

PDF says:
- line 24: Columns ... 2,425 for 31168 and 162 for 31138.
- line 28: Headers ... 5 for 31138.
- line 30: Shaft and stair openings cut ... 1 for 31138.

Actual shipped .e2k counts:
- 31168: 2426 columns, proven by KC2426 at line 7020; report line 5 also says 2426.
- 31138: 165 columns, proven by KC165 at line 2798; report line 5 says 165.
- 31138: 8 headers, proven by KS8 at line 3142.
- 31138: 2 openings, proven by KO2 at line 3157.

Engineer impact: the handoff document does not describe the file she is receiving. It also undermines any claim that the counts are audit-grade.

Adversarial pass: the .e2k object names are sequential and the report agrees with the .e2k, so this is not a parsing artifact. This survives.

3. The 31138 questionnaire contains 31168-specific questions and facts.

File: \\Kor-fs01\...\31138-QUESTIONS-for-Andrea.xlsx

Evidence:
- Questions!B6/C6 asks about a stepped block and says all 70 fell through to columns. That is the 31168 C1 case.
- Questions!B7/C7 says 31168's P1, P2 and P3 each carry one plate of about 75,800 sq ft.
- Questions!B8 lists LEVEL 1 MEZZ, C-LEVEL 3 and B-LEVEL 28; those are 31168 storeys, not 31138.
- The 31138 report instead says its own plateless storeys are L01, L02, L03, L04, L05, L06, L08, L11, L14, L16, L17, L20.

Engineer impact: Andrea opens the 31138 workbook and is asked to rule on the wrong building.

Adversarial pass: these are actual worksheet cells, not unused shared strings. This survives.

4. Header depth documentation is false for both shipped models.

Files:
- Kor.Operations.EngineeringTools.Core\Dxf\ModelQuestionnaire.cs:36-38
- Kor.Operations.EngineeringTools.Core\Dxf\E2kGeometryComposer.cs:334
- both *-QUESTIONS-for-Andrea.xlsx, Questions!B9/C9

What is wrong:
- The workbook says headers are generated 24 inches deep.
- The writer actually uses storeyHeight - OpeningHeight, clamped to 24-60 inches.

Evidence from shipped models:
- 31168 header depths by actual KOR 3D point offsets: 24:8, 27:26, 30:2, 32:83, 38:8, 55:2, 56:4, 60:8.
- 31138 header depths: 32:3, 60:5.
- Example 31138 line 2176: POINT "KP83" ... 60; KS8 at line 3142 is a generated header.

Engineer impact: she is asked to approve a 24-inch header assumption while most generated headers are not 24 inches.

Adversarial pass: the PDF correctly describes the dynamic 24-60 inch rule, but the workbook she answers does not. This survives as a handoff defect.

5. The integrity tests miss the duplicate class that is present in 31168.

File: Kor.Operations.EngineeringTools.Core.Tests\ModelIntegrityTests.cs

Evidence:
- The duplicate check under NothingIsModelledTwiceAndNothingIsDroppedInSilence only checks floor plates: line 172 says "No two plates..."; the regex targets AREA "(KF\d+)" around lines 176-178.
- It does not check KW walls or KC columns.
- The writer dedupes before multi-storey expansion: E2kGeometryComposer.cs:240 for walls and :280 for columns, then assigns using StoreysSpannedBy(story) at :266 and :315. That is exactly how two different source placements can collide on the same interleaved storey.

Engineer impact: the test name says "NothingIsModelledTwice," but the shipped model still has duplicated walls/columns.

Adversarial pass: this is not hypothetical; the shipped 31168 file contains the duplicates. This survives.

Suspected

- 31168 has one generated KOR-C65x82 column: section line 217, point line 1704, line object line 5032, assignment line 9115 on LEVEL 1 MEZZ. That is a 65x82 inch generated column. It may be a wall/pier mis-typed as a column, but I cannot prove that from the .e2k alone. Andrea's 31138 reference does contain large columns up to 36x72, so size alone is not proof.

Dropped After Adversarial Pass

- ANG 360 appears 70 times in 31138 and not in the closest references. I dropped it because 360 degrees is equivalent to 0 unless ETABS import proves otherwise.
- Missing CONCRETESECTION records for generated KOR frame sections looked suspicious, but the stated scope says section/design properties are Andrea's responsibility, so I did not keep it as a defect.
- I did not report the known-open plateless 31168 storeys or the 25 known unresolved 31168 outlines.
```

---

## How each was resolved

| # | Resolution |
|---|---|
| 1 | Deduplication now tested against every storey a member is *assigned* to, all-or-nothing, quantised to the inch (two columns 0.004″ apart are one column). |
| 2 | Publishing now compares every count the dossier states against the model and refuses to ship on disagreement. Proven by feeding it a dossier reading 2,425 against a model of 2,426. |
| 3 | Questions are built from the report of the project being written, not fixed prose. |
| 4 | The question now states the rule — storey height less opening, clamped 24–60″. |
| 5 | `NothingIsModelledTwiceAndNothingIsDroppedInSilence` now covers walls and columns. |
| Suspected | Columns wider than 48″ on both faces are now flagged for review rather than shipped silently. |

## The prompt that produced it

Kept because the framing did the work. Two things in it earned the findings: it points at the
*classes* of failure already found rather than giving a checklist, and it forbids verifying format
claims against this repo's own reader, requiring real models on the share as ground truth instead —
the one constraint that would have caught the two most expensive bugs of the week, and the one thing
a fresh reviewer can do that the author structurally cannot. A generic "review the code" prompt
would have re-found what was already fixed and missed all five of these.

Verbatim, as issued:

```
Goal: Adversarially verify a DXF→ETABS model generator and the two models it produced, before they
go to a structural engineer. Find what would embarrass us in front of her. Report findings; do not
fix them.

Context. C:\VIsual Studio Projects\Operations — Kor.Operations.EngineeringTools.Core\Dxf\ is the
generator, Kor.Operations.EngineeringTools.Core.Tests\ its 430 tests. It reads drafting's
concrete-outline DXF plans and writes geometry into an ETABS .e2k text model. Two outputs, both
under \\Kor-fs01\Projects\Projects\03 Residential\:

- 31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\31168-FROM-DRAWINGS.e2k
  — 918 walls, 2,426 columns, 83 plates, 141 headers
- 31138-01 (2170 W 1st Ave Vancouver BC)\...\31138-FROM-DRAWINGS.e2k — 89 walls, 162 columns, 11 plates

Ground truth available to you. ~400 real engineer-built ETABS models exist under
\\Kor-fs01\Projects\Projects (*.e2k, *.$et). Andrea Neuviale's own hand-built model,
31138-reference-from-Andrea-gravity.e2k, is the closest oracle. Verify format claims against those
files, never against this repo's own reader.

The failure classes already found — look for more of the same kind, not the same instances:
1. Reader and writer sharing a wrong assumption, so they validate each other and the file is
   nonsense. Twice: point Z read as elevation when it's a storey offset; storey heights corrected in
   the reader while the file still said 13,366.
2. Silent drops — geometry read from a drawing, matched no rule, modelled as nothing, leaving no
   trace in any count. 37 found.
3. Silent duplicates — the same floor modelled twice because two sheets cover it. 43 found.
4. Plausible-but-wrong classification — 160 chamfered square columns modelled as circles because
   every shape test says circle.

What to check, in priority order:
- Does anything in either .e2k violate how real models on the share are written? Compare
  connectivity forms, assign syntax, section/material declarations, storey lists.
- Is any geometry read from the DXFs and then silently lost, doubled, or mis-typed?
- Are the counts quoted in …-QUESTIONS-for-Andrea.xlsx and docs\KOR-DxfToEtabs-web.pdf actually true
  of the shipped files?
- Do the tests in ModelIntegrityTests.cs and ModelPlausibilityTests.cs actually fail on the bugs they
  claim to catch, or are they vacuous?
- Are the engineer's rulings implemented as stated? They are in
  KOR.Drafter\db\029_SeedAnalysisKnowledge.sql and 031_*.sql, and in ModelQuestionnaire.cs.

Known-open, do not report as findings: three storeys have no floor plate (no closed outline exists in
the drawing); 25 outlines on 31168 read and modelled as nothing; stepped corner blocks modelled as
one pier rather than split (asked as question C1); neither file import-tested beyond one manual open.

Constraints: read-only. Do not build, do not run the test suite, do not modify or commit anything, do
not touch \\Kor-fs01 except to read. Do not run ETABS.

Output: a ranked list. For each finding: the file and line, what is wrong, the evidence that proves
it (a count, a quoted line from a real model, a specific coordinate), and what an engineer would see.
Separate confirmed from suspected. If you cannot prove it, say so.

Finish with an adversarial pass on your own findings: for each, argue the case that it is not a
defect, and drop the ones that argument wins.
```

Note the counts in the prompt (918 walls / 2,426 columns for 31168, 89 / 162 for 31138) are the
pre-fix numbers. After the duplicates in finding 1 were removed the shipped files read 900 / 2,407
and 88 / 165.
