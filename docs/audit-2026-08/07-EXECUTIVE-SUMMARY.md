# The verdict

**The engineering is better than the operations around it.** Across ~464,000 hand-written lines and
fourteen modules, the code is largely sound — several modules carry zero TODOs, zero
`NotImplementedException` and zero empty catch blocks, which is rare at any size. What is weak is
everything *around* the code: what gets deployed, where credentials live, whether the tests check
the thing that matters, and whether the documentation is true. **Every systemic finding below is a
process fix, not a rewrite.**

**One pattern explains most of the risk: the system reports success it has not earned.** Sixteen
instances across eight modules, and it kept appearing as the audit widened. The AI answered
financial questions for 34 days from stale binaries while `/health` returned 200. FileSync shows a
live countdown for a job the scheduler was never told about. The BD workspace paints *"Unable to
reach AI service"* into a card headed *"Drafted from live intel"* in green. DemoStudio's redaction
processor copies the raw file and returns `Succeeded: true`. Most instances are one-line fixes.

**On competition: you are not behind, but the ground moved under three of your four pillars in the
last twelve months.** Conversational ERP analytics stopped being a differentiator in February. An
Outlook filing add-in with download telemetry shipped from a vendor in May. What remains genuinely
rare — verified across three search engines and every structural vendor — is **drawings in,
analysis model out.** Nobody is building it.

**Cost:** having this built professionally lands at **≈ $5.7M central** (range $1.2M–$16.9M).
Licensing the nearest commercial equivalents costs **~$650k over five years** for 40 users.

---

# Module scorecard

| # | Module | Verdict | Working | The one thing |
|---|---|---|---|---|
| 1 | **Email filing & search** | **Demo-ready** — open with it | 12/16 | 39% of last month's filenames carry a `4501-01-01` prefix |
| 2 | **Transmittals & tracking** | Demo-able with care | 10/18 | Hasn't compiled since March; not in git |
| 3 | **FileSync** | Demo-able with care | 10/15 | Public map 8 days stale; UI counts down a job that never runs |
| 4 | **Financials & Deltek** | Demo-able — strongest data asset | 8/15 | Summary ledgers stop at **February 2026** |
| 5 | **AI / virtual CFO** | Demo-ready **after a redeploy** | 19/23 | Serving wrong numbers today from 34-day-old DLLs |
| 6 | **PM Tools & analytics** | Demo-able with care | 9/11 | Employee Summary must stay off screen |
| 7 | **BD Brain core** | Demo-able with care | 6/14 | AI research layer dead ~2 months, reporting success |
| 8 | **BD desktop surface** | **Change the default screen first** | 18/27 | Default view leaks competitor strategy at load |
| 9 | **Engineering tools** | Demo-able with care | 12/18 | The best tool is CLI-only, stranded outside the app |
| 10 | **DXF → ETABS** | Demo-able — best engineering | 8/15 | Test suite red at HEAD (3 stale tests, not a broken generator) |
| 11 | **Revit tools / Drafter** | RevitTools ready · Drafter off screen | 6/10 | Revit→DXF→ETABS does **not** close |
| 12 | **Standards centralisation** | One live click-path, two static screens | 8/11 sheets | All 12 governed masters are unopenable |
| 13 | **Virtual Drafter** | Artefacts and a story — never live | 7/13 | Exam is real; the scorecard's win count is not |
| 14 | **ETABS plugins · DemoStudio** | Plugins yes (needs ETABS 23) · Studio off screen | 17/19 · — | Plugins are the missing half of DXF→ETABS |

---

# The five systemic findings

### 1 · The system reports success it has not earned

*16 instances, 8 modules.* `AppAiService` returns its own error text as the answer. FileSync reports
`Shadow` while all seven jobs run `Live`. `WipFinancialsService` returns `DataLoaded: true` after
catching a failure. The MCP smoke gate died on 9 June while `/health` kept returning 200. BD
research executors have logged `Success=1; considered=0` daily for two months. Three Revit sheet
commands swallow a lost viewport and report success. **DemoStudio's redaction returns
`Succeeded: true` after copying the unredacted file.** Nothing tells you when it stops working.

