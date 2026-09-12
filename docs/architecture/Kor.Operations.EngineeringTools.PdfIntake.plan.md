# PDF intake → ETABS — Completion Plan

**Status:** Rev 3, 2026-09-12 (step 49 added to §8, 3b) — WP1–WP5 landed overnight on Ian's go-ahead ("go through this all,
step by step, and finish it overnight"): commits `8fccbc25` (WP3), `3f3f82b9` (WP2), `aba7d9ff`
(WP4), `69e554b5` (WP5), each gated by the six byte-identical and the fast suite; WP5's remaining
conventions are counted by a test, not by this document. §8 is what needs Ian. Rev 2 (2026-09-11)
turned the plan on Ian's direction: *"rather than build the brain, then expose it to one drawing at
a time … build an analyzer to get all the info you need at once"* (§1a, §1b).
**Author:** Claude, for Ian Lalonde. Written after Ian's instruction the same day: *"You build
ephemeral bloated solutions and they're not built as code or in DB. This must stop."*
**Audience:** Ian. Approve it, strike what you disagree with, and each package then runs one per
sitting, each ending in a commit and a single printed verdict.

---

## 1. What "complete" means — the contract, unchanged since 2026-09-10

A structural drawing set arrives as a PDF and becomes an ETABS `.e2k` with no reference model, no
Revit, and **no code change for a set from an office we have never seen**. Every sheet the tool
cannot read says why, in the report, in words a person acts on. One ingestion point
(`DrawingIntake.ReadSheet`) feeds every outlet: the model, the inventory, the schedules, the
reissue diff, the self-check.

That is a contract, not a floor count. It is met when:

