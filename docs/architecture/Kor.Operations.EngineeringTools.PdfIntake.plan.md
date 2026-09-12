# PDF intake → ETABS — Completion Plan

**Status:** Rev 2, 2026-09-11 — revised the same day on Ian's direction: *"rather than build the
brain, then expose it to one drawing at a time … build an analyzer to get all the info you need at
once"*, and on the census that direction called for (§1a). Nothing below WP1 starts until approved.
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

## 1b. The corpus, built (2026-09-11, `takeoff corpus-analyze`, run 1 — WP1's first pass, PdfIntake.md §53)

| | |
|---|---|
| Sets built from the PDF alone | **39 of 292**; 8,692 pages read, 3,989 plans, 0 failed |
| No model because no storey ladder was read | **225 of 292** — they all have plans (2,462 plan views); the ladder reader wants shear-wall elevations, and most of the office's sets have none |
| Of the 39: plan views set on the grid by name | 873 of 2,058 (42%) |
| Of the 39: storeys with a plate | 230 of 1,098 (21%) |
| Yardsticks (engineers' own models, 92 exported from KOR-210) | 18 of the 39 have one; **45% of our columns within 100 mm of theirs, 52% of theirs within 100 mm of ours**; per set from 100% (31039) to under 25% (six sets) |

**The work order is a count now.** 1. A set's storeys from its plans (names, order) with heights
from sections where present, the architect's set, or a stated assumption — the one rule that
unblocks 225 sets. 2. Views on the grid (42%). 3. Plates (21%). Each measured on 292 before it is kept.

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
| Loose scripts the loop depends on | **40 python + 5 bash** in `docs/etabs-handoff/` (26 touched this week) | `takeoff` verbs, tests, or deleted with the finding recorded |
| Banked baselines and the yardstick | 80 `.e2k` (1.8 MB for the current six) + 616 MB of renders/DXFs in **`%LOCALAPPDATA%\Temp`**, banked by a hand-typed `cp` until this morning | the current six in the repo beside the tests; a bank is a reviewable commit diff |
| Drafting conventions compiled as constants | **106** `const` values in the readers (reach, taper share, pattern count, label reach…) against **49** rules in KorStandards | rows, read through `PdfIntakeOptions.For(conn)`, parity gated by `CompiledDefaultsAreTheBankedRowsTests` |
| `Program.cs` | **5,153 lines, 56 verbs** in one file | one file per verb, a registry, the help-list test |
| Transport between the two halves | scratch DXF written to disk and re-read (`pdf-takeoff` → `dxf-to-etabs`) | in-memory `PlanGeometrySet`; the DXF stays as an outlet |
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

### WP2 — Instruments are verbs
- The instruments used more than once become `takeoff` verbs on the same code the readers use:
  `pdf-lines`, `pdf-words` (band and near), `pdf-tiles`, `grid-names`, `model-to-page`,
  `outside-plate`, `columns-vs-yardstick`, `crop`. Each prints what it covers.
- The one-off diagnostics (the rest of the 45) are deleted; the finding each produced is
  already in `PdfIntake.md`, and that section gets the sentence "measured with X, since removed".
- Gate: `docs/etabs-handoff/` holds `.md` only; `takeoff --help` lists every verb (existing test).

### WP3 — `Program.cs` one file per verb
- `Verbs/<Verb>.cs` each with its usage line, a registry, `Program.cs` under 100 lines.
- Gate: the help-list test; the six baselines byte-identical (WP1's test).

### WP4 — In-memory handoff
- Gate FIRST: a test that builds all six through the DXF detour and through memory and asserts
  the `.e2k` identical. Then `DrawingIntake` hands `PlanGeometrySet` straight to the composer;
  `pdf-takeoff` keeps writing DXF for anyone who wants it, as an outlet.
- Deletes the scratch-DXF folder per job and the re-read.
- Gate: that test, green; the harness run time falls (measured, stated).

### WP5 — Conventions are rows
- The 106 compiled constants triaged in one table: **drafting convention** (a row, with the
  compiled value as its default — the pattern `PdfIntakeOptions` already uses for the vocabularies),
  **geometry tolerance** (stays code, named, documented as such), or **dead**. One migration for
  Ian (`KOR.Drafter\db\083_...`), one parity test extended.
- Gate: `CompiledDefaultsAreTheBankedRowsTests` covers every row; the six byte-identical.

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
