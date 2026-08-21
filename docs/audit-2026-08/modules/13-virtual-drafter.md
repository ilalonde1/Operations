# Module 13 — The Virtual Drafter program (`KOR.Drafter`)

**Audited 2026-08-21.** One repo outside the Operations solution, one workstation, one database schema.
Evidence tiers: `RUN` executed · `QUERIED` live state read · `READ` source only · `DOC` a document says so.

Modules 11 and 12 read in full first. **11** covered the bridge and RevitTools as *products*; **12**
covered the *standards chain*. Neither asked what the Virtual Drafter **is**, whether the exam is
real, or whether any of it can be put in front of MVE. This module answers only those. Where a
finding is 11's or 12's it is cited, not re-derived — with two exceptions where I reached a
different conclusion from the same evidence (§5.2, §5.3), both marked.

---

## 1. What I searched

**Read first, as instructed** (and then verified rather than restated)
- `docs/STATE-2026-08-04.md`, `docs/ROADMAP.md`, `docs/TRAINING-CURRICULUM.md`,
  `docs/TEMPLATE-BUILD-PLAN.md`, `docs/ARCHITECTURE-INTERCONNECTION.md`, `docs/PLAN-2026-08-04.md`,
  `docs/FINISH-PLAN.md`, `docs/CRAWL-RUNBOOK.md`, `docs/ECONOMICS-BASELINE.md`.
- Root: `PROJECT-CONTROL.md`, `RESUME-HERE.md`, `BRIDGE-READY.md`, `START-NEW-SESSION.txt`, `README.md`.
- `intake/simon-standard-details-2026-07-30/FINDINGS.md`; `exam/31202-01/` — all six files in full.
- `standards/RULINGS.md`; `process-record/reports/` (85 reports; read VERB-SMOKE, DRILL-02/02c/04/05,
  the four `31202-01_*`, `COMPLETED-WORK-ORDER-GAIRWILLIAMSON`).
- Sibling audits `modules/11-revit-drafter.md` and `12-standards-centralisation.md`, both in full.

**Git**
- `git log --since="30 days ago"` → **69 commits**, all between 2026-07-31 and 2026-08-15 `[RUN]`.
- `git show 3624a61 --stat` and its `BridgeApp.cs` diff (**12 lines, all additive**);
  `git log -- src/KOR.Drafter.Bridge/BridgeApp.cs` (17 commits); `git show d17b625:…/BridgeApp.cs`;
  `git show 3624a61:Directory.Build.props`; `git log -- artifacts/` (**empty — artifacts are untracked**).

**Builds / scans I ran**
- `dotnet build src/KOR.Drafter.Bridge -c "Debug R25"` → **succeeded, 0 warnings, 0 errors, 0.8 s** `[RUN]`
- Verb enumeration from the dispatch switch, `BridgeExec.cs:32-73` → **42 top-level cases** `[RUN]`
- Byte-scan of `artifacts/2024` and `artifacts/2025` `KOR.Drafter.Bridge.dll` for the literals
  `exportdxf`, `exportsheets`, `newdim`, `newlines`, `has been copied or moved` `[RUN]`
- `grep -cE 'TODO|FIXME|HACK|NotImplementedException|NotSupportedException'` over all four bridge
  source files; `grep -cE 'catch\s*(\([^)]*\))?\s*\{\s*\}'`; `grep -n 'C:\\KOR'` `[RUN]`
- `pdftotext -layout` on `Operations/docs/KOR-Virtual-Drafter-Playbook-2026-07-30-web.pdf` (126 lines)
  and on the plotted exam deliverable (3 pages), then token-scanned both for employee and
  third-party firm names `[RUN]`