### 2 · Deployed artifacts cannot be traced to source

*7 modules.* The MCP service's stamped version disagrees with its own binary contents. FileSync's
executable names a commit whose tree lacks a job it was running. The redirector has not compiled
since **17 March**. The ETABS plugins are worse in an instructive way: **the git-tracked source no
longer builds, and the code actually shipping to the firm — a fork rebuilt in March for ETABS 23 —
has no `.git` at all.** A fourth shipped ETABS plugin has no source anywhere on the machine.

### 3 · One computation, two wirings — and no arbiter

*17 instances.* MCP and the desktop app construct `ProjectAnalyticsService` differently, so employee
scores and the at-risk watchlist diverge. Two FX regimes, $35k apart. The CRM win rate contradicts
the app's own metric dictionary. "Client Lifetime Fee" computes three ways. **The sharpest form:
three parity claims are written into the MCP system prompt, so the model asserts an agreement that
does not exist.**

### 4 · Verification points away from the risk

Zero tests on FileSync (6,919 lines), the redirector, the Drafter bridge, `AskService`, 34,000 lines
of BD UI — and **14,400 lines of ETABS plugin code that mutates live structural models**, which also
has two `catch` blocks and 94 unguarded `Convert.ToDouble(textBox.Text)` calls. Gates that cannot
fail: 32 of 35 rule keys checked, the one-pager gate parsing HTML instead of the shipped PDF.

### 5 · The written record is wrong, and some of it is machine-read

`AGENTS.md` is **0 for 3** on its warnings. `PROTOCOL.md` documents 29 verbs against 56 dispatched.
Tool counts read 28, 79, and actually 145. `BRIDGE-READY.md` names the wrong Revit version.
`cPlugin.Info` still reads *"Version 0.9 / December 3, 2020 / Adrian Crowder."* **47% of `docs/` is
more than 60 days old.**

---

# Fix these first

Ordered by risk × cheapness. **S** ≤2h · **M** ≤1 day.

| | Action | Size | Who | Why |
|---|---|---|---|---|
| 1 | Land BD workspace off Dashboard; gate 4 XAML bindings | S | either | Only item that could end the meeting |
| 2 | Redeploy MCP from HEAD incl. `Business.dll`; verify the **binary**, not `/health` | S | **Ian** | `get_wip` and `get_cash_position` are wrong today |
| 3 | Relink the 11 quarantined standard-detail masters | S | **Ian** | One `UPDATE`; turns 12 dead Open buttons into working ones |
| 4 | Three values that make the AI agree with the screen | S×3 | **Ian** | `BilledDefaultOrg`, `EmployeeSummaryExcludedIds`, peer estimator |
| 5 | Put the redirector in git; fix its 5 build errors | S+S | either | Also the honest answer to "what happens when you leave?" |
| 6 | Strip `BUILTIN\Users` ACE on `appsettings.Production.json` | S | **Ian** | Any domain account reads Deltek + Anthropic keys |
| 7 | Delete the plaintext secrets script from the Library share | S | **Ian** | Two Entra secrets + Anthropic key, staff-readable |
| 8 | Correct the Virtual Drafter exam claim everywhere it is quoted | S | either | Your own ruling register says 1 win, 2 losses |
| 9 | Rotate the SAM.gov API key | S | **Ian** | Only US federal source, dead since 1 August |
| 10 | Fix the `4501-01-01` filename guard | S | either | 39% of last month's filed emails |
| 11 | Fix `Process.Start` in a `catch` at `EmailSearchWindow:412` | S | either | Clicking **Open** off-VPN crashes the app |
| 12 | Fix the AD fail-open — 7 surfaces exposed | S | either | Off-LAN is exactly your situation in California |
| 13 | Hide the two "Coming in Phase 2" ETABS tabs | S | either | Still in the 2026 production build |
| 14 | Re-render the DXF one-pager; verify with `pdftotext` | S | either | It ships an Edge 404 into client job folders |

**Deliberately not before the demo:** rotating the committed SQL passwords (breaks filing for all 40
staff — the VSTO add-in has no override path); the `DeltekClientId` backfill; BD ingest dedup;
merging `feature/details-palette`; unblocking the details palette (five independent blockers).

