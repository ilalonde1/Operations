# Workload meeting tool: Jim's redesign instructions, call of 9 Nov 2026

Source: `Call with Jim DesRoches-20261109_100814-Meeting Recording.txt` in Desktop/PM Tools/Transcripts (kept out of git: verbatim internal call) ( OneDrive Recordings `Call with Jim DesRoches-20261109_100814-Meeting Recording.mp4`, transcribed with tools/transcribe_recordings.py) (faster-whisper small.en, 15.3 min, no speaker labels, noisy). Timestamps are h:mm:ss into the call. Quotes are verbatim from the transcript, including its errors: "unbuilt fee" almost certainly means "unbilled fee"; "cat or rabbit" means "CAD or Revit"; "deltech" means Deltek; "Jam" and "John" most likely mean JM (Jim says "jm" at 0:07:18). Those readings are inferences. Attribution: Jim is the main speaker and is named only where the content makes it clear. **[Ian]** marks lines that are clearly Ian's (he is the one doing the build). Everything else is "speaker unclear".

Cross-references ("13 Mar #n") point to the numbered items in section 2 of `Jim-2026-03-13-Workload-Instructions.md` (beside this file).

## Summary

Jim and JM talked about the workload meeting while they were away. Feedback said the meetings do not pick out overall **project** priorities for the next two weeks. Each PM talks about their own priorities, and drafters sit silent. Jim wants five changes:

1. Replace the block at the top of the workload meeting tool (today the "P1s" priority list, which Jim says he does not use and which holds too many jobs). The new block lists the **prioritised** jobs whose next recorded deliverable (Deltek milestone) falls in a **selectable look-ahead window** (two weeks, three, "however far"), **sorted chronologically by due date**. The due date is the priority, so these jobs need no separate priority rank.
2. Add the **next expected deliverable/milestone** and **CAD vs Revit** to each PM's project list.
3. Compute each PM's **fee and unbilled fee totals from prioritised jobs only**, and order PMs by that figure. This keeps out the "noise" of jobs left on the hot list without a priority.
4. After the PMs, **switch to the drafters** and show a similar list of their priorities, so they speak up. JM and Griffin asked for this.
5. A place in the app, perhaps a PM Tools tab or a single demo repository, holding **recorded demos**. Jim asked Ian to suggest the form.

Jim wants the tool reviewed **Friday afternoon (13 Nov)** and used at the **Monday workload meeting (16 Nov)**. He accepts building it "a bit at a time", with the top due-date list as the main thing. Ian called it "a complete rewrite" and said "I can probably get it done by the end of the week".

## Requirements (numbered)

Status codes: NEW = not in the 13 Mar instructions; CHANGES = alters or replaces a 13 Mar item; REPEATS = restates a 13 Mar item.

