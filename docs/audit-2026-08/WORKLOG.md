# Work log — executing the August 2026 audit

**Authority: `04-TODO-REGISTER.md` is the single arbiter of *state*.** Its `status` column says
whether an item is done. This file is the append-only *evidence trail* — what was briefed, what
changed, how it was checked. If the two ever disagree, the register wins and this file is wrong.

Do not restate an item's text here. Reference it by number.

## How a piece moves

    open  →  briefed <N>  →  applied <N>  →  verified <date>

- **open** — in the register, untouched.
- **briefed N** — handed to Codex in `codex/BRIEF-N-*.md`. Codex is asked to *verify the finding
  first* and to say so if it does not hold.
- **applied N** — Codex reports the edit landed. **Not** yet trustworthy.
- **verified <date>** — checked here on the dev box against the artifact, not against Codex's
  report. Evidence recorded below. Only then does the register status change.

A finding Codex overturns is recorded as `not-an-issue` with its reasoning **and** an independent
check, per the audit's own calibration note: the defect findings held, the absence findings did not.

## Status vocabulary in the register

| value | means |
|---|---|
| `open` | untouched |
| `briefed N` | out with Codex under brief N |
| `applied N` | Codex says done, unverified here |
| `verified YYYY-MM-DD` | checked on the dev box; evidence in this file |
| `not-an-issue` | the finding did not hold; see the entry here |
| `superseded` | folded into another item; names which |

---

## Log

*(newest last)*

### 2026-08-21 · Brief 1 — "Client present" mode for the BD surface
`codex/BRIEF-1-client-present-mode.md` · items **1, 2, 18, 22, 23** → `briefed 1`

Verified all five findings against live source before briefing:

| item | check | result |
|---|---|---|
| 1 | `DashboardView.xaml:589,659` | held — `DisplacementRead` on Open Structural Seats, `CapacityRead` on Competitor Watch, both on the landing view |
| 2 | `OrgDossierView.xaml:618` | held — register says `:610`, it is at `:618` |
| 2 | `CompetitionInfoView.xaml:125,144` | held — exact lines |
| 18 | `PursuitBriefViewModel.cs:125,129` | held — exact lines, verbatim string |
| 22 | `BdWorkspaceWindow.xaml:96` | held — `AttributionButton`, "BD Scorecard" |
| 23 | `CompetitionInfoSourcesWindow.xaml:114` | held — register says `:106`, the `Coming next` TextBlock is at `:114` |

**Decisions taken as head, which the register left open:**

- Item 1 offered *land elsewhere* **or** *suppress the columns*. Chose **suppress**. Changing the
  landing view moves everyone's muscle memory to solve a problem that only exists when a client is
  in the room; the two columns are the entire disclosure.
- Item 2 says "behind a demo flag". **No feature-flag mechanism exists in this app** — grepped
  `DemoMode|IsDemo|KOR_DEMO|FeatureFlag` across `Kor.Operations.App`, zero hits. So brief 1 creates
  one: a single `Demo.ClientPresent` App.config key. One switch covers all six gates and reverses
  after the demo by editing one value.
- Item 18 offered *replace* or *delete the card*. Chose **replace with `—`**: the card has a shape
  worth keeping and an em dash is an honest empty state that survives the PDF export.

**One gate added that the audit missed.** Module 08 RISK 5 notes that double-clicking a name in the
Awards **Winner** column opens `CompetitorProfileWindow`, which chips `NORR ARCHITECTS & ENGINEERS
LIMITED` and `ROBSON DESIGN BUILD LTD.` as **DIRECT COMPETITOR** and lists their named executives.
Item 2 only hides the two Awards *columns* — the drill-down still opens. Read
`CompetitorProfileWindow.xaml:52-72`: the whole AI panel is a single `Border` with one `Visibility`,
so gating it is one more edit. Added as §3.5 of the brief. **If this holds, item 2's own description
is incomplete** — amend it when the work is verified, do not silently widen it.

### 2026-08-21 · Ratchet #2 run for the first time — two findings, one of them in the audit itself

Ran the `START-HERE.md` §6 secret scan across `.cs .config .json .xml .ps1 .md` before the audit's
first commit, because §7 asserted the markdown was redacted and safe and rule 5 says verify the
artifact rather than the claim about it.