---

# The competitive answer

### Where you are genuinely ahead

**Transmittal evidence.** 829 transmittals, 4,284 per-recipient links, 741 external addresses,
**2,682 click events with zero null IP, user-agent or email**, 88% open rate. Newforma's cloud
product logs a download *count* and expires links after two weeks. **Deltek PIM has no transmittals
module at all.**

**Per-bookmark issue annotation.** For a Site Instruction, the tool reads the bookmark outline out
of the attached PDF and lets the engineer write a note against **each individual bookmark** — and
those notes print on the cover sheet the client receives. General document tools move files; this
annotates the issue, item by item.

**Corpus depth.** 372,370 emails across 955 projects back to October 2014. `seismic review` returns
**7,216 hits, sub-second.**

**Drawings to analysis model — and the second half nobody had joined up.** DXF→ETABS generates the
geometry; the **Kor Tools ETABS plugin** does materials, sections, cracking modifiers, load
combinations, pier labels and Excel export. Paired, that is drawing in → model defined and ready to
analyse. CSI ships zero AI and calls its own DXF import *"a template to trace over."*

### Where you are behind — concede on sight

No two-way RFI workflow: **you issue, annotate and prove delivery — you do not track the reply.**
No AI filing suggestion. Search is keyword, not semantic. No link expiry or reminders. Transmittal
"numbering" is a UTC timestamp, not a sequence. Filed email is `.msg` on FS01 — **not**
SharePoint-native; only transmittals are.

### The three dangerous objections

**"What happens when you leave?"** — Most dangerous; no cost table answers it. Concede entirely.
Note that "it's all in source control" is *currently false* until the redirector and the live ETABS
fork are committed.

**"Egnyte does this now."** — True since 13 May 2026 at $10–48/user/month. Three answers survive: it
requires migrating **off** SharePoint, it is per seat, and it has no numbered transmittal register.

**"We'd build it in Power BI."** — Cheap (~$8,600/yr) and must be conceded. It breaks on Microsoft's
own documentation: the ODBC connector is **Import only, no DirectQuery**; M365 Copilot *"isn't
available on archive, group, shared or delegate mailboxes"*; the Power Platform SQL connector has
**no GROUP BY and no joins**; and Microsoft warns you may want to *"consider advising users not to
use Copilot to consume your semantic model."*

### What the vendors are doing

Deltek's own roadmap puts **Vantagepoint agentic features in 2027** — last in their queue. Ask Dela
is GA and free, and Deltek's help states it *"cannot query for aggregate data."* Newforma put all
2026 AI investment into Konekt; Project Center 2026.2 shipped **no AI**. The real threats are
**Deltek PIM** (one-click Outlook filing) and **Bluebeam Max Smart Overlay** (change detection, in
preview, inside software MVE may already own). **Ask about both, early and neutrally.**

---

# The demo

**Running order.** Ask five questions first. Then: **transmittals + telemetry** (PIM has no
equivalent, read-only), **email search** (concede filing, win on corpus), **DXF→ETABS paired with
the Kor Tools ETABS plugin** (rarest capability, and now a complete story), **detail-sheet
composition in Revit** — the only live click-path that needs **no VPN, no SQL, no share** — **rebar
delta** (needs no network), and **financials + AI last and short.** **Omit the BD Brain.**

**Do not put on screen:** BD Workspace Dashboard · BD Scorecard · `CompetitionInfoSourcesWindow` ·
Employee Summary · `get_wip` and `get_cash_position` · KOR.Drafter repo · `CompetitorProfileWindow`
· any SoCal architect dossier · Compensation tile · the two "Coming in Phase 2" ETABS tabs · the
Hotel Circle North (San Diego) exam sheets · **and the Operations repo itself**, which contains
`bd-dossier-mve-mclarand-2026-06-17.md`.

**Why the BD module is omitted.** MVE is in your database as a pursuit target — `CanonicalOrg`
76952, **18 contacts with real @mve-architects.com addresses** including their President, 12 of
their projects with incumbent structural engineers named, ranked **#14 on a report subtitled "Warm-
intro priority list."** The default BD screen renders **25 named architecture firms beside your
written displacement strategy** at load, with no click. Nothing derogatory about MVE is stored.

