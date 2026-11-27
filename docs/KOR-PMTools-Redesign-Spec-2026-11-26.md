# PM Tools / Workload Meeting - Redesign Spec

**2026-11-26. Board row 2415.** Written by the Operations lead from Jim DesRoches's two calls with Ian. It was checked
against the code at Operations `404f8d1a`. Built since, 26 Nov: section 6 items 1 and 2 (R1-R5, R7, R8) in `f9b8ba49` and
`2974e27e`, plus the item 6 warning; items 3 and 4 (R9, R10, R14) in `2a3fd872`; R17 (Email the report) in `fda3ff79`;
R15, R21, R22, R25, R29, R30 (Ian chose 26 Nov to build the March money asks) in `0ad18755`..`bc991e52`; R20 Demos card as
a side item in `485921c6`. Each was looked at on Ian's screen except the last two small fixes in `bc991e52`. **R18 (Monday
sheet to each PM) is on hold until Jim confirms he still wants it** (Ian, 27 Nov). Where the two calls disagree, **the 9 Nov call wins**
because it is Jim's later word. Every such case is listed in section 7.

## 1. Sources

| Ref | Call | Where | Read |
|---|---|---|---|
| **M** | "Project Management Workload Discussion", 13 Mar 2026, 1h15m. Teams transcript with speaker labels. | `Desktop\_archive\Old Desktop\Workload Discussion.docx`; extract in `docs/KOR-PMTools-Redesign-Spec-2026-11-26-sources/`; transcript in Desktop/PM Tools/Transcripts (kept out of git: verbatim internal call) | 542 of 542 lines |
| **N** | Call with Jim, 9 Nov 2026, 15m. Teams recording, transcribed 26 Nov with faster-whisper small.en, no speaker labels. | OneDrive `Recordings\Call with Jim DesRoches-20261109_100814-Meeting Recording.mp4`; extract in `docs/KOR-PMTools-Redesign-Spec-2026-11-26-sources/`; transcript in Desktop/PM Tools/Transcripts (kept out of git: verbatim internal call) (transcribed with `tools/transcribe_recordings.py`) | 196 of 196 lines |

How references read:
- `M 27:40` is the 13 Mar call at 27:40.
- `M#25` is item 25 of the 13 Mar extract.
- `N 0:02:21` is the 9 Nov call at 0:02:21.

The other six recorded calls with Jim (4 Aug to 30 Oct) were transcribed and are not about PM Tools.

**Not available:**
- Jim's Bluebeam markup of Jeremy's spreadsheet (M 0:37, M 18:12).
- The screen he pointed at on 9 Nov (N 0:01:22).

Neither is in a transcript.

"Today" cites the code at `404f8d1a`. PMTools was last changed on 9 Nov at 19:54 (`07da0c54`, a comment). None of the
9 Nov asks has been built.

## 2. What the meeting is for (the acceptance test)

The workload meeting runs every two weeks (M 47:15). Jim drives the screen (M 1:00:48), and PMs and a couple of
drafting leaders attend.

On 9 Nov the feedback was that the meetings do not bring out the firm's project priorities for the next two weeks
(N 0:00:54-0:04:07). Each PM talks about their own list, and the drafters sit silent. In Jim's words, "at the end of
the meeting nobody seems to know what's going on" (N 0:07:18). That has to stop being true.

So the redesign is judged on this: **after one meeting run on it, everyone in the room can say which jobs are due in
the window, who is on them and what was agreed, and they have it in writing.**

Keep it familiar, because people already use Jeremy's weekly sheet (M 0:04, M#1). Workload comes first. Financials are
a separate, later demo (M 51:48, M 59:03).

## 3. The screen, top to bottom (target)

1. **Meeting banner.** Date, editing or read-only, New Meeting, Save, overall notes. *Keep as is*
   (`WorkloadMeetingWindow.xaml`:222-357).