1. **Replace the top of the tool with a list of upcoming projects by due date.** Jim, 0:01:21–0:01:42: "What we decided this is lacking what we'd like to change is up here at the top ... Be able to list The projects that we're working on in the next Several weeks period ... display those jobs at the top here based in chronological order as to when ... their due dates are". Jim, 0:02:54: "replace this with Projects that have due dates". **NEW.** It also **CHANGES** 13 Mar #33 (hot or priority jobs brought "to the top of the list", deferred by Ian on 13 Mar). The top list now orders those jobs by due date instead of by a priority level.
2. **The look-ahead period is selectable.** Jim, 0:01:38: "it'd be nice if we could select the period". Jim, 0:02:42–0:02:48: "in the next two weeks Or three weeks or however far we want to look out". **NEW.**
3. **"Due date" = the next recorded deliverable in the database.** Jim, 0:01:49–0:01:52: "what we need to identify is What's the next recorded deliverable in the database that is due on any of those projects". **REPEATS** 13 Mar #22 (next milestone from Deltek, "whatever milestone is listed that is yet to be completed"). It now drives the sort as well as being a column.
4. **Only jobs a PM has prioritised feed the top list, sorted by due date and not by priority level.** Jim, 0:02:21–0:02:41: "if in your list you've prioritized a job I Want to make sure those jobs that have been prioritized are popping up in this upper list here in Chronological order they don't need to have a priority because they will be given a priority based on their due date So they should just be sorted by Due dates". **CHANGES** 13 Mar #33.
5. **Remove the current P1 priority block at the top.** Jim, 0:04:59–0:05:11: "the main thing is we need to show the projects that are ... Because right now having these priorities up here like all the p1s up here. It isn't I don't find I use this Plus there's just too many jobs here". **CHANGES** the current build (the P1 block is not in the 13 Mar items; it is in the tool as built). Relates to 13 Mar #33.
6. **Per-PM project list: add the next expected deliverable / next milestone.** Jim, 0:03:06–0:03:13: "The first deliverable is what we want to see like this or the next expected deliverable like the next milestone that's been recorded in deltech". Jim, 0:03:22: "both of those things are missing from this project list". **REPEATS** 13 Mar #22, which Ian committed to on 13 Mar and which is missing from the build.
7. **Per-PM project list: add CAD vs Revit.** Jim, 0:03:16: "it'd be good to be able to see here if the projects are cat [CAD] or rabbit [Revit]". **REPEATS** 13 Mar #10, also missing from the build (0:03:22).
8. **A drafters' segment in the same meeting.** After the PMs, switch to the drafters' priorities in a similar list so the drafters take part. Jim, 0:03:28–0:04:07: "the other thing that John [JM?] wanted to do and what Griffin would also suggest ... Once we go through that it would be good to switch over to drafters and look at the drafters priorities and look at a similar list so that we can then give them a voice ... the drafters are sitting there and not contributing". **CHANGES** 13 Mar #37, where the drafters' version was a separate sheet for a separate meeting and was deferred. It is now a second pass inside the workload meeting. What "drafters' priorities" are keyed on (drafting manager? 13 Mar #3) was not said.
9. **Each PM's fee and unbilled-fee totals count prioritised projects only.** Jim, 0:05:21–0:05:41: "there's the fee there's an unbuilt [unbilled] fee. I think this is Fine, except what I would like to see here also is I Would like to see the fee and the unbuilt fee totaled up for only the projects that have priority Because I don't want all the noise people are putting on here". **CHANGES** 13 Mar #25 (PM rows carry "the total of all of these columns" across all hot-list jobs).
10. **Order PMs by the unbilled fee of their prioritised jobs.** Jim, 0:06:05–0:06:31: "Based on the jobs that are you're actually working on. What is the unbuilt [unbilled] fee that is? ... outstanding so that I can then Let's put you in the list according to that ... if we can sort only by the jobs That have been given a priority That would be helpful". Why, 0:06:15–0:06:26: one PM keeps many hot-list jobs that are "not a priority and I've asked him to trim it down But he won't". **CHANGES** 13 Mar #26 (sort PMs by total fee across the hot list). The sort key is now unbilled fee and the scope is prioritised jobs only. Jim did not mention "JM always at the top" (13 Mar #27). Whether that still holds is open.
11. **The meeting's purpose: everyone leaves knowing what is going on.** Jim, 0:07:18–0:07:32: "we want these meetings to be useful and helpful and Productive and not just a bunch of people sitting around talking about stuff at the end of the meeting Nobody seems to know what's going on". This is context and the acceptance test, not a column. It is in line with 13 Mar #31 and #34 (the final report to everyone) but restates neither directly.
12. **Delivery may be phased; the top list comes first.** Jim, 0:04:55–0:04:59: "we can do it a bit at a time if you want I think the main thing is we need to show the projects that are [due]". **NEW** (sets priority within the work).
13. **Recorded demos available from the dashboard / PM Tools.** Speaker unclear, 0:12:51: "can we like have something on the dashboard where those things just live?" Jim, 0:13:10–0:13:25: "Maybe you go to PM tools and there's another tab here that shows demos ... or maybe we have one Depository on here that has all of the demos you can go pick your topic ... Maybe you can think about that Tell me what you think makes sense". **NEW.** This is a PM Tools item but not part of the workload screen.
14. **Record demos as a one-person session, not in front of everyone.** Jim, 0:13:46–0:14:03: "maybe the demo that gets recorded that we put in here. Maybe is one that we do for just like one person ... So that the person doing the demo doesn't get sidetracked". Jim, 0:12:41–0:12:49: "we should record the demo and then have that save somewhere so that people who can't see the demo can go Back and look at it". **NEW** (process, linked to #13).

### Ian's commitments and responses
- **[Ian]** 0:04:36: "Well, it's a completely redoing it right so" (his reaction to the scope).
- **[Ian]** 0:06:51–0:06:56: "Yeah, it's a lot of work. It's a complete rewrite, but Yes, I can probably get it done by the end of the week".
- **[Ian]** 0:07:36: "Yep, I'll do my best". 0:07:47–0:07:54: "let me have a look at it get my head around What I need to change".
- **[Ian]** 0:10:32–0:10:38: "I'd rather get my stuff that I'm working on done rather than dropping tools ... redoing this for your workflow meeting, but I can I'll see what I can do". After this, Jim pushes the detail catalog meeting to next week (0:10:43).
- **[Ian]** 0:15:09: "I'll do my best."
- **[Ian, implied]** Propose where the recorded demos live. Jim asked at 0:13:25: "Maybe you can think about that Tell me what you think makes sense". Ian made no explicit reply.

## Open questions

1. **What does "prioritised" mean in the data?** Is it the existing P1/P2 priority a PM sets in the tool (0:05:05 "all the p1s"), any priority at all, or something else? Jim says jobs PMs have "prioritized" feed the top list (0:02:21) and the totals (0:05:35), but does not say which priority levels count.
2. **Which deliverable counts as the due date?** Is it the next uncompleted Deltek milestone (13 Mar #22), or any "recorded deliverable in the database" (0:01:52)? What happens to a prioritised job with no future milestone recorded: is it hidden from the top list or shown undated?
3. **Default look-ahead window.** Two weeks is implied (0:01:17, 0:02:42), but "select the period" leaves the default and the choices open.
4. **PM order: unbilled fee or fee?** 0:05:35 asks for both fee and unbilled fee totals over prioritised jobs. 0:06:05–0:06:15 asks to rank by unbilled fee. Ian's reading of which one sorts is not confirmed. Does "JM always at the top" (13 Mar #27) still apply? Not mentioned.
5. **The drafters' segment.** Is it grouped by drafter or by drafting manager? Which columns (13 Mar #37 suggested drafting manager, drafting budget hours, on track)? How do drafters' "priorities" get recorded? Not specified.
6. **Does the top list carry PM / drafting manager / CAD-Revit columns?** Not said.
7. **Recorded demos: a PM Tools tab or one repository by topic?** Jim deferred to Ian (0:13:25).
8. **Jim's markup.** He shared his screen and pointed at "up here at the top" and "down here in each person's project" (0:01:22, 0:02:56). The screen itself is not in the transcript.

## Deadlines

- **Review with Jim (and JM): Friday afternoon of the same week, Fri 13 Nov 2026.** Jim, 0:04:26–0:04:33: "What John and I were hoping to be able to do is maybe review this this week on Friday and then ... conduct the meeting with it on Monday". Jim, 0:11:04: "Say Friday afternoon and I'll check with John of what a good time is". Jim, 0:14:54: "I'll talk to Jam probably Friday afternoon. I'll put something in the calendar".
- **Use at the workload meeting: Monday of the following week, Mon 16 Nov 2026.** Jim, 0:04:19: "We have another workload meeting coming up next week on Monday". Fallback, Jim, 0:15:03: "If you can't and we have to wing it on Monday. We'll just have to do that too. But be nice if it could be ready". Jim, 0:06:59: "if you can't let me know".
- **Ian's estimate:** "end of the week" (0:06:56).
- Dates are worked out from the recording date 2026-11-09, which `date` gives as a Monday. Friday = 2026-11-13 and the next Monday = 2026-11-16.
- **Not the workload tool:** the detail catalog / virtual drafter review meeting, first proposed for this week (0:08:07), was pushed to next week (0:10:43). Jim would send a meeting request (0:12:01, "Tuesday of three" at 0:11:57 is garbled).

### Rest of the 9 Nov call (not the workload tool)
- 0:00:01–0:00:19: small talk about a good week, meetings and a conference.
- 0:08:07–0:12:28: detail catalog / virtual drafter. Simon's feedback is that people still do things the old way. Jim praises the tool demonstrated last week, but would not "give all those extra features to everybody just yet". Catalog first, then release "to the masses".
- 0:12:31–0:14:43: recording demos (requirements #13–#14). Griffin's demo, and the "sidetracked" / "Rory's negative Feedback" remark at 0:14:08.

## Other calls (B)

- **7 Aug 2026 (20260807_154314, 26.4 min).** Jim wants a project map for the NAMDAR (Namdar Realty Group) San Diego pursuit: a QR code or link in the proposal, with KOR jobs and his prior jobs as different-coloured dots (orange/green, red for KOR). He also wants per-person project maps on website profile pages, populated from Deltek for engineer of record / PM, regional pages with regional leaders and region-synced maps, and replacements for the "flipping pictures". **Nothing about PM Tools, the workload meeting, the hot list or PM workload.** The closest point is a project-management role used as a map filter (0:17:31–0:18:28: "If you're either project manager or engineer of record on any project, it should show up on your map").
- **12 Aug 2026 (20260812_105848, 9.4 min).** Cleanup of the San Diego project map for the NAMDAR proposal. Duplicate dots come from featured projects plus Deltek entries, and JB's 01–06 sub-number jobs. Jobs Jim marked in his spreadsheet are removed. He wants the screenshot zoomed to the downtown core and the QR code pointed at the live San Diego project map with NAMDAR's site in a different colour. Ian is to hand it to Delir for the end of the proposal. **Nothing about PM Tools, the workload meeting, the hot list or PM workload.**
- Check: a case-insensitive grep for `workload|hot ?list|pm tools|priorit|deliverable|unbilled|unbuilt` across both files returned 0 matching lines in each. This backs up the full read; it does not replace it.

## Coverage

| File | Lines read | Total lines |
|---|---|---|
| 20261109_100814 (9 Nov) | 1–196 | 196 (2 header + 194 timestamped) |
| 20260807_154314 (7 Aug) | 1–665 | 665 |
| 20260812_105848 (12 Aug) | 1–238 | 238 |
| `workload-instructions.md` (cross-reference) | 1–139 | 139 |

Each file was read whole in one Read call. Line totals are from `wc -l`, which gives 196, 665 and 238 (it counts newlines, so the last line, if it has no trailing newline, would not be counted). The Read output ended at lines 196, 665 and 238 respectively.