1. **`modules/02-transmittals-tracking.md:259` quoted the redirector's live Entra client secret
   verbatim** in a fenced code block. Redacted before staging; it never entered history. `START-HERE`
   §7 now carries the correction rather than a silent edit.
2. **The same secret is committed in 24 tracked `.ps1` files** under `_Scripts Rebuild/` `[RUN]`.
   The audit's own credential table recorded that credential as *not* in git because the redirector's
   directory is untracked. The directory is untracked; the credential is not. `02-CROSS-CUTTING-SCAN.md`
   now carries a third correction, and item 59 carries the rotation side-effect.

Neither of these is a new class — both are the *same* class the file already retracted twice: the
scan's file-type coverage, one extension further out each time. `.cs` → `.config` → `.ps1`.

**Not doing now, and why:** purging history. It stays `SOON` for the reason the register already
gives — a history rewrite two weeks before a demo is the wrong order of operations — and the new
count changes the scope of that job, not its timing.

### 2026-08-21 · Brief 2 — error honesty
`codex/BRIEF-2-error-honesty.md` · items **14, 16, 17, 19** → **drafted, NOT issued.** Status stays
`open`. One brief is in flight at a time; brief 2 goes out only after brief 1 is verified.

All four held. `PursuitBriefWindow.Approach.cs:65-77`, `HomeWindow.xaml.cs` bare catch (7 hosts
forced `Visible`, two of them Financials and Compensation), `OrgDossierViewModel.cs:501,556` +
`OrgDossierView.xaml:925`, `EmailSearchWindow.xaml.cs:412`. Item 14's 4-minute figure is exact —
`AppAiService.cs:31`.

**Two corrections to the register, both found while verifying:**

- **Item 14 is two instances, not one.** `Controls/AiQueryPanel.xaml.cs:111` has the same shape and
  additionally appends the error string to `_history` as an `assistant` turn, so a failed call
  poisons every later question in that panel. Added to the brief.
- **Item 17's "nine status-line sites" is 72** `[RUN]` — `BdReportsViewModel.cs` alone has 11.
  Item 17 stays `S` by scoping to the org dossier, which is the demo path; the sweep is new item 183.

**Decision taken as head:** the register proposes matching `AppAiService`'s three error *prefixes* at
the call sites. Rejected — that leaves failure representable as success and makes the fourth prefix a
new bug. Briefed the cause instead: change the return so a caller cannot read an error as an answer.
`AppAiService`/`IAppAiService` have no consumers outside `Kor.Operations.App`, so it is three files.

### 2026-08-21 · Brief 1 VERIFIED — items 1, 2, 18, 22, 23 → `verified 2026-08-21`

Codex reported all eight edits `held`. Checked here:

- **Build green**, 0 errors. The 8 warnings are pre-existing and in unrelated files.
- **Diff correct.** Four gates use `{x:Static app:DemoClientPresentMode.InternalOnlyVisibility}`
  directly; the two that had an existing `Visibility` binding (`OrgDossierView` displacement panel,
  `CompetitorProfileWindow` AI panel) were rewritten as a `Style` whose base `Setter` is the flag and
  whose `DataTrigger` re-collapses on the data condition. `SectionCard` is `TargetType="Border"`, so
  the `BasedOn` is valid.
- **Gate mechanism proven, not assumed.** `{x:Static}` on `DataGridColumn.Visibility` resolves —
  which `{Binding}` would not, since the column is outside the visual tree. Truth table across all
  four quadrants passes: with the flag off the panels still follow their data condition; with it on
  they collapse regardless.
- **App launches with the flag on.**

**Harness note worth keeping:** the first truth-table run reported a false failure. A detached
element plus `Measure()` does not evaluate `DataTrigger`s — the trigger only fires once the element
is in a real window. The code was correct and the test was wrong. Anything checking WPF triggers
must put the element in a `Window` and pump to `DispatcherPriority.Loaded`.

**Not done, and it is Ian's anyway:** seeing the gated columns absent on the real dashboard. The
screenshot attempt caught the wrong window. Items 39 and 44 are the rehearsal that covers this, and
they are his.

**Item 2 amended, not silently widened** — it now records that the `CompetitorProfileWindow` panel is
part of the gate.

