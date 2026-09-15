# Codex — the title block's fields: five defects run 14 exposed, one rule each

**Reading set: about 45 KB at HEAD on `develop` (the commit in the prompt) — three source files and one test
file below, plus the evidence in this brief. Write code and tests in the repo; no build, no test run, no
drawings, no database, no network, nothing under `%LOCALAPPDATA%`. I build, run the tests, the fast suite and
the six-set gate, and commit by named files.**

## What this is

`TitleBlockFields.Read` reads a sheet's title block by LABELS (SHEET TITLE, DRAWN, SCALE …): a label's value
is what stands beside it on its line, else the lines below it in its column down to the next label line.
`SheetDxfName.For` names the sheet's DXF from the SHEET TITLE (or DRAWING TITLE) field, and every storey,
building and roof the composer reads comes from that name. Step 68 (2026-09-14, your build) added the short
labels the small-job template uses (a standalone TITLE, DRAWN:, CHECKED:) and it read the real titles of
sets that had never had one — and the corpus run after it (run 14, `docs/PdfIntake.md` §80) showed five
ways the field read is wider or narrower than the title. Each is one universal rule about title blocks,
not a fix for one set. The evidence below is the page's own words (`takeoff vector-words`, page points,
y up, the title-block band only) and, for each page, WHAT THE READERS RETURNED on it (run on 2026-09-15
against the mirrored PDFs: `TitleBlockFields.Read`, `SheetTitleReader.TitleText`), so nothing here needs
a drawing and nothing is inferred.

## Read, in this order

1. `Kor.Operations.EngineeringTools.Core/Intake/TitleBlockFields.cs` — whole file (9 KB).
2. `Kor.Operations.EngineeringTools.Core/Intake/SheetDxfName.cs` — whole file (7.6 KB): how the fields
   become the name, and what it falls back to when SHEET TITLE is missing.
3. `Kor.Operations.EngineeringTools.Core/VectorPageReader.cs` lines 30–40 only: `TextToken` (Text, Cx, Cy,
   MinX, MinY, MaxX, MaxY; Width, Height). A rotated glyph run comes through with the same fields.
3a. `Kor.Operations.EngineeringTools.Core/SheetTitleReader.cs` lines 81–138 only: `TitleText` — the field
   first, else the size heuristic on the right edge (defect 4 comes through the heuristic).
4. `Kor.Operations.EngineeringTools.Core.Tests/Intake/TheAuditsCounterexamplesTests.cs` lines 250–320: the
   two existing title-block tests and how they build a `PageContent` from tokens (18 KB file; those lines).
5. `docs/PdfIntake.md` §78 and §80's "title-reader defects" paragraph (about 3 KB) — what was measured.

## The five defects, with the page's words

**1. A label the block uses is not in `Labels`, so the field runs on into the next field.**
30878-02 p35 (3456 × 2592 pt), the column under SHEET TITLE:

    y 281.2  SHEET TITLE
    y 253.9  ROOF PLAN
    y 232.2  - BUILDING K
    y 120.1  PROJ. #  30878-02   DRAWING NUMBER
    y  81.7  SCALE 1/8"=1'-0"        (y 79.0  S2.22, beside)
    y  62.4  DRAWN K. FRANK