2. **"Due in the next [2 | 3 | 4 | 6] weeks".** This replaces "This Meeting's Priorities", the P1-P5 block. See R1-R5.
3. **PM sections.** One per PM:
   - PMs are ordered per R10.
   - Each PM row shows totals over prioritised jobs (R9).
   - Expanding a PM shows their jobs sorted by phase (R12).
   - Each job carries its next milestone and CAD/Revit (R6, R7).
   - Opening a job shows who put hours on it (R13).
4. **Drafters' pass.** The same kind of list for the drafters, run after the PMs in the same meeting (R14).

Sending the report at the end of the meeting (R17) is a later step, not this build.

Separately, the PM Tools chooser gets a **Demos** card (R20).

## 4. Requirements

Status against today:
- **BUILT**: does it now.
- **PART**: does some of it.
- **NO**: absent.
- **REMOVE**: exists, and Jim wants it gone.

### A. The top list: what is due (9 Nov's main ask; build first, N 0:04:55)

| # | Requirement | Source | Today | Status |
|---|---|---|---|---|
| R1 | Replace the P1-P5 "This Meeting's Priorities" block at the top. Jim: "I don't find I use this. Plus there's just too many jobs here." | N 0:04:59-0:05:11, N 0:02:54 | P1-P5 block with chips and Priority/Project/PM/Notes rows, `WorkloadMeetingWindow.xaml`:393-507 | REMOVE |
| R2 | The top list shows the jobs PMs have **prioritised** whose **next due deliverable** falls in the look-ahead window, **in date order**. The due date *is* the priority: "they don't need to have a priority because they will be given a priority based on their due date". | N 0:01:21-0:02:41 | No due dates anywhere: 0 hits for Milestone/Deliverable/DueDate/Deadline in `Kor.Operations.App/PMTools` | NO |
| R3 | The window can be selected: "two weeks or three weeks or however far we want to look out". | N 0:01:38, N 0:02:42-0:02:48 | Nothing | NO |
| R4 | "Due date" means the next deliverable recorded in Deltek for the job, which is the next milestone not yet completed. | N 0:01:49-0:01:52, N 0:03:13; M 43:01 ("whatever milestone is listed that is yet to be completed") | Not read by the app. KOR records deadlines in Deltek `Projects_Deadlines` (`CustIssue`, `CustDateExpected`, `CustRemarks`), **not** in `PRMilestone`, which holds 29 rows on 4 projects, the newest dated 2023. Read 26 Nov: 9,215 deadline rows on 1,480 projects; 56 dated today or later. The Monday 05:00 "Weekly Project Deadlines" email (IIS `DeltekDeadlineSync`, `C:\VIsual Studio Projects\DeltekProjectDeadlines`) already reads this table. See section 5 | NO |
| R5 | Jobs on the hot list without a priority stay out of the top list ("I don't want all the noise people are putting on here"). | N 0:05:35, N 0:06:15-0:06:26 | Hot list is Deltek `CustWatchlist`, filtered in SQL (`Financials/FinancialsService.cs`:429). Priority 1-5 per meeting is in `dbo.WorkloadMeetingProjects` (`Kor.Operations.Data/SqlWorkloadMeetingStore.cs`:117-125) | PART: both flags exist, but nothing combines them with a date |

### B. PM sections