1. **The corpus builds.** Every structural stick file the office holds (292 jobs' current issues,
   §1a) runs through the one ingestion point, and the ledger says, set by set and sheet by sheet,
   what was read, what was placed, and why not — with the totals rising, never a set regressing.
   The sets from other offices are already in it (460 jobs hold an architect's set), so there is
   no "seventh set" to ask for: the held-out sets are the ones the rules were never written for.
2. **Andrea accepts one model** built this way as a starting point she would use. (The
   31170-from-PDF deliverable exists; it has not been put in front of her.)
3. **Every instrument that measures the tool is code in the repo** — a `takeoff` verb, a test,
   or a `tools/` console — and every drafting convention the readers apply is a row in
   KorStandards with its compiled default proven equal by a test. Nothing the loop depends on
   lives in `%TEMP%`, in `docs/*.py`, or in a memory file.

## 1a. The corpus, counted (2026-09-11, `takeoff corpus-census`, 90 s over the share, 0 folders unlisted)

| | |
|---|---|
| Job folders on `\\Kor-fs01\Projects\Projects` | **1,158** in 9 categories |
| Jobs with a structural stick file (`05 Stickfile\*Stickfile*.pdf`) | **292** — 382 issues; the newest per job is the current set: **3.5 GB** to mirror (4.6 GB for every issue). 109 current sets dated 2026, 60 dated 2025 |
| Jobs with an architect's set (`05 Stickfile\01 Architectural`) | **460** — 6,829 PDFs (single sheets and whole sets mixed; the analyzer sorts them by page count) |
| Jobs with an ETABS model where the convention files it | **104** — 47 `.e2k`, 746 `.EDB` (the 2026-08 server-side walk found 1,126 `.e2k` on the volume: models are filed in more places than the convention) |
| **Jobs with both a stick file and a model — a yardstick each** | **66** (56 residential, 6 industrial-garage): 9 with an `.e2k` now, 57 `.EDB` only, which ETABS must export — a batch on a machine with ETABS, Ian's call |
| Pitfalls the census found | the naming template `31###-01 YYYY-MM-DD … Stickfile.pdf` copied into 437 job folders (excluded); one job's stick file copied into 16 others (flagged); three spellings of the dated name, all read |

Today's harness is 6 of those 292. The one-job yardstick (31168) is 1 of 66.

## 1b. The corpus, built (`takeoff corpus-analyze`; run 1 2026-09-11 = WP1's first pass, PdfIntake.md §53; run 3 2026-09-12 = after steps 45 and 46, §55)

| | run 1 (step 44) | run 3 (step 46) |
|---|---|---|
| Sets built from the PDF alone | **39 of 292**; 8,692 pages read, 3,989 plans, 0 failed | **207 of 292** (197 after step 46; the `-MARKUP` layer rule freed 10 more, run 4) |
| No model because no storey ladder was read | **225 of 292** — they all have plans; the ladder reader wanted shear-wall elevations | **67** — their plans name storeys with WORDS (step 47) |
| No model: no plan the reader typed / refused at the layer gate | 17 / 11 | 17 / 1 (10 of the 11 were our own `-MARKUP` layer, §55) |
| Plan views set on the grid by name | 873 of 2,058 (42%) | 1,948 of 4,323 (45%) |
| Storeys with a plate | 230 of 1,098 (21%) | 741 of 2,401 (31%) |
| Yardsticks (engineers' own models, 92 exported from KOR-210) | 18 of the 39: **45% / 52%** within 100 mm | 62 sets have one, 42 share a storey with columns: **34% / 48%**; per set from 75–99% (4 sets) to 0–24% (20 sets) — more sets, more assumed storeys, a lower share |

**The work order is a count now.** 1. Storeys: the 67 sets whose plans name their storeys with
words, and 935 views the composer can put on no storey by name (step 47, §8 item 5). 2. Views on
the grid (55% are not placed by name). 3. Plates (69% of storeys have none). Each measured on 292
before it is kept.

## 2. Where it stands, measured (2026-09-11, commit `3da6f85c`)

| | |
|---|---|
| Sets that build from the PDF alone | 6 of 6 (five KOR, one architect's — Vectorworks) |
| 31168 against the Revit route | columns median 16 mm, **92% within 50 mm** (2,211 columns, 58 storeys); tower plates within 0.1%; **36 of 62 storeys carry a plate**; walls 1,324 vs 1,832 |
| Steps landed | 43, each one universal rule with a banked test stating what it covers and does not |
| Core tests | 1,315, all green (full suite 8 min; fast 25 s) |
| Codex adversarial audit of steps 31–41 | 25 findings; 23 fixed as tests, 2 stated as limits |

## 3. The debt that makes it "ephemeral" — counted

| What | Count | Where it should be |
|---|---|---|
| Loose scripts the loop depends on | **40 python + 5 bash** in `docs/etabs-handoff/` (26 touched this week). **2026-09-11: none (WP2)** | `takeoff` verbs, tests, or deleted with the finding recorded |
| Banked baselines and the yardstick | 80 `.e2k` (1.8 MB for the current six) + 616 MB of renders/DXFs in **`%LOCALAPPDATA%\Temp`**, banked by a hand-typed `cp` until this morning | the current six in the repo beside the tests; a bank is a reviewable commit diff |
| Drafting conventions compiled as constants | **106** `const` values in the readers (reach, taper share, pattern count, label reach…) against **49** rules in KorStandards. **2026-09-11: 169 declarators triaged by a test — 13 rows, 44 conventions still compiled, 85 tolerances, 22 rules, 5 another product's, 3 dead deleted** | rows, read through `PdfIntakeOptions.For(conn)`, parity gated by `CompiledDefaultsAreTheBankedRowsTests`; the triage gated by `EveryReaderConstantIsTriagedTests` |
| `Program.cs` | **5,153 lines, 56 verbs** in one file. **2026-09-11: 345 lines, 70 verb files (WP3)** | one file per verb, a registry, the help-list test |
| Transport between the two halves | scratch DXF written to disk and re-read (`pdf-takeoff` → `dxf-to-etabs`). **2026-09-11: in memory (`DxfSheet`), the DXF an outlet (WP4)** | in-memory `PlanGeometrySet`; the DXF stays as an outlet |
| State carried in prose | `docs/PdfIntake.md` 2,555 lines; the seed doc; memory files; "known red" carried a day | one START page; the log frozen as the record; a red is a finding, never a known |

## 4. The packages, in order — each one sitting, one commit, one verdict

Nothing new is read from a drawing until WP1–WP5 land, except from a red test. The reading
backlog (§6) waits; it is where the last two weeks went and it is not what makes this a tool.

### WP1 — The analyzer: the whole corpus through the one ingestion point, into a ledger
- **Mirror** (done once, then by hash): the 292 current issues (3.5 GB) to the local drawing
  cache the tests already use; the architects' PDFs with more than a few pages. Read-only on
  the share; nothing read over SMB in the loop after that.
- **`takeoff corpus-analyze`**, on `StickFileCorpus` (the census, landed today): every sheet of
  every set through `DrawingIntake.ReadSheet`, then the composer, in parallel; one row per sheet
  and one per set into **`analysis.IntakeLedger`** in KorStandards (migration for Ian) and a
  CSV beside it: set, sheet, type, title, storey, stated scale, placed on the grid or why not,
  columns/walls/plates per storey, the yardstick residual where the job has a model. Incremental:
  a set is re-read only when its file hash or the tool's version changes.
- **The population's vocabulary** out of the same pass, as rows: every level name, sheet-title
  word, scale note, wall-tag code and layer word across all 292 sets, with counts — the part
  that is learned from the corpus into the DB rather than typed from one drawing.
- **The regression bank inside it**: the six sets' baselines move into the repo beside the
  tests and stay byte-identical gates (`SixSetsBuildAsBankedTests`); the other 286 are a ledger
  that only ratchets. Banking = a commit that changes a baseline file, reviewable.
- **The order of work falls out of the ledger** — "31 of 292 sets have a sheet that cannot be
  set on the grid, 19 of them for one reason" — and every rule is measured on all 292 before it
  is kept.
- Deletes: `pdf_only_all.sh`, `pdf_only_one.sh`, `six_set_diff.sh`, `six_set_bank.sh`,
  `render_storeys.sh`, `members_diff.py`, `plate_diff.py`, `storey_counts.py`, `plan_sheet.py`
  (ported as one C# implementation shared by the test, the analyzer and a `takeoff model-diff` verb).
- Gate: the six baselines byte-identical on `3da6f85c`; the ledger holds 292 sets with a row
  for every sheet; the old scripts and the new code give the same diff on one deliberately
  changed model; a second run reads nothing that did not change.

### WP2 — Instruments are verbs — DONE 2026-09-11
- The instruments used more than once are `takeoff` verbs on the code the readers use:
  `vector-lines` (was `pdf_lines.py`), `vector-find` (`pdf_words_near.py`), `vector-words --band`
  (`pdf_words_in_band.py`), `grid-names`, `model-to-page`, `pdf-overlay --mark/--crop` (`crop_mm.py`),
  `corpus-query` (`plan_titles.py`, `set_sheets.py`), and from WP1 `model-diff`, `model-render`,
  `model-yardstick`. One e2k grid reader (`E2kDocument.ReadGrids`) serves the differential, the
  yardstick and the instruments — it was two private copies. The scratch DXF banks its page origin
  (`$INSBASE`) so a model point comes back to the page without a second alignment; verified by
  marking a P2 column of 31168 on its own sheet and looking.
- The one-offs (`chains`, `view_breaks`, `view_parts`, `dxf_layer_entities`, `pdf_tiles`,
  `ledger_diff`, `pick_plans`, `mpa`, `tags`, `annots`, `renderpage`, `order`, `idbprose`,
  `read_questions`, `transcribe`) are deleted; each finding stands in `PdfIntake.md`, annotated at
  its first mention; `docs/etabs-handoff/README.md` maps every old name to its verb.
- Gate, met: `docs/etabs-handoff/` holds no scripts; the help-list test; the six byte-identical
  (the exporter's header changed, the models did not); `TheInstrumentsShareTheReadersFramesTests`.

### WP3 — `Program.cs` one file per verb — DONE 2026-09-11
- `Verbs/<Verb>.cs` (70 files, one class per verb: `Matches(args)` and `Run(args)`, the body moved
  verbatim by `tools/split_program_cs.py`), `TakeoffVerbs.All` the registry in the order Program.cs
  always tried them, `GlobalUsings.cs`; `Program.cs` is the help check, the dispatch loop, the
  rebar-CSV default and the help catalogue (345 lines, from 5,153).
- Gate, met: the help-list test reads the registry (not the source); the six byte-identical
  (`SixSetsBuildAsBankedTests`, 8 m 33 s); the fast suite 1,218 green.

### WP4 — In-memory handoff — DONE 2026-09-11 (first form)
- Gate first: `TheHandoffIsInMemoryAndTheModelIsTheSameTests` — the composer given views in memory
  and a folder that does not exist builds the same model as the folder route (fast, synthetic), and
  the six built both ways are byte-identical (Slow). Then the change: `DxfExporter.ExportLines`
  holds a view as its lines, `SheetsResult.Views` carries them as `DxfSheet`s, and
  `DxfToEtabsRequest.Sheets` / `LevelLines` hand them to the composer, whose every read goes
  through one `LinesOf`. `PdfOnlyBuild.Build` composes from memory (`Handoff.Memory`, the default);
  the DXF files are still written as the outlet the corpus, `--recompose` and the instruments read.
- What this is and is not: the DXF TEXT is the interchange, held in memory — every reader in the
  composer takes lines and the office's own exports arrive as files of them. The typed
  `PlanGeometrySet` behind it (no serialisation at all) is the next refinement; the rounding a
  typed handoff would have to reproduce to stay byte-identical is the DXF's 0.0001 mm.
- Gate, met: both tests green; the six through memory and through the disk, 21 m 42 s for the eighteen builds with the corpus rebuild running beside them; the per-route seconds print from the test's next run.

### WP5 — Conventions are rows — TRIAGED 2026-09-11, tier one wired
- The triage is a gate, not a table in prose: `EveryReaderConstantIsTriagedTests` scans every
  `const double|int` in the readers (the same scan as `tools/list_reader_constants.py`, 169
  declarators) and holds each to one line — **Row** (a KorStandards key, named and present in the
  code), **Convention** (belongs in a row, still compiled: the debt, counted), **Tolerance**
  (slack against drafting and precision, code by design), **Rule** (a fact of geometry or of
  buildings), **Elsewhere** (the SAFE/WPF side's, sharing a file), **Dead** (fails until deleted —
  three were, and are gone). A constant added without a line fails the test.
- Tier one, wired end to end: three conventions the two sides mean the same thing by now read
  the DXF side's rows — `dxf.bridge-tolerance`, `dxf.min-plate-area`, `dxf.dash-join-gap` (both
  compiled values equal; the parity test holds them so) — and two PDF-side rows are seeded by
  **migration 085** for Ian: `dxf.pdf.fallback-scale` (96) and `dxf.pdf.ladder-min-rows` (3).
  `PdfIntakeOptions.SettingKeys` is 17 keys.
- Owed (the test counts them, 44 lines): the title-block region compiled five times over
  (`TitleRegionMinFx` — one definition first, then the row), the sheet-furniture and title-reader
  shares, the wall-tag and mark-up reaches, the bubble radii, the schedule column bounds, the DXF
  outlet's name height; each names its row in the test. NOT shared with the DXF side, deliberately:
  the slab-edge extend limit (48 in here; the DXF side measured extending as harmful and banks 0).
- Gate, met: the triage test green; `CompiledDefaultsAreTheBankedRowsTests` (085's two keys
  declared unbanked until applied); the six byte-identical.

### WP6 — The finish line
- The ledger's totals against §1: how many of the 292 build, how many sheets say why not, the
  yardstick distribution over the 66. One model — 31170's, or whichever the ledger ranks best
  of the architects' sets — goes in front of Andrea. What she and the ledger say is the backlog
  for the next plan, not this one.
- Gate: §1's three conditions, each with its evidence in the commit.

## 5. Process rules that become code, not prose
- **A red test blocks the bank.** WP1's verdict includes the full suite; red = no bank, no
  commit of a rule. There is no "known red".
- **Every step gets its adversary the same day.** After each package's commit, one bounded
  Codex brief (`docs/codex/CODEX-PDF-INTAKE-WP<n>.md`) on that package alone; findings become
  tests before the next package starts.
- **Rule 1 stays a gate:** before any new instrument, `takeoff --help` and `tools/` are
  searched, and the reply says so.

## 6. Parked until WP6 — the reading backlog, so it is not lost
Boundary walk (+13 storeys, stashed); a ring that is a piece of the floor; 25 storeys with no
plate (not one cause); tower walls 33 vs 40; mezzanines placed; 31130's halves; overlapping
collinear copies across sheets (§51); 31202's PENTHOUSE stated twice; 31065 ROOF 5.8 m over L19;
the harness's 31168 moved to the 09-10 reissue (re-banks every 31168 baseline — Ian's call);
the 57 EDB-only yardsticks exported to `.e2k` on a machine with ETABS.

## 7. What this plan does not do
It does not promise a storey count, a wall count, or "31168 = Revit". Those are measurements the
harness reports; the contract is §1. It does not touch the app (WPF), the Drafter bridge, or the
Revit route.

## 8. What needs Ian (2026-09-12 morning)

1. **Migrations — DONE 2026-09-12** (083, 084, 085 by Ian; then 086, because 084 and 085 had
   inserted their rows `'unverified'` and `analysis.vw_RuleSetting` exposes only `replay-verified` /
   `engineer-confirmed` — the tool never saw them and the parity test, reading the same view, stayed
   green instead of going red. 086 set the three rows `replay-verified` on the evidence that the six
   sets build byte-identical at those values; the three keys left `UnbankedByDesign`, and
   `CompiledDefaultsAreTheBankedRowsTests` now reads each row and holds it equal to its compiled
   default. Lesson in 086's header: a PDF-intake row is inserted `replay-verified` when the harness
   has replayed at its value, as 082's were.)
2. **ETABS 23 on KOR-210 — DONE 2026-09-12**: all nine exported (31097, 31138, 31168, 31170,
   31183, 31195, 31199, 31202, 50054 — 8.0 MB of `.e2k`), mirrored to the yardstick folder (105
   models); four of them are harness sets, so the six have engineers' yardsticks now.
3. **31130's yardstick** — 20 shared storeys and under 25% of our columns within 100 mm of the
   engineer's: the yardstick's own numbers say why (median 5.7 m ours → theirs, 22.9 m theirs →
   ours): the two halves of the building are registered as one frame. The backlog's "31130's
   halves" item, now with a measurement. Nothing for Ian.
3a. **What the yardsticks found on 2026-09-12** (PdfIntake.md §56): an inch applied in
   millimetre models (a unit differential now gates it), one column read from two sheets kept as
   two (an invariant now refuses it), and 31202's tendon anchors read as columns (step 48; 10 of
   55 a sheet caught so far). All six re-banked; migration **087** (`dxf.pdf.force-words`) for Ian.
3b. **Step 49, 2026-09-12 afternoon** (PdfIntake.md §57), on Ian's "go with step 1 — can we get
   these numbers nearly identical?": 31202's 34 unmatched "18x18" columns were the two filled
   quadrants of spot-elevation targets, read as columns, walked into one figure-of-eight by the
   loop builder, and placed by a centroid formula that divides by a near-zero area — eight joints
   at kilometres in every banked 31202 model since its first bank, the reason every render showed
   the building as a dot. Three rules, each banked: a centroid lies inside its own box; a
   target's quadrants are not columns (a pair of one-size filled shapes touching only at a
   corner); a member stands on the building (publish-blocking invariant; the OLD banked model
   fails it). 31202 **85% / 95%** (from 82%); 31065 one wall moved; four sets byte-identical.
   `dxf-inspect --loops` lists the classifier's own loops whose centroid is the vertex mean.
   Nothing for Ian. Next: the 42 anchors the chains miss, the 32 offset 12x24s, the crossing-strip
   wall loops (9 on 31202, 1 on 31065).
4. **WP6** — one model in front of Andrea (31170's, or whichever the ledger ranks best of the
   architects' sets). Ian's call when.
5. **Step 47**, the next reading rule, from the corpus. `takeoff corpus-query plan-titles` on the
   step-45 ledger: **1,049 of 3,967 written views (162 sets) the composer can put on no storey by
   name.** 579 are named by the PDF's stem and page (step 46's class — the running rebuild measures
   it); the rest name their storey with WORDS: FLOOR 269, SHOWING/OVER 62/63 ("MAIN FLOOR PLAN
   SHOWING 2ND FLOOR FRAMING OVER" — the storey is the one before SHOWING), MAIN 28, GROUND 21,
   FIRST/SECOND/2ND/3RD, LOWER; "PARKADE PLAN - P2" (P2 at the end of a title is not read as a
   parkade level); "FLOOR PLAN AND CEILING PLAN" ×16 (one-storey sets naming no level at all);
   "BUILDING n FLOOR PLANS". Ordinals and GROUND/MAIN/LOWER are a vocabulary row; one rule,
   measured on the six and on the corpus (`--recompose`, ~20 min) before it is kept.
