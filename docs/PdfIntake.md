# PDF intake — what it does today, and what it leaves on the page

## 0. START HERE (state as of 2026-09-16 06:20, after steps 47 and 54–91 — 260 of 296 sets build from the PDF alone on run 25 (260 of 279 with a stick file of their own, the most yet); plates on 65% of storeys; every step of the overnight session is in a banked run)

A session picking this up cold reads this section, then the completion plan
(`docs/architecture/Kor.Operations.EngineeringTools.PdfIntake.plan.md` — what "complete" means, the
packages, and where each stands), then the last three step sections (§54–§56). §1–§52 are the
record of how each rule was arrived at, read when a rule is being changed — since 2026-09-15 they live in `docs/pdf-intake/` (three parts, listed at the foot of this page; the numbering is unchanged, a new section goes at the end of the last part). §102 is the latest step (run 24 banked — 254 of 296, plates 64%; step 91: a label's own colon is not its value, a right-aligned label's value lies to its left — 50026 and 30985 back); §101 the tendon shape's first column (pens); §100 a title that is a storey's name alone is a plan (31229); §99 a grid name is what the bubble says (31183; 86 → 9 sets; 31202 and 31168 re-banked); §98 a B-level is a level below grade (dxf.parkade-words P → P;B, migration 093, Ian applies); §97 words written up the page are read up the page (30888, 01589, 30941); §96 a letter-spaced label is a label, an empty title box is no title, the floor label is not a neighbour (30980); §95 a tendon family rule measured and rejected; §93 a title names a plan, a project name does not (30994; item 5's first class); §94 item 3(b) looked at); §92 storeys named as the set names them (LEVEL (L35) = L35, LEVEL -3 = P3; `corpus-query storeys`); §89–§91 the full suite's four reds closed as rules (on the edge is in; an open chain's closing gap is not a face; the nearest facing partner; a thickness cap measured and rejected); §88 words on one baseline by closeness (01379's second view); §87 a kept sheet titled as a plan the set issues, plus words, is a drawing ABOUT that plan (item 3's first half); §86 a line ending at an arrowhead is a tendon or a section cut, not a slab edge, the PlanarRings sweep, and the rejected force-label attempt; §85 the cells united — plates; §84 the callout that is not a heading; §83 steps 73–76 and runs 17–18; §82 the audit of 63–71 answered; §81 the static that leaked between sets; §80 run 14 looked at; §76 is why a corpus read is 46–53 min now and how a run is launched so it outlives the session.

**What this is.** A PDF ingestor: one ingestion point (`DrawingIntake.ReadSheet` → `PdfOnlyBuild`)
that reads a drawing set and hands its geometry to outlets — the ETABS `.e2k` today, the DXF as a
second outlet, Revit and SAFE to follow. "Finished" is a set from an office we have never seen
building a model with no code change. Nothing lives in a session, a scratch folder or a memory: a
rule is code with a banked test that states what it covers and what it does not; a drafting
convention is a KorStandards row with its compiled default; an instrument is a `takeoff` verb.

**Where the data is.** `%LOCALAPPDATA%\Temp\kor-drawings\stickfiles` mirrors the six PDFs the
rules were written on (31065, 31130, 31138, 31168, 31202, and `31170-01-arch` — the ARCHITECT's
set for 31170, the only one from another office); `...\kor-drawings\<hash>\` mirrors every
current stick file on the share (292 sets, 3.5 GB, mirrored once by hash); `...\corpus\<job>\`
is each set's build (DXF views, levels.csv, out.e2k, report.txt, yardstick.txt) and
`...\corpus\ledger-sets.csv` / `ledger-sheets.csv` the ledger of the last run, `ledger-sets.partial.csv` appended per set while it runs (banked copies under
`docs/etabs-handoff/corpus/`; the same rows in `analysis.IntakeSet`/`IntakeSheet` — 083 is applied, both of 2026-09-12's runs wrote them);
`...\yardsticks\<job>.e2k` the engineers' own models (92, exported on KOR-210). The six banked
baselines are IN THE REPO: `Kor.Operations.EngineeringTools.Core.Tests/Baselines/pdf-only-<job>.e2k`.
Nothing is read over SMB in the loop.

**The commands that measure every deliverable.**

    dotnet test Kor.Operations.EngineeringTools.Core.Tests --filter "FullyQualifiedName~SixSetsBuildAsBanked"
                                                        # THE SIX-SET GATE, ~8 min: byte-identical to the baselines, or what moved
                                                        # (TestResults/six-sets). Banking a step = replacing a baseline in the commit
    takeoff corpus-analyze [--recompose|--reuse] [--parallel N]
                                                        # THE CORPUS: all 292 sets into the ledger; --recompose re-runs ladder+composer
                                                        # on standing views (~20 min); a reader change rebuilds everything (~3 h, detached)
    takeoff corpus-query summary|no-model|plan-titles|set <job>|yardsticks
                                                        # the population in one table; every set without a model, by reason; how the
                                                        # plans name their storeys; one set's sheets; the yardsticks worst first
    dotnet test ... --filter "Speed!=Slow"              # the fast suite, ~35 s, every edit; the full suite before a commit of a rule

**Look at it, do not count it.** `takeoff model-render <e2k> <png>` draws every storey on one
sheet; `takeoff pdf-overlay <pdf> <page> <png> --scale N [--walls] [--columns]` paints what the
reader saw onto the sheet, `--mark x y --crop x y hw hh` cuts to a point; `takeoff model-to-page
<e2k> <sheet.dxf> <x> <y>` carries a model point back to its sheet and page through the shared
grid names; `takeoff vector-lines`, `vector-find`, `vector-words --band` read the PDF below every
reader (is the line there? what does the drawing call it? is the bubble drawn twice?);
`takeoff grid-names` puts a sheet's axis names beside the model's; `model-render --storey L2` draws ONE floor full size, before and after a rule; `dxf-strip` + `dxf-to-etabs` twice + `model-diff` is the strip-a-layer differential (three minutes that say whether a move is the composer's or a layer's); `corpus-query pages <job> --runs a,b` reads one set's pages run against run from the database. Step 27 was a day spent guessing
closing rules that a rendered view would have settled; that is the mistake this line exists to stop.

**Where the route stands, measured on the corpus** (run 25, 2026-09-16 05:24–06:18, steps through 91 — the overnight session's closing run):

| | |
|---|---|
| Sets that build a model from the PDF alone | **260 of 296** on run 25 — of the 279 that hold a stick file of their own (the most yet: 50026 and 30985 back on step 91, 30768, 90101, 30834 and 31229 new; no model lost against run 24) |
| No model | 17 the stick file of another job (a denominator, not a fault), 13 no plan sheet with structure on it (§100: six 1–2 page sets, four with no title text, 30743 by number only, 31036 and 30907 with no text layer, 90102 bookmark titles carrying the project's name), 6 no storeys (01742, 01783, 01788, 30836 OCR garbage, 30980 titles plotted as outlines, 30743) |
| Sheets placed on a storey | 2,162 of 4,059 plan views written (53%) on run 25 — the rest are refused non-structural sheets, sheets no storey name fits, and the 42 sets without a model |
| Storeys with a plate | **1,855 of 2,835 (65%)** on run 25 (41% on run 19) — the engineers' bar (plan WP6a item 2); the remaining 37%: no ring read 6%, rings read but no plate 12%, no sheet placed 19% |
| Against the engineers' own models (51 sets on run 25, `corpus-query summary`) | 58% of our columns within 100 mm of theirs (6,779 of 11,662), 52% of theirs within 100 mm of ours (6,742 of 12,881), pooled; the matched counts did not fall between runs 18 and 21 on any set — where the share fell, the judged population grew (§86) |
| The six banked sets | in the repo, byte-identical to their baselines at every commit (`SixSetsBuildAsBankedTests`, ~3 min with the cached page walk) |

**The work order is the engineers' definition of usable** (plan Rev 4, WP6a): the verticals and a plate on
every storey; one model per building. Plates first (63%); then footings ≠ columns (step 80 took the sheet-level half; the wood-plan half stated, §94), part plans never duplicate
the overall plan, the no-model residue, the DXF-route ratchets, storey names as the set names them, the
engineers' review. Each rule is universal, measured on the six (byte-identical or what moved) AND on the
corpus before it is kept; a rule the corpus refuses is written down with its cost.

**The reading backlog, parked until the plan's WP6** (plan §6; each was measured on 31168 and is
NOT one cause): the boundary walk (+13 storeys, stashed, held by one red gate and an 11% corner
notch); a ring that is a piece of the floor (31168 L2 takes 4,222 sq ft of 48,501); 15 views on
31168 that do not close, 14 never rendered; tower walls 33 vs 40 a storey; mezzanines placed;
31130's halves; C's floor on C-L4; A-L34 at 9,326 vs 5,949. ⛔ Measured and rejected, do not
retry: seeding the walk from the longest segment; feeding small loops to the bridging pass;
`RecoverAll` as the plate fallback; removing members outside the plate (KOR plates are fragments —
such rules count and list, §44).

**The working rule for all of them.** One universal rule per step, never a fix for one drawing;
banked as a test that states what it covers AND what it does not; measured on all six sets and on
the corpus before it is kept; the cost written down when a rule is rejected, so it is not tried
again; a red test is a finding, never a "known red".

---

Written 2026-09-08 from the code and from a content inventory of the five local stick files
(31065, 31130, 31138, 31168, 31202: 294 pages). Every number below was counted on the whole
population named; nothing is from a sample. The intake brief series lives in
`docs/codex/CODEX-INTAKE-CONVERGENCE-*.md` (29 so far; 23 was implemented by Codex and verified 2026-09-08 — the window's extractor is the CLI's `PdfPlanReader.Read` call, `TheWindowReadsWhatTheCliReadsTests`; 24 was Codex's audit, answered in §18) and this is the state they have reached.

The purpose of the intake is stated once so the rest can be judged against it: **pull everything a
drawing set carries that any downstream tool could need, once, through one reader, and account for
what was not pulled.** Where this document says "unread", that is a gap against that purpose, not
a note.

## Sections (the step-by-step record, split by period on 2026-09-15 — the numbering is unchanged; a new section is appended to the last part)

- [1. What it is](pdf-intake/part-1-s01-s30.md#1-what-it-is)
- [2. Where each tool stands](pdf-intake/part-1-s01-s30.md#2-where-each-tool-stands)
- [3. What the five sets carry, and what is read](pdf-intake/part-1-s01-s30.md#3-what-the-five-sets-carry-and-what-is-read)
- [4. Duplicates and bypasses in the code](pdf-intake/part-1-s01-s30.md#4-duplicates-and-bypasses-in-the-code)
- [5. How to audit it so the gaps stay measured](pdf-intake/part-1-s01-s30.md#5-how-to-audit-it-so-the-gaps-stay-measured)
- [6. Step 0, done 2026-09-08: the instruments, and the starting numbers](pdf-intake/part-1-s01-s30.md#6-step-0-done-2026-09-08-the-instruments-and-the-starting-numbers)
- [7. Step 1, done 2026-09-08: one record, one home, a reason for every path](pdf-intake/part-1-s01-s30.md#7-step-1-done-2026-09-08-one-record-one-home-a-reason-for-every-path)
- [8. Step 2a, done 2026-09-08: a path that draws nothing is not geometry](pdf-intake/part-1-s01-s30.md#8-step-2a-done-2026-09-08-a-path-that-draws-nothing-is-not-geometry)
- [9. Step 2b, done 2026-09-08: a wall is a filled rectangle of wall proportions](pdf-intake/part-1-s01-s30.md#9-step-2b-done-2026-09-08-a-wall-is-a-filled-rectangle-of-wall-proportions)
- [10. Step 3, done 2026-09-08: the build proves the compiled defaults and the banked rows agree](pdf-intake/part-1-s01-s30.md#10-step-3-done-2026-09-08-the-build-proves-the-compiled-defaults-and-the-banked-)
- [11. Steps 4 and 5, done 2026-09-08: a plan is the sheet that says PLAN, and a glyph drawn twice is one glyph](pdf-intake/part-1-s01-s30.md#11-steps-4-and-5-done-2026-09-08-a-plan-is-the-sheet-that-says-plan-and-a-glyph-)
- [12. Step 6, done 2026-09-08: the scale is accounted for on every sheet](pdf-intake/part-1-s01-s30.md#12-step-6-done-2026-09-08-the-scale-is-accounted-for-on-every-sheet)
- [13. Step 7, done 2026-09-08: a footing is a dashed rectangle whose size the schedule declares](pdf-intake/part-1-s01-s30.md#13-step-7-done-2026-09-08-a-footing-is-a-dashed-rectangle-whose-size-the-schedul)
- [14. Step 7b, done 2026-09-08: a footing outline is drawn only where nothing stands on it](pdf-intake/part-1-s01-s30.md#14-step-7b-done-2026-09-08-a-footing-outline-is-drawn-only-where-nothing-stands-)
- [15. Step 8, done 2026-09-08: a grid axis is a named line](pdf-intake/part-1-s01-s30.md#15-step-8-done-2026-09-08-a-grid-axis-is-a-named-line)
- [16. Step 10, done 2026-09-08: a storey height is the distance between two level lines](pdf-intake/part-1-s01-s30.md#16-step-10-done-2026-09-08-a-storey-height-is-the-distance-between-two-level-lin)
- [17. Step 11, done 2026-09-08: the drawings' storeys against the model's](pdf-intake/part-1-s01-s30.md#17-step-11-done-2026-09-08-the-drawings-storeys-against-the-models)
- [18. The audit, 2026-09-08, and what it changed](pdf-intake/part-1-s01-s30.md#18-the-audit-2026-09-08-and-what-it-changed)
- [19. Step 12, done 2026-09-08: a dimension string is a length the drafter wrote](pdf-intake/part-1-s01-s30.md#19-step-12-done-2026-09-08-a-dimension-string-is-a-length-the-drafter-wrote)
- [20. Step 13, done 2026-09-08: a view's scale is the caption under it](pdf-intake/part-1-s01-s30.md#20-step-13-done-2026-09-08-a-views-scale-is-the-caption-under-it)
- [21. Step 14, done 2026-09-08: a wall is what its clip lets through, and its doorways are its piers](pdf-intake/part-1-s01-s30.md#21-step-14-done-2026-09-08-a-wall-is-what-its-clip-lets-through-and-its-doorways)
- [22. Step 14b, done 2026-09-08: the north arrow is furniture](pdf-intake/part-1-s01-s30.md#22-step-14b-done-2026-09-08-the-north-arrow-is-furniture)
- [23. Step 15, done 2026-09-08: a sheet from the stick file is a drawing like any other](pdf-intake/part-1-s01-s30.md#23-step-15-done-2026-09-08-a-sheet-from-the-stick-file-is-a-drawing-like-any-oth)
- [24. Step 16, done 2026-09-08: Reissue Impact, first cut — what changed between two issues](pdf-intake/part-1-s01-s30.md#24-step-16-done-2026-09-08-reissue-impact-first-cut--what-changed-between-two-is)
- [25. Step 17, done 2026-09-08: a mark-up is a list of instructions](pdf-intake/part-1-s01-s30.md#25-step-17-done-2026-09-08-a-mark-up-is-a-list-of-instructions)
- [26. Step 18, done 2026-09-09: the drafter's reply is beside the thing it answers](pdf-intake/part-1-s01-s30.md#26-step-18-done-2026-09-09-the-drafters-reply-is-beside-the-thing-it-answers)
- [27. Step 19, done 2026-09-09: the set checks itself](pdf-intake/part-1-s01-s30.md#27-step-19-done-2026-09-09-the-set-checks-itself)
- [28. Step 20, done 2026-09-09: a wall is what the cut pen encloses](pdf-intake/part-1-s01-s30.md#28-step-20-done-2026-09-09-a-wall-is-what-the-cut-pen-encloses)
- [29. Step 21, done 2026-09-09: a wall's faces may converge, and a band thicker than any wall is not a slab](pdf-intake/part-1-s01-s30.md#29-step-21-done-2026-09-09-a-walls-faces-may-converge-and-a-band-thicker-than-an)
- [30. The PDF alone, measured 2026-09-09: what Model Start gives with no Revit and no reference](pdf-intake/part-1-s01-s30.md#30-the-pdf-alone-measured-2026-09-09-what-model-start-gives-with-no-revit-and-no)
- [31. Step 22, done 2026-09-09: the PDF alone gives the parkade its plate](pdf-intake/part-2-s31-s60.md#31-step-22-done-2026-09-09-the-pdf-alone-gives-the-parkade-its-plate)
- [32. Step 23, done 2026-09-09: what the audit of steps 20 to 22 changed](pdf-intake/part-2-s31-s60.md#32-step-23-done-2026-09-09-what-the-audit-of-steps-20-to-22-changed)
- [33. Step 24, done 2026-09-09: a floor's edge is the outermost closed loop the plan draws](pdf-intake/part-2-s31-s60.md#33-step-24-done-2026-09-09-a-floors-edge-is-the-outermost-closed-loop-the-plan-d)
- [34. Step 25, done 2026-09-09: an elevation's ladder is every column of it, and a break is not a storey](pdf-intake/part-2-s31-s60.md#34-step-25-done-2026-09-09-an-elevations-ladder-is-every-column-of-it-and-a-brea)
- [35. Step 26, done 2026-09-09: a sheet is its views](pdf-intake/part-2-s31-s60.md#35-step-26-done-2026-09-09-a-sheet-is-its-views)
- [36. Step 27, 2026-09-09: an edge interrupted is still one edge — and what a flood fill answers instead](pdf-intake/part-2-s31-s60.md#36-step-27-2026-09-09-an-edge-interrupted-is-still-one-edge--and-what-a-flood-fi)
- [37. Step 28, done 2026-09-10: a wall standing on the slab edge does not remove the slab edge](pdf-intake/part-2-s31-s60.md#37-step-28-done-2026-09-10-a-wall-standing-on-the-slab-edge-does-not-remove-the-)
- [38. Step 29, ATTEMPTED 2026-09-10 and NOT LANDED: a floor's edge as the outer boundary](pdf-intake/part-2-s31-s60.md#38-step-29-attempted-2026-09-10-and-not-landed-a-floors-edge-as-the-outer-bounda)
- [39. Step 30, done 2026-09-10: the words a drawing names a level with are a vocabulary — and the first set from](pdf-intake/part-2-s31-s60.md#39-step-30-done-2026-09-10-the-words-a-drawing-names-a-level-with-are-a-vocabula)
- [40. Step 31, done 2026-09-10: every sheet is read at the scale it states](pdf-intake/part-2-s31-s60.md#40-step-31-done-2026-09-10-every-sheet-is-read-at-the-scale-it-states)
- [41. Step 32, done 2026-09-10: an assembly schedule is a legend of cards, and a card is read whole](pdf-intake/part-2-s31-s60.md#41-step-32-done-2026-09-10-an-assembly-schedule-is-a-legend-of-cards-and-a-card-)
- [42. Step 33, done 2026-09-10: a wall is what its tag says it is](pdf-intake/part-2-s31-s60.md#42-step-33-done-2026-09-10-a-wall-is-what-its-tag-says-it-is)
- [43. Step 34, done 2026-09-10: a sheet that says what a wall is wins](pdf-intake/part-2-s31-s60.md#43-step-34-done-2026-09-10-a-sheet-that-says-what-a-wall-is-wins)
- [44. Step 35, done 2026-09-10: a dimension string is not a wall — and across sheets, a ring inside the floor is](pdf-intake/part-2-s31-s60.md#44-step-35-done-2026-09-10-a-dimension-string-is-not-a-wall--and-across-sheets-a)
- [45. Step 36, done 2026-09-10: a roof plan draws the storey above the highest storey the set's numbered plans d](pdf-intake/part-2-s31-s60.md#45-step-36-done-2026-09-10-a-roof-plan-draws-the-storey-above-the-highest-storey)
- [46. Step 37, done 2026-09-10: a pattern's cells abut, three and more of a size; a column stands alone](pdf-intake/part-2-s31-s60.md#46-step-37-done-2026-09-10-a-patterns-cells-abut-three-and-more-of-a-size-a-colu)
- [47. Step 38, done 2026-09-10: a wall is what a fill pattern fills](pdf-intake/part-2-s31-s60.md#47-step-38-done-2026-09-10-a-wall-is-what-a-fill-pattern-fills)
- [48. Step 39, done 2026-09-10: a column stands at the centre of its outline](pdf-intake/part-2-s31-s60.md#48-step-39-done-2026-09-10-a-column-stands-at-the-centre-of-its-outline)
- [49. Step 40, done 2026-09-10: the drawings' own grid is written to the model](pdf-intake/part-2-s31-s60.md#49-step-40-done-2026-09-10-the-drawings-own-grid-is-written-to-the-model)
- [50. Step 41, done 2026-09-10: a level may be named by a word alone](pdf-intake/part-2-s31-s60.md#50-step-41-done-2026-09-10-a-level-may-be-named-by-a-word-alone)
- [51. Step 42, done 2026-09-11: the adversarial audit of steps 31–41, answered](pdf-intake/part-2-s31-s60.md#51-step-42-done-2026-09-11-the-adversarial-audit-of-steps-3141-answered)
- [52. Step 43, done 2026-09-11: a bubble labelled twice with one name is labelled once](pdf-intake/part-2-s31-s60.md#52-step-43-done-2026-09-11-a-bubble-labelled-twice-with-one-name-is-labelled-onc)
- [53. Step 44, 2026-09-11: the whole corpus through the one ingestion point — the first run](pdf-intake/part-2-s31-s60.md#53-step-44-2026-09-11-the-whole-corpus-through-the-one-ingestion-point--the-firs)
- [54. Steps 45 and 46, 2026-09-11: a set's storeys are what its plans name; the title on the page names the shee](pdf-intake/part-2-s31-s60.md#54-steps-45-and-46-2026-09-11-a-sets-storeys-are-what-its-plans-name-the-title-o)
- [55. 2026-09-12: step 46 measured on the corpus; the completion plan's WP1–WP5 closed; a layer of ours with the](pdf-intake/part-2-s31-s60.md#55-2026-09-12-step-46-measured-on-the-corpus-the-completion-plans-wp1wp5-closed-)
- [56. 2026-09-12: the engineers' own models of four harness sets, and what they showed — an inch, one joint per ](pdf-intake/part-2-s31-s60.md#56-2026-09-12-the-engineers-own-models-of-four-harness-sets-and-what-they-showed)
- [57. Step 49, 2026-09-12: a centroid lies inside its own box; a target's quadrants are not columns; a member st](pdf-intake/part-2-s31-s60.md#57-step-49-2026-09-12-a-centroid-lies-inside-its-own-box-a-targets-quadrants-are)
- [58. Step 50, 2026-09-12: a sheet that says what it is, is that; and the yardstick judges only where the engine](pdf-intake/part-2-s31-s60.md#58-step-50-2026-09-12-a-sheet-that-says-what-it-is-is-that-and-the-yardstick-jud)
- [59. Step 51, 2026-09-12: a refused sheet's axes still place the sheets that name them — and 31130 is one build](pdf-intake/part-2-s31-s60.md#59-step-51-2026-09-12-a-refused-sheets-axes-still-place-the-sheets-that-name-the)
- [60. 2026-09-12, later: what the residue on 31130 and 31138 is — hers, by the drawings — and the yardstick says](pdf-intake/part-2-s31-s60.md#60-2026-09-12-later-what-the-residue-on-31130-and-31138-is--hers-by-the-drawings)
- [61. Step 53, 2026-09-12: `pdf-at`; the grid is drawn with one pen; a run may stop just past its anchor — and a](pdf-intake/part-3-s61-onward.md#61-step-53-2026-09-12-pdf-at-the-grid-is-drawn-with-one-pen-a-run-may-stop-just-)
- [62. Step 55, 2026-09-12, late: a sheet that names no axis stands where its members stand](pdf-intake/part-3-s61-onward.md#62-step-55-2026-09-12-late-a-sheet-that-names-no-axis-stands-where-its-members-s)
- [63. Step 56, 2026-09-13: the same drawings shifted on the page build the same structure — a place is decided b](pdf-intake/part-3-s61-onward.md#63-step-56-2026-09-13-the-same-drawings-shifted-on-the-page-build-the-same-struc)
- [64. Step 54, 2026-09-13: a sheet's frame is its page's](pdf-intake/part-3-s61-onward.md#64-step-54-2026-09-13-a-sheets-frame-is-its-pages)
- [65. Step 57, 2026-09-13: the adversarial audit of steps 44–56, answered](pdf-intake/part-3-s61-onward.md#65-step-57-2026-09-13-the-adversarial-audit-of-steps-4456-answered)
- [66. Step 47, 2026-09-13: a storey may be named by a word](pdf-intake/part-3-s61-onward.md#66-step-47-2026-09-13-a-storey-may-be-named-by-a-word)
- [67. Step 58, 2026-09-13: a column is drawn with four corners](pdf-intake/part-3-s61-onward.md#67-step-58-2026-09-13-a-column-is-drawn-with-four-corners)
- [68. Step 59, 2026-09-13 night: what moved between two runs is classed by the first thing that changed — and th](pdf-intake/part-3-s61-onward.md#68-step-59-2026-09-13-night-what-moved-between-two-runs-is-classed-by-the-first-)
- [69. Step 60, 2026-09-13 night: a stick file that is another job's; a title may run two lines; the set's own or](pdf-intake/part-3-s61-onward.md#69-step-60-2026-09-13-night-a-stick-file-that-is-another-jobs-a-title-may-run-tw)
- [70. Step 61, 2026-09-14 early: the audit's queue closed — and what closing it found](pdf-intake/part-3-s61-onward.md#70-step-61-2026-09-14-early-the-audits-queue-closed--and-what-closing-it-found)
- [71. Step 62, 2026-09-14: the second audit's brief A (the instruments), answered](pdf-intake/part-3-s61-onward.md#71-step-62-2026-09-14-the-second-audits-brief-a-the-instruments-answered)
- [72. Step 63, 2026-09-14: a wall is six inches or more — and brief B answered](pdf-intake/part-3-s61-onward.md#72-step-63-2026-09-14-a-wall-is-six-inches-or-more--and-brief-b-answered)
- [73. Step 64, 2026-09-14 afternoon: what the yardstick's 58% is made of — her model's age](pdf-intake/part-3-s61-onward.md#73-step-64-2026-09-14-afternoon-what-the-yardsticks-58-is-made-of--her-models-ag)
- [74. Step 65, 2026-09-14 evening: run 11, and the reference plan is the plan the others can be set on](pdf-intake/part-3-s61-onward.md#74-step-65-2026-09-14-evening-run-11-and-the-reference-plan-is-the-plan-the-othe)
- [75. Step 66, 2026-09-14 evening: one plan naming no storey is a one-storey building](pdf-intake/part-3-s61-onward.md#75-step-66-2026-09-14-evening-one-plan-naming-no-storey-is-a-one-storey-building)
- [76. 2026-09-14 night: the read's cost — the page record, the profile, and a run that dies keeps its ledger](pdf-intake/part-3-s61-onward.md#76-2026-09-14-night-the-reads-cost--the-page-record-the-profile-and-a-run-that-d)
- [77. Step 67, 2026-09-14 night: a wall the drafter did not fill is a wall only at a retaining wall's thickness ](pdf-intake/part-3-s61-onward.md#77-step-67-2026-09-14-night-a-wall-the-drafter-did-not-fill-is-a-wall-only-at-a-)
- [78. Step 68, 2026-09-14 night: the small jobs' title blocks — and every composed column says what made it](pdf-intake/part-3-s61-onward.md#78-step-68-2026-09-14-night-the-small-jobs-title-blocks--and-every-composed-colu)
- [79. Step 69, 2026-09-14 night: numbered buildings — BUILDING 1, BLDG 1A, 12-LEVEL 3 are building tags](pdf-intake/part-3-s61-onward.md#79-step-69-2026-09-14-night-numbered-buildings--building-1-bldg-1a-12-level-3-ar)
- [80. Run 14 and step 70, 2026-09-14 night: the full read after steps 67–69, its eleven storey movers looked at ](pdf-intake/part-3-s61-onward.md#80-run-14-and-step-70-2026-09-14-night-the-full-read-after-steps-6769-its-eleven)
- [81. Run 15 and step 71, 2026-09-15 early: a set no rule touched moved — the vocabulary was another set's](pdf-intake/part-3-s61-onward.md#81-run-15-and-step-71-2026-09-15-early-a-set-no-rule-touched-moved--the-vocabula)
- [82. Step 72, 2026-09-15 morning: the audit of steps 63–71 answered — eleven findings, eight fixed, one measure](pdf-intake/part-3-s61-onward.md#82-step-72-2026-09-15-morning-the-audit-of-steps-6371-answered--eleven-findings-)
- [83. Steps 73–76 and runs 17, 18, 2026-09-15 midday: the title block's fields, the plate instrument, a dead bra](pdf-intake/part-3-s61-onward.md#83-steps-7376-and-runs-17-18-2026-09-15-midday-the-title-blocks-fields-the-plate)
- [84. Step 77, 2026-09-15 afternoon: a line that names another sheet is a callout, not a heading](pdf-intake/part-3-s61-onward.md#84-step-77-2026-09-15-afternoon-a-line-that-names-another-sheet-is-a-callout-not)
- [86. Step 79, 2026-09-15 evening: a line ending at an arrowhead is a tendon or a section cut, not a slab edge — and the PlanarRings sweep](pdf-intake/part-3-s61-onward.md#86-step-79-2026-09-15-evening-a-line-ending-at-an-arrowhead-is-a-tendon-or-a-section-cut-not-a-slab)
- [87. Step 80, 2026-09-15 late evening: a kept sheet titled as a plan the set issues, plus words, is a drawing ABOUT that plan — WP6a item 3, the 30990 class](pdf-intake/part-3-s61-onward.md#87-step-80-2026-09-15-late-evening-a-kept-sheet-titled-as-a-plan-the-set-issues-plus-words-is-a-drawing-about-that-plan--wp6a-item-3-the-30990-class)
- [88. Step 81, 2026-09-15 night: words are on one baseline when their baselines are close, not when they round to the same point — WP6a item 4's cause on 01379](pdf-intake/part-3-s61-onward.md#88-step-81-2026-09-15-night-words-are-on-one-baseline-when-their-baselines-are-close-not-when-they-round-to-the-same-point--wp6a-item-4s-cause-on-01379)
- [89. Step 82, 2026-09-15 night: on the edge is in — the full suite's fourth red, a plate that came and went with the origin](pdf-intake/part-3-s61-onward.md#89-step-82-2026-09-15-night-on-the-edge-is-in--the-full-suites-fourth-red-a-plate-that-came-and-went-with-the-origin)
- [90. Step 83, 2026-09-16 small hours: the edge that closes an open chain is not drawn, so it is not a face — the first of the three DXF-route reds](pdf-intake/part-3-s61-onward.md#90-step-83-2026-09-16-small-hours-the-edge-that-closes-an-open-chain-is-not-drawn-so-it-is-not-a-face--the-first-of-the-three-dxf-route-reds)
- [91. Step 83 (continued): a face's partner is the nearest face that faces it, wherever it lies — the second DXF-route red; and a thickness cap measured and rejected](pdf-intake/part-3-s61-onward.md#91-step-83-continued-2026-09-16-small-hours-a-faces-partner-is-the-nearest-face-that-faces-it-wherever-it-lies--the-second-dxf-route-red-and-a-thickness-cap-measured-and-rejected)
- [92. Step 84, 2026-09-16 small hours: a storey is named as the set names it — WP6a item 7, with its instrument](pdf-intake/part-3-s61-onward.md#92-step-84-2026-09-16-small-hours-a-storey-is-named-as-the-set-names-it--wp6a-item-7-with-its-instrument)
- [93. Step 85, 2026-09-16 small hours: a title names a plan; a project name does not — WP6a item 5's first class, and the residue named](pdf-intake/part-3-s61-onward.md#93-step-85-2026-09-16-small-hours-a-title-names-a-plan-a-project-name-does-not--wp6a-item-5s-first-class-and-the-residue-named)
- [94. WP6a item 3(b) looked at: 31162's "footings" are on its wood framing plans — a rule stated, not shipped](pdf-intake/part-3-s61-onward.md#94-wp6a-item-3b-looked-at-2026-09-16-small-hours-31162s-footings-are-on-its-wood-framing-plans--a-rule-stated-not-shipped)
- [95. Measured and rejected: a family of parallel lines as tendons — a third attempt on the shape, made against rule 10](pdf-intake/part-3-s61-onward.md#95-measured-and-rejected-2026-09-16-0152-a-family-of-long-parallel-lines-at-one-spacing-as-tendons-and-a-third-attempt-on-the-shape)
- [96. Step 86: a letter-spaced label is a label, an empty title box is no title — 30980 (titles plotted as outlines)](pdf-intake/part-3-s61-onward.md#96-step-86-2026-09-16-0300-a-letter-spaced-label-is-a-label-and-an-empty-title-box-is-no-title-wp6a-item-5-30980s-class)
- [97. Step 87: words written up the page are read up the page — 30888, 01589, 30941; §80's fixture was not its page](pdf-intake/part-3-s61-onward.md#97-step-87-2026-09-16-0335-words-written-up-the-page-are-read-up-the-page-30888-01589-and-30941-wp6a-item-5-the-second-instance-so-the-class)
- [98. Step 88: a B-level is a level below grade — dxf.parkade-words P → P;B, migration 093 (Ian applies)](pdf-intake/part-3-s61-onward.md#98-step-88-2026-09-16-0345-a-b-level-is-a-level-below-grade-as-a-p-level-is-the-row-dxfparkade-words-p-pb-wp6a-item-7-migration-093)
- [99. Step 89: a grid name is what the bubble says — 31183; 86 → 9 sets with refused grid text; 31202/31168 re-banked](pdf-intake/part-3-s61-onward.md#99-step-89-2026-09-16-0415-a-grid-name-is-what-the-bubble-says-31183s-zone-plans-and-86-of-294-sets-grid-text-wp6a-item-4b)
- [100. Step 90: a title that is a storey's name alone is that storey's plan — 31229; item 5's class B looked at (17 sets classed)](pdf-intake/part-3-s61-onward.md#100-step-90-2026-09-16-0430-a-title-that-is-a-storeys-name-and-nothing-else-is-that-storeys-plan--wp6a-item-5s-class-b-looked-at)
- [101. Measurement only: the tendon shape's first column — vector-lines --pens; 31130's sheet is CONCRETE OUTLINE & POST TENSION](pdf-intake/part-3-s61-onward.md#101-measurement-only-2026-09-16-0445-the-tendon-shapes-first-column-the-pens-and-what-31130s-sheet-is-called)
- [102. Run 24 banked + step 91: a label's own colon is not its value; a right-aligned label's value lies to its left — 50026, 30985 back](pdf-intake/part-3-s61-onward.md#102-run-24-banked-and-step-91-2026-09-16-0525-a-labels-own-colon-is-not-its-value-a-right-aligned-labels-value-lies-to-its-left-two-models-lost-on-run-24-found-by-the-diff-both-back)
- [85. Step 78, 2026-09-15 afternoon: a floor is the cells its structure stands in, united — PlanarRings wired; a](pdf-intake/part-3-s61-onward.md#85-step-78-2026-09-15-afternoon-a-floor-is-the-cells-its-structure-stands-in-uni)