The reader: SHEET TITLE = `ROOF PLAN - BUILDING K PROJ. # 30878-02 DRAWING NUMBER` (SCALE is the first
label line below); also REVISION = `a m + h h i t e c Street Columbia …` (letters of a vertical word
joined) and DATE = `h i l l t s V6C 3A8 NUMBER`.
30912-01 p20 (3456 × 2592), the column under TITLE: — the same shape with `DRAWING NO:` and `S2.02.1`:

    y 255.2  TITLE:
    y 230.6  LEVEL -4 PLAN      (y 226.7, x 3393.9: "-"  — a trailing dash 3.9 pt below the line)
    y 209.5  CONCRETE
    y 197.7  1                   (x 3377.1 — alone on its baseline, at the column's right edge)
    y 188.3  OUTLINE
    y 124.7  DRAWING NO:
    y  90.1  S2.02.1

The reader: SHEET TITLE = `LEVEL -4 PLAN - CONCRETE 1 OUTLINE DRAWING NO: S2.02.1`; and CHECKED BY =
`Checker PM PLAN - 1` — the CHECKED BY column (its right edge defaulted to MinX + 260) swallowed title
tokens too. 01379-01 p77 has `JOB NO.` / `JOB TITLE` (SCALE read `1/8" = 1'-0" JOB NO. 215369 S212.9`).
The rule: the labels an office's blocks carry are the labels (`DRAWING NO`, `DRAWING NUMBER`, `DRAWING #`,
`PROJ. #`, `PROJECT #`, `JOB NO`, `JOB NUMBER`, `JOB TITLE`, `PLOT DATE`, `FILE`, `SHEET` …) — and say in
the file which drawing each came from. The list is compiled today; note in your response that it wants to be
a KorStandards row (`dxf.pdf.title-block-labels`, extending the compiled defaults, as the other vocabularies
do in `PdfIntakeOptions` — do NOT add the row yourself; name it).

**2. A lone revision mark between two title lines is read as a title word** — the `1` at (3377.1, 197.7)
on 30912 p20 above: its own baseline, one token, a digit, at the far right of the column, between CONCRETE
and OUTLINE. What is it? A REV cell beside the title lines. A rule that says what a title line IS (words of
the same text on one baseline; a lone number on a baseline of its own at the column's right edge is a mark,
not a word) — stated in the test with its counterexample: a title whose LAST line is a lone number is rare;
say what you chose.

**3. A short glyph on a slightly lower baseline falls out of its line** — 30926-01 p18 (2592 × 1728), under
`Title` at y 228.1:

    y 210.5  LEVEL 5 [x 2370, 2408]    LEVEL 14 [x 2456, 2499]
    y 207.1  -  [x 2420.9]
    y 190.2  PLAN

The reader: SHEET TITLE = `LEVEL 5 LEVEL 14 - PLAN` — the dash (its baseline 3.4 pt below the line's;
the LEVEL tokens are 12.5 pt high, the dash's own box is whatever a hyphen's is) ended on a line of its own,
after LEVEL 14. Say from the code exactly why — the line test is `|l[0].Cy − t.Cy| ≤ max(l[0].Height,
t.Height) × 0.5` and on these numbers it should have joined, so either `l[0]` is not the token you expect
or the hyphen's box is not — and fix the cause, not the symptom. The title is `LEVEL 5 - LEVEL 14 PLAN`, a
range of ten storeys; read as two levels the range is lost (30926 built 11 storeys for a 15-storey
building). On 30912 the trailing "-" 3.9 pt below its line DID join (`LEVEL -4 PLAN -`).

**4. A rotated title strip is read as horizontal lines** — 01589-01 p7 (2592 × 1728): the block is a strip
along the right edge, rotated 90°: every field's tokens share an X and run along Y, and the reader (lines by
Cy) joined tokens from DIFFERENT fields that happen to share a y:

    x 2270.3  ISSUES:      (y 528.9)          x 2277.3  DRAWN     (y 165.9)   BY: (y 165.9, x 2311.9)
    x 2270.4  DATE:        (y 759.3)          x 2277.7  SCALE:    (y 111.7)   1/4" = 1'-0" (x 2341–2371)
    x 2270.5  JOB          (y 191.6)  #: (x 2293.9)  01589-01 (x 2360.7)
    x 2292.3  ISSUED FOR BUILDING PERMIT  DEC. 6, 2021    (y 526 .. 793, one revision row)
    x 2316.3  ISSUED FOR BUILDING PERMIT  APR. 12, 2022
    x 2340.2  ISSUED FOR BUILDING PERMIT  JUN. 6, 2022
    x 2439.1  FOUNDATION (y 311.0)   PLAN (y 424.8)          ← the sheet title, reading along y
    x 2284.2  RESIDENCE (y 403.9)  PRIVATE (y 275.6)          x 2322  Proposed (y 268) Renovation (y 363)