**Pre-flight.** VPN up **before** launching. Confirm **ETABS 23** is installed — it is not on the
dev workstation. Seed favourites. Clear the FileSync watcher failure. **Do not change
`Mcp:AnthropicModel`** — `temperature = 0` returns 400 on every 5-series model.

**A fallback you already own.** DemoStudio records, composes and exports a demo — verified today
producing a 26.3-second 1920×1200 h264 file with thumbnail, tutorial and package. Two defects stand
in the way, both hours: a bad `FfmpegPath` blocks startup, and publish throws after writing the
package correctly. It runs **with Wi-Fi off**. Record in Desktop mode on one monitor.
**Do not rely on its redaction — it is a stub that copies the raw file and reports success.**

---

# Only you can do these

1. **Test Ask Dela in your own tenant and screenshot it.** A Deltek article dated 5 August claims an
   aggregate query their help page says is impossible.
2. **Ask MVE whether they run Deltek PIM, and whether they're on Bluebeam Max.**
3. **Decide on org 76952** — suppressing MVE's own record needs a data write.
4. **Ask Daler why GL posting stopped after February.** One conversation removes the largest data
   risk in the demo.
5. **Get client sign-off or anonymise the Hotel Circle North sheets** before any San Diego work is
   shown to a SoCal firm.

---

# What you actually built

In eight months, largely alone: a **Newforma replacement with better transmittal evidence than
Newforma's current cloud product** and a per-bookmark annotation workflow no document tool has; an
**eleven-year searchable email corpus**; a **conversational financial layer with 23 typed tools
where the vendor's own AI cannot answer an aggregate question**; a BD pipeline over 111 sources with
a canonical org table of **9,641 rows and zero duplicates**; a **DXF→ETABS generator with 483 tests
that builds a 63-storey building in 50.7 seconds**, paired with an ETABS plugin suite that finishes
the model; a **145-command Revit ribbon across six Revit versions** including sheet composition
built well enough to measure true viewport footprints and never strand a viewport on a temporary
number; **612 canonical details and 29 adjudicated rulings** in a standards database; and a Revit
bridge an AI drives, tested closed-book against a human drafter on a real job.

Professionally quoted, that is **≈ $5.7M** of software. The BD dedup code cites its own audit IDs in
its comments. This is a codebase maintained with discipline, not a prototype.

---

# What this audit got wrong

Stated for calibration, because it tells you which half to trust.

**My negative findings were unreliable. My positive findings held.** Four times I reported a
capability absent and was wrong every time — the transmittal type system, per-bookmark annotation,
custom detail-sheet creation, and the Virtual Drafter as a whole. Each failure was the same
mechanism: **I searched for a phrase instead of a capability.** `grep "Transmittal"` missed the
transmittal window because it is called `MainWindow`. `grep "detail sheet"` missed sheet composition
because the buttons are named for verbs. **Treat every "KOR is behind on X" as a prompt to check.
Treat every "this is broken at file:line" as reliable** — those survived challenge, and two agents
overturned their own first answers rather than defending them.

**My mechanical scans were the weakest evidence produced.** I reported "no hardcoded credentials"
from a scan that read only `.cs` files — there are **nine** credential locations. Empty-catch counts
were ~4× low. The hardcoded-path finding ran about 1 in 8 true.

**Three briefing assumptions were wrong and the agents caught them:** FileSync's UI/service gap was
not format drift (431 of 431 log lines parse); PM Tools is not a second AI service; the BD module is
not a third analytics implementation. **And Revit→DXF→ETABS, which I flagged as potentially your
strongest demo, does not close** — the bridge exports `A-WALL`/`S-COLS`/`A-FLOR` and the generator
matches `WALL`/`_COL`/`SLABEDG`. The fix is a Revit export-layer table plus three settings.

**The cost estimate was rebuilt four times**, each round starting from "this is too small." Thirteen
newly-priced estates later the central case moved 5%. That stability is the argument for the number;
its weakest input, stated in the report itself, is that **no published hours-per-screen benchmark
exists anywhere.**
