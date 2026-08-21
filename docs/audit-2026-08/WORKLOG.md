# Work log — executing the August 2026 audit

**Authority: `04-TODO-REGISTER.md` is the single arbiter of *state*.** Its `status` column says
whether an item is done. This file is the append-only *evidence trail* — what was briefed, what
changed, how it was checked. If the two ever disagree, the register wins and this file is wrong.

Do not restate an item's text here. Reference it by number.

## WHERE THIS STANDS — 2026-08-21, end of session

**Read this, then `04-TODO-REGISTER.md`. Everything below is recorded there too; this is the résumé.**

### In flight right now
- **Brief 4** (`codex/BRIEF-4-failure-honesty.md`) — items **14, 16, 17** are with Codex. When it
  reports: build, read the diff, then **revert each named production line and confirm its gate
  fails**. Only then does the register move.

### Shipped to production today
| what | where | evidence |
|---|---|---|
| MCP from HEAD (items 3, 4) | KOR-APP01 | `SplitWipNet` 0 → 1 in the deployed DLL; `/health` reports commit `4947d977` |
| MCP config (items 5, 6) | KOR-APP01 | `BilledDefaultOrg` `""` → `CAD`; `EmployeeSummaryExcludedIds` absent → `[IANLALONDE,DALERSINGH]` |

### Fixed in git, NOT yet in front of anyone
This is the gap that matters most when picking this up again.

- **FileSync service** — live binary is `2026-08-12`. Items 10 and 12's service-side fixes are
  committed and tested but **not deployed**, so `KorMapSync` still never fires and the public map is
  still stale. → **item 183**
- **App + VSTO add-in** — items 13, 18, 19 and the App halves of 11 and 12 are committed and not
  published. Item 13 is the live one: ~40 staff keep filing `4501-01-01` filenames until the signed
  add-in ships. → **item 184**, scheduled for the evening of 2026-08-21.

### Known-imperfect, deliberately
- **Item 11 is not gated.** Its test passes with the bug reinstated. The fix is right; the test does
  not lock it. → **item 186**
- **Items 1, 2, 22** are `open — demo staging, not a defect`. The "client present" switch built for
  them was reverted: it was a feature nobody asked for. Do not rebuild it without being asked.
- **Item 36 cannot be closed by config.** The MCP never consumes `UsdToCadRateByYear`. → **item 188**

### The rule that earned its keep
Run every new gate against the **pre-fix** code. Three of four did fail correctly; the fourth did not
and is recorded as ungated rather than counted. A gate that passes either way is the same defect this
audit's systemic finding #1 describes.

---

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

### 2026-08-21 · Brief 1 partly REVERTED — the switch was a feature nobody asked for

`9d4a95ed` reverted except the two permanent copy fixes. Build green, 0 errors.

**What went wrong, so it does not recur.** Item 2 said *"gate behind a demo flag"*. The app had no
flag mechanism, so I invented one — a config key, a new class, six gates — and shipped it as the
first batch of audit work. That is not fixing a defect. It is adding a feature to someone's product
on the strength of a demo I never confirmed was happening, because the audit is written top to
bottom around one and I inherited the framing without checking it.

**The owner's actual goal: the app runs smoothly without issues.** That re-ranks this register.
`BEFORE-DEMO` is ordered by *risk to a meeting*. It is not ordered by *what is broken for the people
using this every day*, and those are different lists.

- **Reverted** — items **1, 2, 22** back to `open`, retagged *demo staging, not a defect*. Nothing
  about them is wrong; they are just not what "running smoothly" means. `DemoClientPresentMode.cs`
  deleted, `Demo.ClientPresent` removed from `App.config` and `AppConfigKeys`, all five gated XAML
  files restored.
- **Kept** — items **18** and **23** stay `verified`. *"coming with the AI Crucible."* rendered in
  every Pursuit Brief and exported into the client PDF, and the "Coming next" panel advertised three
  unbuilt features. Both are wrong whether or not anyone ever demos this.

**New ranking rule for this register:** an item earns priority by what it does to real users on a
normal day, not by what it would do to an audience. Item 13 — 39% of filed emails carrying a
`4501-01-01` prefix — outranks every disclosure item on that basis.

### 2026-08-21 · Brief 2 VERIFIED — items 13, 19

Both held. Codex added `EmailFiler/EmailFilerv2/OutlookDateGuard.cs` and wired it into
`EmailFilerv2.csproj` — required, since that is an old-style project with explicit
`<Compile Include>`.

- **Build.** `Kor.Operations.App` green via `dotnet build`, 0 errors. `EmailFilerv2` green via
  MSBuild (`Program Files\Microsoft Visual Studio\18\Community`) — it is .NET Framework VSTO, so
  `dotnet build` is the wrong tool for it.