**Live state I read (read-only; targeted `Test-Path` / single-level listings, never `-Recurse` over the projects share)**
- `\\KOR-302N\C$\KOR.Drafter\` root, `logs\` (8 newest), `models\`, `evidence\31202-01_2026-07-27\`,
  `deliveries\`, `tasks\QUEUE\`, `tasks\template\work\`, `bridge\inbox\done\*.json`, `CLAUDE.md` (full) `[QUERIED]`
- `\\KOR-302N\C$\Users\administrator.kor\AppData\Local\KOR.Drafter\<year>\*.dll` — 7 years, mtimes `[QUERIED]`
- `\\KOR-302N\C$\Users\` profile list; `app\Dialog-Watchdog.ps1` existence `[QUERIED]`
- **The 302N Claude session transcripts** —
  `\\KOR-302N\C$\Users\administrator.kor\.claude\projects\C--KOR-Drafter\*.jsonl`, 8 files.
  Read the 8.5 MB exam-session file into memory and counted occurrences of `answer-key`, `answer key`,
  `ANSWER KEY`, `sacosti`, `0728-0700`, `Hotel Circle North_Struct - R24.rvt`, `closed-book`, then
  pulled ±350 chars of context around every hit of the last model name `[RUN]`. **This is the
  verification the exam turns on.**

**Not done, deliberately:** no writes anywhere; the KorStandards/KorTransmittals row counts and the
conformance scoreboard are module 12's and are not re-queried here; no Revit was opened.

---

## 2. What this module is

**The Virtual Drafter is not a program that drafts. It is a general-purpose LLM agent with a remote
control for Revit and a written apprenticeship.** That sentence is the whole finding, and it should
be said in exactly those terms, because the alternative readings are all wrong in ways a technical
lead will find. There is no compiled drafting application, no UI, no ribbon, no installer beyond a
single invisible add-in, and — critically — **no code anywhere in the repo that decides what to
draft.** The C# is 3,489 lines and every line of it is plumbing: a folder watcher, a JSON parser, and
42 verbs that each wrap one Revit API call in one named transaction `[RUN]`. The drafting judgement
lives in three non-code places: a 9.2 KB seed prompt installed as `CLAUDE.md` on the workstation
`[QUERIED]`, a 29.7 KB wire-protocol document, and a corpus of measured facts about KOR's own model
fleet (194 per-job dossiers, a rebar census, a markup lexicon, a ruling register). Claude Code runs
on KOR-302N, reads a Bluebeam-marked-up PDF, writes JSON command files into
`C:\KOR.Drafter\bridge\inbox\`, and reads responses out of `outbox\`. That is the entire mechanism.

**What a user would see and do.** An engineer marks up a PDF in Bluebeam exactly as today and hands
it over — that part genuinely does not change. Ian starts a session on one workstation. The agent
extracts the markup annotations structurally (author, text, cloud geometry, page coordinates), fits a
page→model coordinate transform from the sheet's own grid bubbles, queries the model to find the
elements each cloud points at, and writes a numbered **change plan** in drafting language with every
ambiguity listed as a question. **It then stops and waits for a human to approve** — that gate is
written into the seed prompt as non-negotiable and has not been lifted `[QUERIED]`. On approval it
executes one command per change, re-reads every element it wrote to confirm the write took, exports
before/after PNGs, and produces a change report. A human applies the verified result to the real
project model through normal drafting process; the bridge physically refuses to write to any model
still bound to a live central, and that refusal is observable in the logs `[QUERIED]`.

The honest one-line characterisation for MVE: **it does the mechanical transcription half of a
revision and asks a person about the rest, and it can prove everything it did.** On the one job where
the referral rate was measured it drafted 22 of 45 substantive annotations and referred 23 `[DOC]`.

---

## 3. How you would demo it

**There is no live demo, and there should not be one.** The interface is a JSON file appearing in a
folder. It runs on one workstation on the KOR LAN with Revit open and the right model loaded, and the
last recorded session on that machine spent its first eight minutes failing (§5.3). Any attempt to
run it at MVE would be a screen-share of a console over VPN into a machine that has to be babysat.

**What there is instead is an unusually strong static-artefact demo.** Every file below exists; I
opened each one.

| # | Artefact | Path | Verified |
|---|---|---|---|
| 1 | **The playbook** — 5-page plain-language PDF, "prepared by Ian Lalonde, July 30 2026" | `Operations\docs\KOR-Virtual-Drafter-Playbook-2026-07-30-web.pdf` (114 KB) | `RUN` — extracted as text; **contains no employee name and no third-party firm name** |
| 2 | **Before/after sheet renders**, 8000 px, both levels | `\\KOR-302N\C$\KOR.Drafter\evidence\31202-01_2026-07-27\{BEFORE,AFTER}_{L6,L7}.png`, ~1.07 MB each | `QUERIED` — all four present, 2026-07-29 |
| 3 | **The plotted revised sheets** — the machine's own deliverable | `\\KOR-302N\C$\KOR.Drafter\deliveries\31202-01 - Reinforcing Sheets - REVISED per JD markup 2026-07-27.pdf`, 562 KB | `RUN` — 3 pages; carries `12#5-27.6`, `C#5-14.9`, `C#5-29.6`; **no personal names** |
| 4 | **The full command transcript** — every request and response | same evidence folder, `bridge-transcript.jsonl`, **7.2 MB** | `QUERIED` |
| 5 | **The exam scorecard and completion report** | `exam/31202-01/EXAM-SCORECARD.md`, `…COMPLETION-REPORT.md` | `RUN` — **needs a correction before use, see §5.1** |
| 6 | **The verb smoke test** — 44/45, scored on product not on `ok:true` | `process-record/reports/VERB-SMOKE_2026-08-01.md` | `RUN` |
| 7 | **The fleet crawl** — 194 job dossiers + rebar census, read-only, overnight | `crawl-results/dossiers/` (194 files), `KOR-Fleet-Rebar-Census-2026-07-30.csv` | `QUERIED` |

**The demo that actually works:** hand over artefact 1, put artefact 2 side by side on screen, and
tell the story with artefact 4 as the receipt — *"every command it issued is in that file."* Ten
minutes, no VPN, no Revit, nothing to crash. Artefact 3 only if the grid-B correction in §5.1 is
made first. **Do not open the repository on screen** (§7).

Prerequisites for the artefact demo: none. Prerequisites for anything live: KOR LAN or VPN, Revit
installed, KOR-302N reachable and not already in use, and roughly an hour of setup.

---

## 4. Completeness

### 4.1 The program