| # | Requirement | Source | Today | Status |
|---|---|---|---|---|
| R6 | Each job shows its **next milestone**: the R4 date and the milestone's name. Ian committed to it on 13 Mar. Jim on 9 Nov: "both of those things are missing". | M 42:23, M 43:07; N 0:03:06-0:03:22 | Nothing | NO |
| R7 | Each job shows **CAD or Revit**. | M 17:45; N 0:03:16 | Loaded from Deltek `CustDraftingType` into `DraftingType` (`FinancialsService.cs`:411, 453). Not a column in the meeting grid | PART |
| R8 | **Drafting manager** beside the PM. | M 0:37, M 18:03 | Column `DraftingManager`, hidden in Meeting Mode (column list in `WorkloadMeetingWindow.xaml`:827-1264, per the code inventory) | BUILT: show it in Meeting Mode |
| R9 | Each PM row's **fee and unbilled fee are totalled over prioritised jobs only**. | N 0:05:21-0:05:41. Changes M#25, which totalled all hot-list jobs | The PM header totals Fee and Unbilled over every row in the group (`PmGroupViewModel.cs`:58; `WorkloadMeetingWindow.xaml`:668-727) | PART |
| R10 | **Order PMs by the unbilled fee of their prioritised jobs**, highest first. | N 0:06:05-0:06:31. Changes M 43:42-44:39, which ordered by total fee | The default sort is total Fee over all jobs. An "Unbilled" sort exists, also over all jobs (`PmToolsViewModel.cs`:536-548) | PART |
| R11 | **JM at the top** every time. | M 43:42, M 1:00:48. Not repeated on 9 Nov; see D4 | Not pinned: no JM or pin logic in PMTools | NO |
| R12 | Inside a PM, jobs are sorted **by phase, "the way it is now"**. | M 1:00:48-1:01:27 | Phase order CA, CD, DD, SD, then other, then name (`PmGroupViewModel.cs`:42-45, 79-87) | BUILT |
| R13 | Open a job to see **who has put hours on it**. | M 27:40, M 1:04:37 | Double-click opens `ProjectFinancialDetailWindow`, whose "Team Breakdown" shows Employee / Hours / % (`WorkloadMeetingWindow.xaml.cs`:209-222; `Financials/ProjectFinancialDetailWindow.xaml`:818-838). Expanding the row inline shows only the notes editor | BUILT, as a separate window. An inline expand would match Jim's "expand" |

### C. The drafters

| # | Requirement | Source | Today | Status |
|---|---|---|---|---|
| R14 | After the PMs, **switch to the drafters** and show "a similar list" of their priorities "so that we can then give them a voice". JM and Griffin asked for this. | N 0:03:28-0:04:07. Changes M 29:45-30:37, which had a separate sheet for a separate drafters' meeting, deferred | No drafter view (searched "Drafter" in PMTools) | NO |
| R15 | What drafters need to see: drafting manager, drafting budget hours, and "are we on track or not". | M 30:04-30:25 | Draft Hrs / Budget / % / Remaining exist per job, mostly hidden in Meeting Mode | PART |

### D. Notes, outputs and follow-through

| # | Requirement | Source | Today | Status |
|---|---|---|---|---|
| R16 | A **note per job** that Jim can jot into during the meeting. | M 1:01:46 | A notes pencil per job, stored on the job's priority row in `dbo.WorkloadMeetingProjects`. **Fault: clearing a job's priority deletes its note**, because priority 0 deletes the row (`SqlWorkloadMeetingStore.cs`:256-261) | BUILT, with a fault to fix |
| R17 | **One button at the end of the meeting sends everyone the final report**: the jobs being worked on and who committed to them. Ian: "Through the app... I can do that." | M 1:04:10-1:04:23 | Excel export through a Save dialog only (`PmToolsExportService.cs`:147-238; `WorkloadMeetingWindow.xaml.cs`:299-309). No send (searched Email/Smtp/Outlook/Graph in PMTools) | NO |
| R18 | **Every Monday each PM gets a spreadsheet** with their data "right in their face". The app is for those who want to dig in. | M 47:15-47:56 | No scheduled send in PMTools. Whether a Worker job sends a workload sheet: **not checked**. What does exist: a Monday 05:00 per-PM "Weekly Project Deadlines" email from `DeltekDeadlineSync` on APP01. It holds deadlines only, and it is the obvious place for a PM's Monday sheet to go | NO, or not checked |
| R19 | Meeting history: last meeting's items are visible at the next one, and "in six months you can see all the historicals". | M 1:02:18. This was Ian's proposal; Jim said "Yeah, well, maybe" | Meetings are stored by date and past ones are read-only. New Meeting carries priorities and notes forward (`WorkloadMeetingPanelViewModel.cs`:524-529; `SqlWorkloadMeetingStore.cs`:86-94) | BUILT |
| R20 | **Recorded demos** reachable from PM Tools, as a tab or one repository by topic. Each demo is recorded one-to-one so the presenter is not sidetracked. Jim asked Ian to propose the form. | N 0:12:41-0:14:03 | The chooser has two cards only: Workload Meeting and PM Capacity & Risk (`PmToolsChooserWindow.xaml`:96-116) | NO |

