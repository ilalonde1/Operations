# START HERE — the August 2026 audit, and how to work from it

**If you are a new session picking this up: read this file first, then `04-TODO-REGISTER.md`.
Do not read the whole audit into context — it is ~13,000 lines and will not fit usefully.**

Produced 2026-08-20/21. Fourteen module audits, four competitive studies, a cost benchmark, a demo
run-sheet. Covers ~464,000 hand-written lines across the Operations Brain and its engineering
tooling.

---

## 1 · What is authoritative

| File | What it is | Read it when |
|---|---|---|
| `04-TODO-REGISTER.md` | **182 actions, sized, tagged, with owners, each carrying a `status`. This is the roadmap and the single arbiter of what is done.** | Always. Start here. |
| `WORKLOG.md` | **Starts with WHERE THIS STANDS — the résumé of what is shipped, what is fixed-but-not-deployed, and what is in flight.** Then the evidence trail behind each status change | **Picking this up cold — read this first**, and before trusting any `verified` |
| `codex/BRIEF-*.md` | The exact text handed to Codex for each batch | Reconstructing why a change was made |
| `07-EXECUTIVE-SUMMARY.md` | The whole picture in ~12 minutes | You need context fast |
| `05-MASTER-AUDIT.md` | Macro view + the five systemic findings | You are fixing a *class* of problem |
| `modules/NN-*.md` | One deep audit per module | You are working on that module — **read only that one** |
| `03-COMPETITIVE-BATTLECARD.md` | Objection → response, sourced | Anything client-facing |
| `06-DEMO-RUN-SHEET.md` | Pre-flight, order, avoid-list, aborts | Demo prep |
| `08-COST-BENCHMARK.md` | What this would cost to build or buy | Valuation, hiring, board conversations |
| `00-INVENTORY.md`, `01-DOC-TRUST.md`, `02-CROSS-CUTTING-SCAN.md` | Machine-generated scans | You need raw counts |

`RUBRIC.md` defines the evidence tiers every finding carries. `SCOPE.md` defines what was in and
out.

---

## 2 · Calibration — which half of this to trust

This matters more than any single finding.

**The defect findings held. The absence findings did not.**

- **Trust `[RUN]` and `[QUERIED]` claims about what is broken.** Every "this fails at `file.cs:line`"
  survived challenge. Two module audits overturned their own first answers under pressure rather
  than defending them.
- **Distrust every claim that a capability does not exist.** Four were wrong, four times out of
  four — the transmittal type system, per-bookmark PDF annotation, custom detail-sheet creation, and
  the Virtual Drafter as a whole. **Cause: searching for a phrase instead of a capability.**
  `grep "Transmittal"` missed the transmittal window because it is `MainWindow`. `grep "detail
  sheet"` missed sheet composition because the buttons are named for verbs.
- **Distrust the mechanical scans in `02-CROSS-CUTTING-SCAN.md`.** Its "no hardcoded credentials"
  result was wrong (there are nine locations, now listed in that file's correction block); its
  empty-catch counts run ~4× low; its hardcoded-path hits ran about 1 in 8 true. The file carries
  its own retraction — read that before citing it.

**Rule for a new session: before reporting that KOR lacks something, ask the owner what it is called.
He has been right every time.**

---

## 3 · Evidence tiers

Every finding is tagged. Preserve them; never promote one.

- `[RUN]` — executed and observed
- `[QUERIED]` — live system, database or endpoint read
- `[READ]` — source inspected, not observed running
- `[DOC]` — a document asserts it. **Lowest trust.** 47% of `docs/` is >60 days old and `AGENTS.md`
  is 0 for 3 on its warnings.

---

## 4 · How to run a work session against this

1. Open `04-TODO-REGISTER.md`. Pick items — ideally a whole theme, not scattered ones.
2. Open **only** the module report(s) those items belong to.
3. Verify the finding still holds before fixing it. Several have been fixed since; the register
   records status, not history.
4. Fix. Then **make the check permanent** — see §6.
5. Update the item's `status` in the register, in the same session, and append the evidence to
   `WORKLOG.md`. The register says *whether*; the worklog says *how you know*. Never mark
   `verified` from a report — only from the artifact.

**Do not re-audit.** If something looks unexamined, grep this directory first — fourteen modules
are covered and the answer is usually already written down.

---

## 5 · The five systemic findings — fix as classes, not instances

1. **The system reports success it has not earned** — 16 instances, 8 modules. The highest-value
   class fix in the whole audit.
2. **Deployed artifacts cannot be traced to source** — 7 modules.
3. **One computation, two wirings, no arbiter** — 17 instances.
4. **Verification points away from the risk.**
5. **The written record is wrong, and some of it is machine-read.**

---

## 6 · Turn findings into ratchets

Per the repo's own working rule — *checks go in the build, not in your head*. These four would each
have caught a real defect found here, and would stop it recurring:

| Ratchet | Catches |
|---|---|
| **Deploy provenance check** — byte-scan the deployed binary for a known symbol, not the version stamp | The MCP service running 34-day-old DLLs while `/health` returned 200 |
| **Secret scan across `.cs .config .json .xml .ps1 .md`** | Nine credential locations; the `.cs`-only scan found none |
| **Doc staleness gate** — fail when a doc is older than the code it describes | `AGENTS.md` 0 for 3; `BRIDGE-READY.md` naming the wrong Revit |
| **Shipped-artifact text check** — parse the PDF, never the HTML | The one-pager that ships an Edge 404 into client job folders |

---

## 7 · Safety notes

- **The rendered PDFs and `_*.html` are gitignored and must stay that way.** They quote live
  credentials verbatim as evidence.
- **Correction, 2026-08-21: the markdown was *not* fully redacted.** This file previously asserted it
  was. A secret scan run before the first commit found the redirector's **live Entra client secret**
  quoted verbatim at `modules/02-transmittals-tracking.md:259` — the one item 59 exists to rotate.
  It is redacted now, and it never entered git history. Every other `Password=` hit in the tree is
  either already `‹REDACTED›` or the dummy `Password=secret` literal that genuinely lives in
  `DetailsCatalogTests.cs`. **Re-run the scan before committing anything new here** — this is
  ratchet #2 from §6, and its first run caught a real one.
- **Screens that must not be shown externally** are listed in `06-DEMO-RUN-SHEET.md`. The short
  version: BD Workspace Dashboard, BD Scorecard, Employee Summary, `get_wip`, the KOR.Drafter repo,
  and this repository's own `docs/` folder (it contains an MVE dossier).
- Nine credential locations are catalogued in `02-CROSS-CUTTING-SCAN.md`. **Rotating the committed
  SQL passwords breaks email filing for all 40 staff** — the VSTO add-in has no override path. Plan
  it; do not do it casually.