- **Artifact checked, not just source.** `OutlookDateGuard` is present in the rebuilt
  `EmailFilerv2.dll` (11:55), same byte-scan result as a known-present type. Per this repo's rule 5,
  the check is the shipped binary rather than the diff that was supposed to produce it.
- **13** — `EmailFilerRibbon.cs:794` now calls `GetPlausibleSentOnOrNow`. Range is 1990-01-01 to
  `Now.AddDays(1)`; the firm's email corpus starts 2014, so the lower bound has clearance.
- **13, second path** — Codex checked `ItemsToFileProcessor` as asked and reported the filename path
  did *not* share the defect (it already used `DateTime.Now`, `:570`), but that `ToUtcOrNull:774`
  shared the weak validation. Fixed there too. Verified the return contract did not change: it was
  already `DateTime?` and already returned null, and both callers (`:664,:665`) already assign to
  `DateTime?`. No new null path.
- **19** — the fallback `Process.Start` now has its own try/catch and names the file in the message.
  The first call gets an early `return` so success no longer falls into the fallback. Codex confirmed
  those are the only two `Process.Start` calls in that file.

**Open, and not ours:** item 13 ships inside the VSTO add-in. Until it is rebuilt and republished
signed with Ian's certificate, all ~40 staff keep filing with the old binary. Register status for 13
is `code verified — awaiting signed VSTO republish` rather than `verified`, because saying otherwise
would be the exact defect this audit's systemic finding #1 is about.

**Not done:** nothing renames the 872 already-misfiled emails. Deliberately out of scope — that is a
decision about client folders, not a code change.

### 2026-08-21 · Brief 3 — FileSync truth
`codex/BRIEF-3-filesync-truth.md` · items **10, 11, 12** → `briefed 3`

All three verified against live source before briefing. First brief to require the fix **and** its
gate in the same change, per the repo's own rule that checks go in the build.

**Nearly repeated the audit's own worst mistake.** `QuartzInstaller` registers 5 runners while the
assembly has 8 `IJobRunner` implementations, which reads as three unregistered jobs. It is one.
`WatcherSyncRunner` is driven by `WatcherHostedService` (`Program.cs:91`) and `NoOpJobRunner` is a
stub. Claiming those two were broken would have been an absence finding from a grep — the exact
class of error `START-HERE.md` §2 says was wrong 4 times out of 4. The brief names both as traps, and
test 4.2 encodes them as declared exemptions rather than leaving the next person to re-derive it.

**Decision taken as head — item 12.** The register offers *"honour `KOR_FILESYNC_MODE`, or relabel"*.
There is no such environment variable anywhere in the solution. Honouring it would mean inventing a
global mode to justify a label, when the per-job `Mode` column is already the real authority. Briefed
the opposite: derive the column from the job rows and rename the header. A value nothing consults
should not be displayed as policy.

**My side of the contract when it returns:** run each new test against the **pre-fix** code and
confirm it fails there. A gate that passes before and after is the defect this audit's systemic
finding #1 describes, and adding one while claiming to close that finding would be worse than
leaving the bug.

### 2026-08-21 · Brief 3 verified — 10 and 12 gated, **11 is not**

Builds green: service, new test project, App. Tests: 2/2 in
`Kor.Operations.FileSync.Service.Tests`, 5/5 filtered in `Kor.Operations.App.Tests`.

**Every gate was run against the pre-fix code, one at a time.** That is the only reason this entry
is not a clean sweep.

| gate | fix reverted | result |
|---|---|---|
| `SchedulingCoverageTests` | `KorMapSync` removed from the catalog | **2/2 fail** — real gate |
| `FileSyncModeSummaryTests` | `Derive` replaced with `return "Shadow"` | **3 fail** — real gate |
| `FileSyncLogTailerTests` | `fs.Length` → `new FileInfo(path).Length` | **passes anyway — not a gate** |

**Item 11: the fix is right and the test does not lock it.** Reverting the exact line Codex named
(`FileSyncLogTailer.cs:73`) leaves the test green, so it would not catch a regression. The test is
still a worthwhile regression test — it proves the tailer reads a file another writer holds open —
but it does not reproduce *this* defect.

Why: `FileInfo.Length` reads the directory entry via `GetFileAttributesEx`, while `FileStream.Length`
queries the open file object. The gap the audit measured (0 vs 43,165 on the same file at the same
instant) is Windows lazily updating that directory entry for a file being appended by another
**process** — Serilog, in the real case. A single-process test writing with `AutoFlush` updates the
entry promptly, so the two sources agree and the bug will not reproduce.

The fix stands on its own: taking the length from the handle you are about to read from removes the
window by construction rather than narrowing it. But *gated* and *correct* are different claims, and
the register now says which one this is.