### E. Columns and numbers (13 Mar; none withdrawn on 9 Nov)

| # | Requirement | Source | Today | Status |
|---|---|---|---|---|
| R21 | **Original fee and extra fee shown separately.** Extras are elements under 01. Another client's additional service is its own sub number and its own project. Every sub number is listed as its own project. | M 1:02-2:36, M 4:56-18:57 | One combined Fee: `totalFee = p.Fee + hourlyRev` (`FinancialsService.cs`:243) | NO |
| R22 | **Define % billed.** Jim leans to % of original fee, so that over 100% shows an extra being billed. Ian offered % of original and % of extras. | M 19:20-20:16 | A `% Billed` column exists. Its basis is not stated on screen | PART: definition owed |
| R23 | **Budget hours beside actual hours**, for engineering and drafting. | M 21:17 | Eng Hrs / Eng Budget and Draft Hrs / Draft Budget are adjacent | BUILT |
| R24 | **Budget hours from fee only, no area.** | M 34:09-36:34 | Estimated from similar past projects or a formula, and GFA is used (`PmProjectRow.cs`:87; `FinancialsService.cs`:264-293). The constants are open (money columns, later) | PART |
| R25 | **Earned fee** (hours x charge-out rate) and **cost** (hours x salary rate), with **fee billed vs earned** and **fee billed vs cost** ratios. Billed should land between cost and earned. | M 24:52-27:23, M 55:35-57:04 | Labour cost is loaded (`FinancialsService.cs`:329-336). Earned fee and the ratios were not found | NO |
| R26 | Keep **Fee/Hr** and **Billed/Hr** for now. Target $125/hr; cost $92/hr. | M 52:37-54:45 | Both columns exist, hidden in Meeting Mode | BUILT |
| R27 | Keep the **progress bars** for fee billed and the eng and drafting budgets. | M 24:52 | Whether the grid draws bars: **not checked** | not checked |
| R28 | **Remove non-billable hours and GFA** from the report. GFA stays in Deltek. | M 21:17-24:47 | Neither is a meeting-grid column; both are loaded only (`PmProjectRow.cs`:22; `FinancialsService.cs`:329-336). The 23-column PM Groups export: **not checked** | likely BUILT |
| R29 | **Plain definitions on every column.** | M 18:19 (Ian) | KPI Dictionary button (`WorkloadMeetingWindow.xaml.cs`:174-175) | PART |
| R30 | Mark **underwater jobs**, subtly ("column of shame" declined). | M 45:17-45:27, M 58:50 | Delivery Risk column, and "N at risk" on PM rows | PART: Jim's definition owed |

## 5. Data the redesign needs that the app does not read today

1. **Deltek deadlines** (R2, R4, R6): the next deadline per project, with its date and issue type. The source is
   `Projects_Deadlines`, read with the same join as the Monday deadlines email (`DeltekProjectDeadlines\appsettings.json`,
   `Sql:WeeklyDeadlinesQuery`). One table, two readers: the app should read it the same way.

   **Coverage is the risk.** Read 26 Nov 2026 through the `DELTEK_VP` linked server, with my own filter: top-level PR rows,
   `Status = 'A'`, and `CustWatchlist` set. This is not the app's exact filter (`FinancialsService.cs`:391-430).
   - 61 active hot-list projects.
   - 20 of the 61 have a deadline dated today or later.
   - 11 of the 61 have one in the next 14 days.

   So 41 of 61 hot-list jobs would never reach a due-date list as things stand. The list is only as good as PMs' entries
   in Deltek (D2). The ranking could also be used to push PMs to enter dates, the way Jim
   wanted the order to push them to keep their data current (M 59:39-1:00:03).

   The `CustDateExpected` / `CustDateCompleted` pairs on `ProjectCustomTabFields` (Preliminary Review, Progress, PM
   Review, Final Review, Tender, Building Permit, Construction) are a second possible source. Whether KOR fills them:
   **not checked**.
