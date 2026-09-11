# PDF intake → ETABS — Completion Plan

**Status:** Proposed 2026-09-11, for Ian's approval. Nothing below starts until it is approved.
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

1. **A seventh set** — one not among the six used to build the rules, from an office we have
   not seen — builds end to end with zero code change, and its report names every sheet it did
   not read and why. (Ian supplies the set; I do not see it first.)
2. **Andrea accepts one model** built this way as a starting point she would use. (The
   31170-from-PDF deliverable exists; it has not been put in front of her.)
3. **Every instrument that measures the tool is code in the repo** — a `takeoff` verb, a test,
   or a `tools/` console — and every drafting convention the readers apply is a row in
   KorStandards with its compiled default proven equal by a test. Nothing the loop depends on
   lives in `%TEMP%`, in `docs/*.py`, or in a memory file.

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

### WP1 — The harness is a test, and the bank is a commit
- `SixSetsBuildAsBankedTests` (`Speed=Slow`): builds each of the six from its mirrored PDF,
  in parallel, and asserts byte-identity with `Baselines/pdf-only-<job>.e2k` **in the repo**.
  On a difference it prints the keyed member diff, the plate diff, and renders every storey of
  both to PNG in `TestResults/` — one verdict, nothing to chain by hand.
- Banking a step = replacing the baseline files in the same commit as the rule. The diff is
  reviewable in git; the console goes beside it.
- Deletes: `pdf_only_all.sh`, `pdf_only_one.sh`, `six_set_diff.sh`, `six_set_bank.sh`,
  `render_storeys.sh`, `members_diff.py`, `plate_diff.py`, `storey_counts.py`, `plan_sheet.py`
  (ported, as one C# implementation shared by the test and a `takeoff model-diff` verb).
- Gate: the test is green on `3da6f85c`'s six models; the old scripts and the new test give
  the same diff on one deliberately changed model.

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
- Ian names a seventh set. It is run once, untouched. Its report and model go in front of Andrea
  with the 31170 model. What she and the report say is the backlog for the next plan — not
  this one.
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
the harness's 31168 moved to the 09-10 reissue (re-banks every 31168 baseline — Ian's call).

## 7. What this plan does not do
It does not promise a storey count, a wall count, or "31168 = Revit". Those are measurements the
harness reports; the contract is §1. It does not touch the app (WPF), the Drafter bridge, or the
Revit route.