| Capability | State | Evidence |
|---|---|---|
| Bridge add-in — 42 JSON verbs, one transaction per write, central-write refusal | `WORKING` | `RUN` (builds clean R25); `QUERIED` (2026-08-15 log shows a correct `WRITE REFUSED` naming the central) |
| Verb-level product verification (not `ok:true`) | `WORKING` | `RUN` — 44 PASS / 45, one FAIL (`closedoc`, since fixed at `BridgeExec.cs:2442`), 2 skipped |
| Markup → change plan → approve → execute → verify → report loop | `WORKING`, human-gated by design | `QUERIED` (seed prompt step 3); `RUN` (exam artefacts) |
| Closed-book head-to-head against a production drafter | `WORKING` and **forensically verified** | `RUN` — see §5.1 |
| Accuracy measured against human ground truth | `PARTIAL` — one job, narrow denominator, honestly stated | `DOC` — DRILL-02c: reading fidelity 45/45, located agreement 8/8 of 8 text-verifiable of 22 confirmed; 14 geometric items **unscoreable** |
| Fleet knowledge (194 model dossiers, rebar census, conventions) | `WORKING` | `QUERIED` |
| `exportdxf` — Revit → DXF for the ETABS generator | `PARTIAL` — works on one machine, **absent from shipped artifacts** | `RUN` (byte-scan); `QUERIED` (3 successful exports 2026-08-15) |
| Unattended running on a machine other than KOR-302N | `DEAD` | `QUERIED` — `Dialog-Watchdog.ps1` exists **only** at `\\KOR-302N\C$\KOR.Drafter\app\`; not in the repo |
| **Job instantiation from the template** | **`STUBBED` — the pieces exist, the path has never been walked** | see §4.3 |
| Economics / ROI measurement (DRILL-06) | **`STUBBED` — never run** | `RUN` — no DRILL-06 report among 85; see §6 |
| Grouped-level revision work (the largest class of production revisions) | **`DEAD` — permanent Revit constraint, correctly closed** | `DOC` — ROADMAP A2: both attacks tested, both failed; `swapgrouptype` now refuses rather than silently deleting |
| Second operator / second machine | `DEAD` | `DOC` — ROADMAP C1 **OPEN**; one person, one workstation |

**Marker counts, `src/KOR.Drafter.Bridge/` (4 files, 3,489 LOC):** `TODO` / `FIXME` / `HACK` /
`NotImplementedException` / `NotSupportedException` — **0, in every file** `[RUN]`. This is not a
codebase that parks work in comments. **Empty catch blocks: 21** (`BridgeApp.cs` 3 of 12 `catch`
keywords, `BridgeExec.cs` 18 of 34) — matches module 11's count exactly. `throw new` appears **110
times** in `BridgeExec.cs` `[RUN]`: the verbs fail loudly and name the reason, which the smoke test
independently confirms is their most valuable property.

### 4.2 The 41 verbs — enumerated, and the doc's number is now wrong

`STATE-2026-08-04.md` says *"bridge 1.0.28, 41 verbs"*. **Today the dispatch switch has 42 top-level
cases** (`BridgeExec.cs:32-73`) `[RUN]`. The 42nd is `exportdxf`, added 2026-08-15 by commit
`3624a61`. The doc was right when written; it is 17 days and one verb stale.

Grouped by what they make Revit do, with state. Smoke-test column is
`process-record/reports/VERB-SMOKE_2026-08-01.md`, 2026-08-01, bridge 1.0.26, every verb scored on a
fresh re-read of its product, never on the response echo `[RUN]`.

| Group | Verbs | State | Note |
|---|---|---|---|
| **Read** | `ping` `query`(views/sheets/elements/familytypes/docs) `getparams` `groupinfo` `viewportinfo` `importinfo` `exportlookup` | `WORKING` | 91.5% of all traffic ever (§6) |
| **Edit existing elements** | `setparams` `settext` `formattext` `moveelement` `rename` `delete` | `WORKING` | `setparams` is all-or-nothing and rolls back naming the element's real writable parameters; `formattext` on a non-matching find fails loudly (`ROLLED BACK — 1 edit(s) failed`) |
| **Place new content** | `place` `newnote` `newtag` `newlines` `newdim` `newregion` `loadfamily` | `WORKING` with one caveat | `newtag` **head position is unreliable in rapid succession** — 7 of 10 tags landed at the view origin, invisible on the sheet, while returning `ok:true` (§5.4) |
| **Sheets & views** | `newsheet` `newview` `placeview` `duplicateview` `copyview` `moveviewport` `setcrop` `setscale` `arrange` `sethidden` | `WORKING` | `newsheet` requires `like:"<existing sheet>"` — PROTOCOL shows it optional; `arrange` is PASS-on-acceptance only, draw order is not readable back |
| **Group surgery** | `ungroup` `regroup` `placegroup` `swapgrouptype` | `WORKING` but **policy-restricted** | Splits the shared group type; needs drafting-team sign-off. `swapgrouptype`'s guard was made to fire and refused correctly |
| **Documents** | `opendoc` `closedoc` `savedoc` | `WORKING` | `savedoc` calls `RequireWriteSafe` (`BridgeExec.cs:2486`) — added after an independent audit found it refused the central but passed a live *local* workfile |
| **Export** | `exportview` `exportsheets` `exportdxf` | `WORKING` on 302N; **`exportdxf` `PARTIAL`** | `exportsheets` is Revit 2022+ only and throws on 2020/2021 |
| **Escape hatch** | `postcommand` | `STUBBED by policy` | Probe-only. `ID_EDIT_GROUP` is a one-way door — 21 finish ids probed, zero resolve |
| **Import** | `importlookup` | `WORKING` | 9 calls, 0 errors |

**Dead code:** one, `BridgeApp.cs:198-203` (§5.3). **No other verb has this problem** — I looked for
the pattern (a computed result discarded before use) across both files and `result = 8` is the only
instance. The `DetailElementOrderUtils` and `ViewDuplicateOption` sub-switches all reach their calls.

### 4.3 Job instantiation — traced end to end, and it does not close

The claim (`ARCHITECTURE-INTERCONNECTION.md:28`, `TEMPLATE-BUILD-PLAN.md:6`) is that the template is
*"the file a drafter does File > New from, and the file the Virtual Drafter instantiates a job
against."* Tracing it:

| Step | State |
|---|---|
| A template file exists | **YES** — `\\KOR-302N\C$\KOR.Drafter\tasks\template\work\Kor_Structural_Standards_Template_R25.rvt`, 129.5 MB, 2026-08-06, plus a wood-frame sibling `[QUERIED]` |
| It is a Revit **template** (`.rte`) | **NO.** It is a `.rvt`. `find . -iname "*.rte"` over the whole repo returns **nothing**, and the file on 302N is a project file `[RUN]` |
| A verb creates a document from a template | **NO.** There is no `newdoc`/`newproject` verb in the 42. `OpenDoc` (`BridgeExec.cs:2397`) only opens an existing path `[READ]` |
| The mechanical substitute — `opendoc` template → `savedoc path:<job>.rvt` | **Untested.** `savedoc` has been called **twice, ever**, both into a drill folder `[DOC]` |
| The title block accepts job data via `setparams` | **NOT ESTABLISHED.** Phase 1 of `TEMPLATE-BUILD-PLAN.md` requires a parameterised block exposing Project Name/Number/Address/Client/Consultant. What was actually done was a Family-Editor **deletion** of the previous architect's static text (`COMPLETED-WORK-ORDER-GAIRWILLIAMSON-2026-08-05.md`) — removing wrong content, not adding parameters |
| Anyone has started a job from it | **NO.** `TEMPLATE-BUILD-PLAN.md:126` names this as the acceptance test — *"A template that has not been instantiated has not been tested"* — and nothing in `process-record/` records it being done |

**Break points, in order:** (1) no `.rte`; (2) no verb that instantiates; (3) title block not
parameterised; (4) the acceptance test the plan itself specifies has never been run. **None is hard
— together they are perhaps a day — but today the answer to "can it start a new job?" is no.**

---

## 5. What is broken or risky

### 5.1 The exam is real, the closed-book condition is verifiable, and the scorecard is stale on the one point that matters

This is the centrepiece and it deserves the space. Taking it as a chain of separate claims:

**The task and the people.** Job **31202-01, Hotel Circle North** (1650 N Hotel Circle, San Diego).
Sheet **S2.08.2** "LEVEL 7-12 PLAN - REINFORCING" plus **S2.07.2** (Level 6). The markup is
`Pages from 31202-01 2026-07-27 Hotel Circle North StickSet - FULL SET JD.pdf`, author **`jdesroches`**
— Jim DesRoches, KOR principal — annotations timestamped 2026-07-27 18:40–18:52, **13 annotation
groups over 12 change locations** `[DOC, from the exam's own capture]`. The production
implementation was done by **`sacosti`** on 2026-07-28/29 **in the live central** `[DOC]`.

**The machine's run.** 2026-07-29 evening, bridge v1.0.4→v1.0.8, Revit 2024.3 (24.3.40.26), on
`HotelCircle_BEFORE_0728-0700.rvt` — a pre-revision snapshot, 180.9 MB, mtime 2026-07-27 07:44
`[QUERIED]`. Output `HotelCircle_FINAL_TAGGED.rvt`, 123.5 MB, saved 2026-07-29 21:48 `[QUERIED]`.
**The output was never pushed to the central** and the central has since moved on — the machine's own
follow-up audit says so explicitly `[DOC]`.

**Was it really closed-book? Yes — and this is the finding I would lead with.** The claim is
checkable and I checked it. The 302N session transcripts show **two distinct sessions**: `32e4a8c5`
(2026-07-29 15:36→18:01, 1.65 MB) which produced `answer-key.md` at 17:57, and `7a977b55`
(18:23→21:59, 8.5 MB) which produced the AFTER renders at 21:46, the completion report at 21:49 and
the scorecard at 21:52 `[QUERIED]`. Reading the **exam session file in full** and counting `[RUN]`:

| Token | Occurrences in the exam session |
|---|---:|
| `answer-key` / `answer key` / `ANSWER KEY` | **0 / 0 / 0** |
| `sacosti` | **0** |
| `HotelCircle_BEFORE_0728-0700` (the pre-revision snapshot) | **55** |
| `Hotel Circle North_Struct - R24.rvt` (the post-revision model) | **2** |

I pulled ±350 characters around both hits of the last one: **both are the same single directory
listing**, one being the `tool_result` and the other its `toolUseResult` echo of the identical
`Get-ChildItem` output. The post-revision model was **never opened** in the exam session. The
closed-book claim survives forensic verification. Say it in exactly that form if MVE pushes.

**Who graded it — and here the scorecard overstates.** The task brief I was given said "graded by a
KOR principal." **That is not what happened.** `EXAM-SCORECARD.md` states its own method: *"Graded:
2026-07-29 by cross-referencing `answer-key.md` against `final-inventory.md` + FINAL completion
report."* Its file mtime is 21:52, seven minutes before the exam session ended. **The exam was scored
by the same agent session that sat it**, against an answer key that agent's predecessor session had
built by reading the production drafter's finished work. That is a defensible method — the answer key
is a mechanical element-by-element inventory, not an opinion — but it is **self-grading**, and a
technical lead will ask.

**Jim did adjudicate — two days later, and it went 1–1.** `standards/RULINGS.md:51-53` records
three rulings by **Jim DesRoches, 2026-07-31** `[READ]`:

| Contested point | Scorecard's claim (2026-07-29) | Jim's ruling (2026-07-31) | Outcome |
|---|---|---|---|
| Grid B — 12 vs 14 bars | *"Δ — exam matches markup"* | **"14 bars correct"** — production was right | **Machine LOSES** |
| Grid M — `#5` vs `#6` | *"Δ — exam matches markup"* | **"#5 as marked — production #6 was a confirmed error"** | **Machine WINS** |
| Level 13 scope | *"Open scope question"* — machine followed the markup literally and did not touch L13 | **"Same changes, grids 1–3"** — production was right to extend | **Machine LOSES on scope** |

So the defensible statement is: **12 of 12 marked locations implemented and verified; of the three
points where machine and human differed, the engineer sided with the machine once and with the human
twice.** The scorecard's *"2 locations more faithful to the markup than production"* and the
playbook's *"Both are now questions back to the engineer"* are both **STALE** — the questions were
answered three weeks ago and half the answer went the other way. **This must be corrected before
either document is shown.** It is also, told correctly, still a good story: an automated system found
a real `#6`-vs-`#5` error in issued production work, on the one bar at grid M where the size actually
prints on paper.

**What was autonomous, and what a human did.** Human: restored the VSS snapshot, supplied the markup
PDF, and — per the seed prompt's step 3, which has not been lifted — **approved the change plan
before any write**. Human did **not** correct any model content. The machine self-caught and disclosed,
unprompted: three stray duplicate bars and ten stray/misplaced tags from its own retries, and a bug in
its own polling harness that had made one tagging pass **report work it had not done** (fixed, and
every result re-verified with fresh ids afterwards) `[DOC]`. Disclosing your own harness bug in your
own scorecard is the single strongest credibility signal in this repo.

**Do the artefacts still exist?** All of them `[QUERIED]` — see §3, rows 2–5. Only `AFTER_L7.png` was
copied into the repo; the other four evidence files and the 123 MB model live on KOR-302N alone.
**There is no backup of the exam evidence off that one workstation.**

**Verdict on the exam: real, not a curated demo.** A curated demo does not run in a separately
verifiable session, does not publish the transcript, does not disclose its own harness bug, and does
not get half its headline overturned by the engineer two days later and leave the ruling in the repo.
What it is *not* is reproducible-by-command: re-running it means re-running an agent, and a different
session may not produce the same output.

### 5.2 The bridge you would install today is not the bridge that works — and two different DLLs both call themselves 1.0.31

Byte-scanning the repo's shipped artifacts `[RUN]`:

| Literal | `artifacts/2024/…dll` | `artifacts/2025/…dll` |
|---|---|---|
| `exportsheets`, `newdim`, `newlines` | present | present |
| **`exportdxf`** | **absent** | **absent** |
| **`has been copied or moved`** (the dialog fix) | **absent** | **absent** |

Both are stamped 2026-08-04 20:08. `README.md` and `BRIDGE-READY.md` both instruct a deployer to copy
`artifacts\<year>\` to the workstation — **following those instructions today installs a bridge with
neither the DXF export nor the dialog fix.** This confirms module 11's finding on `exportdxf` and
extends it: the dialog change is missing too, and `git log -- artifacts/` is **empty**, so the DLLs
are untracked build outputs that no commit updates.

**The version number did not move.** `Directory.Build.props:10` read `<Version>1.0.31</Version>` both
at commit `3624a61` and today `[RUN]`. The 302N-installed DLLs are dated 2026-08-15 21:19/21:21 and
the repo's are 2026-08-04 `[QUERIED]` — **two materially different builds, both announcing
`Bridge 1.0.31 up` in the log** `[QUERIED]`. There is no way to tell from a log line which binary is
running. That is a real diagnostic hazard, and it is a five-minute fix.

### 5.3 The dialog fix is dead — and the log proves it is dead for a sharper reason than the source alone shows

Module 11 found `BridgeApp.cs:198-203`'s `result = 8` unreachable because `:216` computes
`answer = !(e is TaskDialogShowingEventArgs) || (id.Length > 0 && id != "?")`, which is `false`
exactly when `id == "?"`. Confirmed `[READ]`. **I can add a harder fact.** The 2026-08-15 log shows
`DIALOG SENTRY [dxf-open-65409]: '?' -> 1 dismissed` — `answer` was **true** with `id == "?"`
`[QUERIED]` — and the command file `bridge\inbox\done\dxf-open-65409.json` is
`{"verb":"opendoc","path":"C:\\Temp\\kor-dxf\\500Foster.rvt"}` with **no `dialogAnswers` override**
`[QUERIED]`. The only branch that yields `answer == true` with `id == "?"` and no override is when
`e` is **not** a `TaskDialogShowingEventArgs` at all. But `result = 8` sits *inside* the
`if (e is TaskDialogShowingEventArgs td)` block. **The fix is not merely unreachable — it is in the
wrong branch for the dialog it targets.** The commit message's *"Now matched on its text and answered
with Close"* is wrong twice over, and the `git show` confirms the commit added exactly 12 lines to
`BridgeApp.cs`, all of them that block, changing nothing else.

**Its cost is on the record.** The same log `[QUERIED]`:

```
21:27:53  DIALOG SENTRY [dxf-open-65409]:  '?' -> 1 dismissed
21:27:57  [dxf-open-65409]  opendoc -> ERROR Opening was canceled.  (79263 ms)
21:29:18  DIALOG SENTRY [dxf-open2-12714]: '?' -> 1 dismissed
21:29:20  [dxf-open2-12714] opendoc -> ERROR Opening was canceled.  (54916 ms)
21:32:45  [op3-70484]       opendoc -> ERROR Opening was canceled.  (55373 ms)
21:33:15  Bridge 1.0.31 up.  Revit 2020 …
21:37:10  [exp-51327]  exportdxf -> ok  (22462 ms)
```

Three consecutive failures, 190 seconds of wall clock, then **Revit was restarted and the model was
opened by hand** — there is no successful `opendoc` between 21:33 and the first `exportdxf` at 21:37.
**The DXF export that module 11 reports as a success was human-assisted.** Older command files on the
same machine (`…-reopen.json`, 2026-08-05/06) carry `"dialogAnswers": {"": 0}` — an explicit
"leave it alone" the caller had learned to pass. The 2026-08-15 caller did not pass it.

The fix is one line: move the text match outside the `TaskDialogShowingEventArgs` branch, or force
`answer = true` when it fires.

### 5.4 `newtag` returns `ok:true` for tags that are invisible on the sheet

`31202-01_…COMPLETION-REPORT.md` §6.2, disclosed by the machine itself: seven of ten tags were created
correctly associated with the right bars but **positioned at the view origin (-1219, 0), outside the
crop — invisible on the sheet and absent from a by-view query.** Re-creating them one at a time with a
pause placed them correctly. The report's own warning: *"a caller who trusts the `ok` + `reads`
response would ship invisible tags."* `[DOC]` **This is the most dangerous verb in the set** — it is a
silent-wrong-output failure on a drawing that gets sealed, and it is not in the smoke test's PASS
column for this behaviour (the smoke test asserted tag *existence*, not tag *position*). No guard has
been added to `NewTag` in `BridgeExec.cs` since `[READ]`.

### 5.5 The bridge resolves views by name only, and KOR's models have duplicate view names

`importinfo -> ERROR View name 'LEVEL 25' is ambiguous:` — the log names five candidate views
`[QUERIED]`. The grid-M audit found the same class at scale: *"this document has 20 duplicate
view-name groups covering 57 views … which are unreachable by a name query"* `[DOC]`. On a 45-storey
tower that is a meaningful blind spot, and it is why one section of that audit had to report "I could
not name their owner views."

### 5.6 Portability, credentials, and the single machine

- **Hardcoded root**, `BridgeApp.cs:34` — `@"C:\KOR.Drafter"` as the fallback when the env var is
  unset, and `:152` hardcodes `C:\KOR.Drafter\bridge\SENTRY-OFF` **absolutely**, ignoring the
  configured root entirely. A bridge installed at any other root has a kill-switch that cannot be
  reached at its own path `[READ]`.
- **`Dialog-Watchdog.ps1` exists only on KOR-302N** (`…\app\Dialog-Watchdog.ps1`) and is referenced by
  `PROTOCOL.md` and five process-record documents. It is not in the repo `[QUERIED]`. Unattended
  running on any other machine has nothing to clear an unnamed dialog — which §5.3 shows is not
  hypothetical.
- **A live SQL password sits in `STATE-2026-08-04.md`** — `transmittals_app / ‹REDACTED — the unmodified scaffold placeholder shipped by the project template›`,
  in a tracked file in this repo `[READ]`. Module 11 flagged the `standards_reader` credential in the
  *other* repo; this is a second one, in this one, and it reaches `KorTransmittals` on KOR-APP01.
  I did not use it.
- **21 empty catch blocks** in 3,489 LOC `[RUN]`. Two worth naming, both already noted by module 11
  and both genuinely low-risk given `setparams`' all-or-nothing contract: `BridgeExec.cs:2711`
  (`SetValueString` falls through to a raw `Set`) and `:2539` (`DeleteWarning` best-effort).
- **No test project.** Zero automated tests over code that edits structural models unattended
  `[RUN]`. The compensating control is real — the FINISH-PLAN five-step artefact gate, the 44/45 smoke
  test, an adversarial Codex audit whose findings are quoted in the source (`BridgeExec.cs:2478`) —
  and it caught genuine bugs. It did not catch §5.3.

### 5.7 The root of the repo cannot tell a reader what is current

Four status documents at the root plus two in `docs/`: `PROJECT-CONTROL.md` (2026-08-01, 39 KB, calls
itself *"the single source of truth"*), `RESUME-HERE.md` (opens `SUPERSEDED`), `START-NEW-SESSION.txt`
(2026-08-06, and by its own ordering rule the current one), `BRIDGE-READY.md` (2026-08-04),
`STATE-2026-08-04.md` (*"Supersedes STATE-2026-08-03"*), `FINISH-PLAN.md` (superseded by
`STATE-2026-08-04.md`'s own header). **Authority order, established by reading them against each
other and against live state:** `START-NEW-SESSION.txt` → `PLAN-2026-08-04.md` → the KorStandards
scoreboard → `STATE-2026-08-04.md`. Everything else is history. `PROJECT-CONTROL.md`'s own staleness
check, which tells the reader to compare its stamp against the newest bridge log, is the most useful
thing in it — and applying it retires §3/§4 of that file.

**`BRIDGE-READY.md` is factually wrong on the demo-relevant point.** It says *"KOR-302N's observed
active bridge was Revit 2025."* The 2026-08-15 log's first line is
`Bridge 1.0.31 up. Revit 2020 build 20200210_1400(x64)` `[QUERIED]`. Module 11 said 2020, the doc says
2025 — **the log says 2020, and both years' DLLs were updated that evening.** Seven Revit years are
installed (2020–2026, no 2027) `[QUERIED]`; the *active* one at the last session was 2020.

---

## 6. Economics — what it claims, and what it actually measured

`docs/ECONOMICS-BASELINE.md` is the ROI argument, and **it does not make the ROI argument.** It is
honest about this in its own subtitle — *"Auto-generated from `C:\KOR.Drafter\logs` on 302N. Feeds
DRILL-06"* — but the numbers get quoted as if they were productivity.

**What it claims** `[DOC]`: 69,599 bridge commands, 1,232 errors (1.8%), 11.6 h of Revit execution
time, 423 dialogs auto-handled, 98 Revit launches, over four days (2026-07-29 → 2026-08-01). Then the
reading: *"commands ≈ discrete drafting operations; a human drafter averages what, 1–2 model
operations a minute at best?"*

**Whether the evidence supports it — no, and the gap is large.** Summing the document's own per-verb
table `[RUN]`:

| | Commands | Share |
|---|---:|---:|
| **Read-only** (`query` 63,707 · `getparams` 1,911 · `opendoc` 444 · `closedoc` 323 · `ping` 150 · `exportlookup` 22 · `exportview` 20 · `exportsheets` 8) | **66,585** | **95.7%** |
| **Writes** (`setparams` 2,084 · `newnote` 628 · `placeview` 115 · `copyview` 50 · `newtag` 31 · `delete` 25 · `settext` 22 · `place` 14 · `newsheet` 11 · `importlookup` 9 · `ungroup` 6 · `regroup` 6 · `newview` 5 · `rename` 2 · `savedoc` 2 · `duplicateview` 2 · `placegroup` 1 · `formattext` 1) | **3,014** | **4.3%** |

So *"69,599 drafting operations"* is **3,014 drafting operations and 66,585 database reads** — and
`query` alone is 91.5% of the total. Worse, the error rate is not 1.8% where it counts: **754 of
2,084 `setparams` calls failed (36%)** and **371 of 628 `newnote` calls failed (59%)** `[RUN]`. The
1.8% headline is diluted by 63,707 near-perfect reads. A single day, 2026-07-30, contributes 61,336
of the 69,599 commands — that was the overnight fleet crawl, a **read-only census**, not drafting.

**The comparison to a drafter is a rhetorical question, not a measurement.** *"a human drafter
averages what, 1–2 model operations a minute at best?"* has no source, no observation, and no
denominator. There is no timing of a human doing the same task anywhere in the repo.

**The measurement that would settle it was designed and never run.** `TRAINING-CURRICULUM.md`
DRILL-06 — *"Instrument every completed drill/task: wall-clock, verb counts, dialog interventions,
token spend → $/task vs drafter-hours/task … produces the cost-per-sheet table for the management
pitch"* — is gated on *"after 2–3 more drills complete, so the sample isn't n=1."* Those drills ran
(02, 02b, 02c, 03, 04, 05 all have reports). **DRILL-06 does not**: no report among the 85 in
`process-record/reports/` `[RUN]`.

**Report it as:** the program has measured its own *throughput* and has **not** measured *savings*.
The one number that is both real and defensible is the exam's wall clock — one markup, two levels,
12 locations, 38 edits + 10 placements + 2 deletions, **from 18:23 to 21:59 including the tooling
being written underneath it** (bridge went 1.0.4 → 1.0.8 during the run) `[QUERIED]`. That is not a
productivity claim either, and it should not be dressed as one. **If MVE asks "what does it save?",
the honest answer today is "we have not measured that yet, and here is the instrument we designed to
measure it."** That answer is far better than a number that falls apart under one question.

---

## 7. Dependencies

| Dependency | Needed for | Reachable from MVE's office? |
|---|---|---|
| **KOR-302N** — the one workstation | Every live operation. `Dialog-Watchdog.ps1`, the seed `CLAUDE.md`, the exam evidence, the 123 MB result model and the template all exist **only** here | **LAN/VPN only.** Single point of failure for the whole program |
| **Autodesk Revit, licensed** (2020–2026 installed on 302N; **no 2027**) | The bridge is an in-process add-in | A demo machine would need Revit. Building does not — the API comes from NuGet `[RUN]` |
| **Claude Code + an Anthropic API/subscription** | **The drafter itself.** This is not a supporting dependency, it is the product | Works anywhere, but the *agent* must run where Revit is |
| `\\Kor-fs01\Projects\Projects` | Every source model, every markup PDF | **LAN/VPN only** |
| `\\Kor-fs01\Drafting\` (KOR-Deploy, 2028 Detail Library, templates) | Template sources, detail library, census scratch | **LAN/VPN only** |
| `KorStandards` on `KOR-APP01\SQLEXPRESS` | The `db/` migrations in this repo build it; the DXF→ETABS generator reads its `analysis` schema | **LAN/VPN only** (module 11/12) |
| `KorTransmittals` on `KOR-APP01\SQLEXPRESS` | Detail catalog / governance (module 12's territory) | **LAN/VPN only** |
| Bluebeam Revu | Markup authoring — engineer-side, unchanged | n/a |

**Nothing in this module is reachable from MVE's office.** No Graph, no SharePoint, no Deltek, no
HTTP service — but also no path to a live demo without a VPN into one specific PC. Plan on artefacts.

---

## 8. Test reality

**There is no test project.** Zero unit tests over 3,489 lines that edit structural models
unattended `[RUN]`. Stated that baldly it sounds worse than it is, and the honest account is more
interesting:

**What stands in for tests is a genuine and unusually rigorous evidence discipline.**
`VERB-SMOKE_2026-08-01.md` is the closest thing to a test suite and it is better than most: every
verb scored on its **product** (echoed id compared to requested id, a fresh query confirming the new
state, before/after counts confirming nothing else moved), never on `ok:true`. **44 PASS / 45**, one
genuine FAIL (`closedoc`), two skipped with reasons. Its guards were **made to fire** — the author
records that his first `swapgrouptype` guard test used two 10-member types, *"it passed while testing
nothing"*, and only a forced 10→1 mismatch proved the guard existed. Its LESSONS section records that
**eight of the nine failures were the harness's, not the subject's**, and that `closedoc` had been
silently failing for nine consecutive rounds masked by a `Stop-Revit` that followed it. That is a
better epistemic posture than most test suites have.

**What it does not do is run.** It is a document produced by an agent session on one evening in
August, not a command anyone can execute. It cannot regress. Nothing re-runs it when
`BridgeExec.cs` changes — and `BridgeExec.cs` gained 94 lines two weeks later with no re-run. **The
`newtag` origin-position bug (§5.4) and the dialog-branch bug (§5.3) are both exactly the class a
smoke test would catch if it were executable.** The evidence discipline is real; its
non-repeatability is the gap.

**Coverage of what matters:** the exam, five drills, a 44/45 verb sweep and a 194-model read-only
crawl cover the *behaviour* well and the *code* not at all. For a demo audience this is the right
trade. For a production gate it is not, and the repo's own ROADMAP C4 (*"production gate held until
proven"*) agrees.

---

## 9. Demo risk

Ranked by likelihood × damage in front of MVE's technical lead.

1. **Naming the production drafter, or letting the head-to-head framing surface at all.** `sacosti`
   is named in `EXAM-SCORECARD.md`, `answer-key.md` and `final-inventory.md`; Jim's ruling names him
   again. **A named KOR employee losing a head-to-head against software is an HR incident, not a
   demo asset** — and it is worse in front of an external partner, who will repeat it. The playbook
   PDF already solves this correctly (it says "a human drafter", "the human version", and I verified
   by text extraction that it contains **no employee name at all**). **Use the playbook. Do not open
   the exam folder.** See §7 below on the repo generally.
2. **Quoting the scorecard's "2 locations more faithful than production."** The engineer overturned
   one of the two on 2026-07-31 and also ruled against the machine on Level 13 scope (§5.1). If MVE's
   lead asks *"and what did your engineer say?"* — and that is the obvious question — the unprepared
   answer is a retraction on stage. Prepared, it is a strength.
3. **Quoting 69,599 operations, or any $/hour saving.** §6. The number is 95.7% database reads and
   the drafter comparison has no source. One follow-up question dismantles it.
4. **Attempting anything live.** The last recorded session failed its first three commands, needed
   a Revit restart and a human to open the model by hand (§5.3), and runs on Revit 2020 on a machine
   the demo would reach over VPN.
5. **"Show me it starting a new job."** It cannot (§4.3). There is no `.rte` and no verb.
6. **"Is this deterministic? What happens if you run it twice?"** The honest answer — it is an LLM
   agent, so no — is fine if given deliberately and terrible if discovered. Prepare it. The mitigation
   is genuine and should be given in the same breath: every write is re-read and verified, every
   command is logged, every batch is one Ctrl+Z, and ambiguity becomes a written question rather than
   a guess.
7. **"Can anyone but you run it?"** Bus factor 1, one machine, and the ROADMAP's own C1 says so
   `[DOC]`. Module 11 correctly notes the *RevitTools* continuity story is strong; **this program's
   is not**, and the two must not be conflated on stage.
8. **Looks-unfinished risk:** the repo root's six competing status documents (§5.7), one of which
   opens with the word `SUPERSEDED`.
9. **Client confidentiality:** the artefacts name **Hotel Circle North, San Diego** — a live KOR
   project in MVE's own back yard. Showing another client's reinforcing sheets to an architecture
   firm in the same market needs a deliberate decision, not an accident.

---

## 10. Confidentiality — what must not be on screen, and why

This is not a footnote. Three separate obligations, all breached by simply opening the repo:

1. **The repo is confidential from KOR's own drafting team.** `README.md` line 3: *"Do not publish,
   reference, or copy any part of this repo into KOR.RevitTools, the KOR-Deploy share, or anything
   drafters receive. This capability is Ian's; it exists on the dev box and the one designated Revit
   workstation, nowhere else."* The seed prompt repeats it as an **absolute** rule and adds:
   *"nothing lands on shared drafting folders … You are Ian's capability; he decides who sees the
   work and when."* `PROJECT-CONTROL.md:1` states the disclosure position outright: *"Confidential
   from the drafting team except as Ian discloses (public face = the Playbook PDF)."* **A document
   KOR's own drafters may not see should not be shown to an architecture partner.**
2. **A named employee benchmarked against software.** §9.1. `sacosti` appears in three exam files
   and in `RULINGS.md`; `EXAM-SCORECARD.md` has an entire section headed *"Where the exam wins."*
   ROADMAP C5 records that drafter disclosure has **not happened** — *"Framing decided: the
   interfacer role is a promotion, not a redundancy. Timing is Ian's — **OPEN**."* Showing this
   externally would pre-empt a conversation KOR has not yet had internally.
3. **Departed-employee material.** KOR-302N carries `michael li.old` and `mli` profiles `[QUERIED]`;
   `intake/…/FINDINGS.md` documents that the former BIM developer *"silently rewrote the standard
   notes"* and notes the register *"doubles as the missing changelog of what Michael actually
   altered (relevant to the **investigation file**)."* There is an open personnel matter in this
   repo. It must not leave the building.

**The safe list — verified, and short:** the playbook PDF (§3 row 1, text-scanned clean); the
before/after renders (row 2 — images of rebar, no names); the standards-drift story told from
`intake/…/FINDINGS.md` **with names removed**; and the fleet-crawl headline (194 models read
overnight, ~4,000 bars printing `NA`, catalogued and being fixed).

**The do-not-show list:** the repository itself, in any form; `exam/31202-01/` and everything in it;
`standards/RULINGS.md`; `PROJECT-CONTROL.md`; the 302N session transcripts; anything naming
`sacosti`, `Michael Li`, `GairWilliamson`, `Meiklejohn`, or `John Bryson`.

---

## 11. To-do register

| # | Item | Size | Tag | Why it matters |
|---|---|---|---|---|
| 1 | **Correct the exam claim wherever it is quoted** — scorecard, playbook, any deck. New line: *"12/12 implemented; of 3 contested points the engineer gave it 1."* | S | `BEFORE-DEMO` | The current claim is refuted by KOR's own ruling register. One question retracts it on stage |
| 2 | **Decide the confidentiality line and write it down** — one page listing what may be shown. Default: the playbook PDF and the two renders only | S | `BEFORE-DEMO` | §10. Three separate obligations, all breached by opening the repo |
| 3 | **Strike the "69,599 operations" and any $/hour figure** from every demo asset; replace with *"we have measured throughput, not savings — here is the instrument"* | S | `BEFORE-DEMO` | §6. 95.7% of it is database reads |
| 4 | **Prepare the three inevitable answers** — determinism, bus factor, "show me a new job" — as three written sentences, not improvised | S | `BEFORE-DEMO` | §9.5–9.7. All three are fine prepared and bad discovered |
| 5 | **Get client sign-off, or anonymise, before showing Hotel Circle sheets** to a SoCal architecture firm | S | `BEFORE-DEMO` | §9.9 |
| 6 | **Back the exam evidence off KOR-302N** — 4 PNGs, the 7.2 MB transcript, the plotted PDF, ideally the 123 MB model | S | `SOON` | The single most valuable artefact in the program exists in one place, on one workstation |
| 7 | **Move the "copied or moved" text match out of the `TaskDialogShowingEventArgs` branch** and force `answer = true` when it fires | S | `SOON` | §5.3. One line. Currently costs ~60 s per unattended open and needs a human |
| 8 | **Rebuild `artifacts/` and bump the version** so a fresh install gets `exportdxf` and the dialog fix, and so two builds stop sharing a version string | S | `SOON` | §5.2. Following the repo's own deploy instructions installs a stale bridge |
| 9 | **Guard `newtag`** — verify head position is inside the view crop before returning `ok`, or serialise tag creation | M | `SOON` | §5.4. Silent wrong output on a sealed drawing is the worst failure class here |
| 10 | **Make the smoke test executable** — a script that replays the 45 assertions against a sandbox and reports pass/fail | L | `SOON` | §8. The discipline exists; it just cannot regress. Both §5.3 and §5.4 are exactly what it would catch |
| 11 | **Run DRILL-06** — instrument the completed drills for wall-clock and $/task against drafter-hours | M | `SOON` | §6. This is the ROI argument, designed and never run |
| 12 | **Close job instantiation** — save the template as `.rte`, parameterise the title block, start one job from it | M | `SOON` | §4.3. Turns "a library" into "how a job begins" |
| 13 | **Rotate the `transmittals_app` password** and scrub it from `docs/STATE-2026-08-04.md` | S | `SOON` | §5.6. A live production credential in a tracked file |
| 14 | **Retire the root status documents** to one file plus an `_archive/` folder | S | `SOON` | §5.7. Six documents, one opening with `SUPERSEDED` |
| 15 | **Fix `BridgeApp.cs:152`** to honour the configured root for `SENTRY-OFF` | S | `LATER` | §5.6. A kill-switch that only works at one path |
| 16 | **Resolve views by id as well as name** | M | `LATER` | §5.5. 57 views unreachable in one tower |
| 17 | **Copy `Dialog-Watchdog.ps1` into the repo** | S | `LATER` | §5.6. It exists on exactly one machine and PROTOCOL depends on it |
| 18 | **Second operator, second machine** | L | `LATER` | ROADMAP C1, open by Ian's decision. ~1 hr of install; the constraint is a person |

---

## 12. Verdict

**Demo-able with care — as artefacts and a story, never live, and never by opening this repository.**
The Virtual Drafter is real and is the most genuinely novel thing in the suite: an LLM agent driving
Revit through a purpose-built 42-verb bridge, which sat a closed-book head-to-head against a
production drafter on a real markup and implemented 12 of 12 marked locations with every write
verified by re-read. **I verified the closed-book condition forensically** — the exam session's
8.5 MB transcript contains zero references to the answer key, zero to the production drafter, 55 to
the pre-revision snapshot, and its only two mentions of the post-revision model are a single directory
listing. That is not a curated demo, and the honest disclosure inside the exam's own reports (a
harness bug that made one pass report work it had not done) is stronger evidence of integrity than
the score is.

**But the headline as currently written is wrong, and that is the single most important thing to
fix.** The scorecard claims two locations where the machine beat production; Jim DesRoches ruled on
2026-07-31 and gave the machine **one** of them, gave production the other, and ruled against the
machine on Level 13 scope as well. That ruling is in KOR's own `standards/RULINGS.md`. Correct the
claim to *"12 of 12 implemented; of three contested points the engineer gave it one — including a
`#6`-vs-`#5` error in issued production work"* and it is still a genuinely compelling result, and one
that survives the follow-up question. Leave it as-is and the first sharp question retracts it.

Everything else is secondary but real: the ROI argument has not been measured (the instrument was
designed and never run, and the 69,599-command figure is 95.7% database reads); job instantiation
does not work; the shipped bridge binaries are two weeks stale; and the whole program is one person
on one workstation. **And it must not be shown as a repository** — it is confidential from KOR's own
drafting team by its own README, it names a production drafter who has not been told, and it touches
an open personnel matter. The correct demo is the playbook PDF, two before/after renders, and the
sentence *"every command it issued is in this file."*