2. **Original vs extra fee** (R21), by element and sub number, with the 13 Mar caveats:
   - 01 and 02 can both be original contracts.
   - JB numbers sub numbers differently.
   - Gardena is set up wrongly.

   The rule is agreed with Jim when the money columns are built (not this build).
3. **Charge-out rate and salary rate per employee** (R25). Where these live is not checked.
4. **Who a drafter's priorities belong to** (R14): the drafting manager (D5).

## 6. What to build: the 9 Nov changes only

The March asks are mostly in the tool already: the Round 52 rework of June 2026 built the meeting from the PM lists,
with notes, history, phase sort, budget beside actual hours, drafting manager and Fee/Hr. They are not reopened. The
build is the five things Jim asked for on 9 Nov, plus one fix:

1. **The top list (R1-R5).** Replace the P1-P5 block with "Due in the next [2 | 3 | 4 | 6] weeks": prioritised jobs,
   ordered by their next date in Deltek `Projects_Deadlines`. Prioritised jobs with no future deadline are listed
   underneath as "No deadline in Deltek", so the gap is visible in the room (D2).
2. **Next deadline and CAD/Revit on every job (R6, R7).** Both columns stay visible in Meeting Mode, along with
   Drafting Mgr (R8).
3. **PM totals and order over prioritised jobs (R9, R10).** Fee and Unbilled count prioritised jobs only. PMs are
   ordered by that unbilled figure. The existing sort choices stay.
4. **The drafters' pass (R14, R15).** A toggle switches the same screen to drafting managers: their prioritised jobs,
   with draft hours, draft budget and draft %.
5. **Demos card (R20).** A third card in the PM Tools chooser that lists the recorded demos by topic.
6. **Fix (R16).** Clearing the priority of a job that has a note now asks first; "No" keeps both. A "Yes" still deletes
   the note, because the table holds P1-P5 only (CHECK Priority BETWEEN 1 AND 5) and the note lives on that row. Keeping
   notes past a cleared priority needs a schema change to the live database, which is Ian's call.

Not in this build: the money columns (R21-R30), sending the report (R17) and the Monday sheet (R18). Jim did not raise
them on 9 Nov, and he kept financials separate.

Each step is done when Jim or JM sees it working on the real screen in a meeting. A passing build is not enough.

## 7. Where 9 Nov changes 13 March

| 13 Mar | 9 Nov | Spec follows |
|---|---|---|
| PM totals over all hot-list jobs (M#25) | Totals over prioritised jobs only | 9 Nov (R9) |
| PMs sorted by total fee (M#26) | Sorted by unbilled fee of prioritised jobs | 9 Nov (R10) |
| Hot jobs brought to the top by a priority level, deferred (M#33) | Top list by due date; the priority level does not set the order | 9 Nov (R1-R2) |
| Drafters: a separate sheet for a separate meeting, later (M#37) | A drafters' pass in the same meeting | 9 Nov (R14) |
| JM always on top (M#27) | Not mentioned | 9 Nov order; no pin (D4) |

## 8. Defaults (no questions for Jim before building)

Each default follows the existing tool or Jim's latest word. He corrects any of them at the review.

- **D1 "Prioritised"** means any P1-P5 set in the tool. That is the only priority mechanism there is.
- **D2 No deadline:** listed under the top list as "No deadline in Deltek", never hidden. Today 41 of 61 active hot-list
  jobs would land there (section 5). That is a data gap for the PMs to fill, not a design question.
- **D3 Window:** two weeks by default ("the next two weeks", N 0:02:42). The choices are 2, 3, 4 and 6 weeks.
- **D4 PM order:** unbilled fee of prioritised jobs (N 0:06:05). JM is not pinned, as in the tool today.
- **D5 Drafters' pass:** grouped by drafting manager, the field the tool already carries.

## 9. Out of scope here

- Staff utilisation (M 42:02-42:23; Jim was unsure).
- The financials demo (M 51:38).
- The detail catalog and Virtual Drafter parts of both calls.