The readers: `TitleBlockFields` finds no SHEET TITLE here (the labels stack along x, not along a line),
DATE = `JUNE 6, 2022 S2.01`, SEAL = `6, 12, 6, of JOB #: 01589-01`; then `SheetTitleReader.TitleText`'s
size heuristic (the field being absent) returns `PERMIT PERMIT PERMIT BUILDING BUILDING BUILDING ISSUED
ISSUED ISSUED PLAN RESIDENCE FOUNDATION PRIVATE` — and the set built a false model of 325 "columns" on
one storey called ROOF. Both readers need the rule. The rule: a block
whose labels stack along X (their MinX within a token's width of each other, their Cy spread over more than
a few line heights) is rotated, and its lines are constant-X columns read along Y — the same reader with the
axes swapped, not a second reader. The `TextToken` carries no rotation; the geometry above is what you have.
State what you cannot tell from it (which way the text reads along the column: bottom-up here — FOUNDATION
at y 311 comes before PLAN at y 424 — and whether that is always so).

**5. A project name becomes the sheet title.** 01379-01 p77 (3456 × 2592):

    y 561.5  JOB TITLE            y 542  1200 STEWART
    y 377.2  DRAWING TITLE        y 353.6  GALLERIA PART PLANS
    y 292.3  DATE   DRAWN
    y 126.9  S212.9

The reader: SHEET TITLE = `1200 STEWART`, DRAWING TITLE = `GALLERIA PART PLANS`, REVISIONS = `STEWART PART
PLANS`. The standalone `TITLE` label (step 68) matched the second token of `JOB TITLE` — `JOB TITLE` is not
in `Labels`, so the pair did not match first — and became SHEET TITLE; `DRAWING TITLE` kept its own identity
and `SheetDxfName` took SHEET TITLE first. The DXF was `S212.9_1_1200 STEWART.dxf` (and `S402_1_1200
STEWART.dxf`), a project name where a plan title was. Two rules: a two-word label whose second word is a
label on its own is matched as the pair FIRST wherever it stands (`JOB TITLE`, `PROJECT TITLE`, `SHEET TITLE`,
`DRAWING TITLE`); and `DRAWING TITLE` IS the sheet title. And a third, in `SheetDxfName`: the name never
falls back to a project or job title — if no sheet title is read, the sheet is untitled (its number and page
name it, as today), never named for the project.

## What to write

- The rules above in `TitleBlockFields` (and `SheetDxfName` for 5), each with the drawing it came from in
  its comment, each stated as a rule about title blocks, not about a set.
- Tests in `TheAuditsCounterexamplesTests` or a new `ATitleBlocksFieldsAreItsOwnTests` in the same folder,
  built from the tokens above (positions as given; heights: the title tokens on 30926 and 30912 are
  12.5–13.8 pt, the labels smaller — state the heights you assume). One test per rule, each with the page it came from and WHAT THIS
  COVERS / WHAT IT DOES NOT in its summary; the five inputs above must read: `ROOF PLAN - BUILDING K`,
  `LEVEL -4 PLAN - CONCRETE OUTLINE`, `LEVEL 5 - LEVEL 14 PLAN`, `FOUNDATION PLAN`, `GALLERIA PART PLANS`.
- The existing two title-block tests stay green; if one of them contradicts a rule above, say so in the
  response and do not change it.

## Constraints

- No build, no tests, no drawings, no database, no network. Edit only `TitleBlockFields.cs`,
  `SheetTitleReader.cs` (the `TitleText` heuristic, for defect 4 only), `SheetDxfName.cs`, and the test
  file(s) named above. No other file.
- Every rule is universal or it is not written: if the evidence supports only "this set", say so instead.
- Write `docs/codex/CODEX-PDF-INTAKE-TITLE-BLOCK-FIELDS-RESPONSE.md`: what you changed, each rule in one
  sentence, what you could not decide from the evidence, and the row you would bank.