**Recommendation, not taken unilaterally:** a gate that reproduces needs a second process holding the
file, which is a heavier test than the defect warrants with 172 items still open. My call is to keep
the fix, keep the regression test, and leave item 11 honestly marked. Ian's to overrule.

### 2026-08-21 · MCP redeployed to KOR-APP01 — items 3, 4

**Before.** Deployed DLLs dated **Jul 17 — 35 days stale**. `grep -ac SplitWipNet` on the deployed
`Kor.Operations.Business.dll` returned **0**; the same scan on a fresh publish from HEAD returned 1.
The WIP fix genuinely was not in production.

**Done.** Published `Kor.Operations.Mcp` Release/win-x64 framework-dependent to
`_Publish\_Ops\Mcp\20260821_122327`, stopped `Kor.Operations.Mcp` via `sc.exe`, robocopied
(`/XF appsettings.json appsettings.Production.json` so live config survived — confirmed, both kept
their May timestamps), restarted.

**Verified as the artifact, not `/health` alone** — which is item 4, and the reason it is a separate
item: the stamped version had already disagreed with the binary's contents once.

- `SplitWipNet` in deployed `Business.dll`: 0 → **1**
- DLL dates: Jul 17 → **Aug 21 12:23**
- `/health` reports `0.4.2+4947d9772011c469...` — **the actual commit SHA**, so the deployed artifact
  is now traceable to source. That is systemic finding #2, closed for this service.
- Authenticated end-to-end, not just `/health`: `/tools` 200 (**23 tools**), `/coo-card/latest` 200
  (7,818 bytes), `/alerts/active` 200 (103,890 bytes). Unauthenticated 401s are the shared-password
  auth working, not a fault.

**Blast radius, recorded because it is bigger than "the AI".** This service also backs
`/collections` (including POST), `/alerts` (including `run-now` and `acknowledge`), and
`/coo-brief` + `/coo-card`. A restart interrupts all of it, not just `/ask`. Downtime was ~60 s.

**Checked and deliberately NOT changed.** Seven `Financials.Billed*` keys are `""` in the deployed
config while `App.config` carries real values, which looks like the same divergence as item 5. It is
not: `BilledFinancialsService.cs:99` passes a code default to `ParseAccounts`, and
`DefaultRevenueAccounts` is `4001.00, 4003.00, 4210.00, 4220.00, 4240.00` — **identical** to
`App.config:47`. Empty there is harmless. Leave them alone.

**Still open — items 5 and 6 need a SECOND restart.** `Financials:BilledDefaultOrg` is `""` on the
server (App uses `CAD`), `Financials:UsdToCadRateByYear` is absent entirely, and
`Mcp:EmployeeSummaryExcludedIds` does not exist in the deployed config at all. A backup of
`appsettings.Production.json` is already on the server as `.bak-20260821`. Not applied yet — timing
is Ian's call now that the blast radius is known.

### 2026-08-21 · Items 5 and 6 applied to KOR-APP01

`appsettings.Production.json` edited via JSON round-trip, **validated by re-parsing before writing** —
a malformed config takes the service down on restart, and this file has no second copy in git.

| key | before | after |
|---|---|---|
| `Financials:BilledDefaultOrg` | `""` | `CAD` — matches `App.config:71` |
| `Mcp:EmployeeSummaryExcludedIds` | **absent** | `["IANLALONDE","DALERSINGH"]` — matches `App.config:149` |

Confirmed untouched after the write: `Mcp.Username/Password/AnthropicApiKey/SqlConnectionString`,
`DeltekOdbc.Dsn/User/Catalog`. Backup remains at `appsettings.Production.json.bak-20260821`.

Restarted; `/health` ok, `/tools` 200 (23 tools), `/coo-card/latest` 200, `/alerts/active` 200.

**A third key was deliberately NOT set, and it is a finding against item 36.**
`Financials:UsdToCadRateByYear` is absent from the server and looked like the same omission. It is
not worth setting: `Mcp/Program.cs:60` reads it into `FinancialsOptions`, and **nothing in
`Kor.Operations.Business` or `Kor.Operations.Mcp` ever reads it back** `[RUN]`. Its only consumer is
`PartnerFinancialsViewModel.cs:585`, in the WPF app.

So the MCP *cannot* apply per-year FX — it has only the flat `BilledUsdToCadRate`. That is the
mechanism behind item 36's $35k gap: the App's Partner Financials converts USA work at the per-year
rate while anything served by the MCP uses the flat one, and no config change on the server can close
it. Item 36 now carries this. Writing the key would have been cargo cult — it would have changed
nothing and left a false trail that the divergence was handled.

