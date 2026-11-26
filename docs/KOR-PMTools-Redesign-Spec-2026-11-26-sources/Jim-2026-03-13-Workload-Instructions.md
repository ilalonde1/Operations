# PM Tools / Workload app: Jim's instructions from "Project Management Workload Discussion", 13 March 2026

Source: the Teams transcript in Desktop/PM Tools/Transcripts (kept out of git: verbatim internal call) as `Workload Discussion-20260313.txt` (text of Desktop\_archive\Old Desktop\Workload Discussion.docx) (Teams transcript, 1h15m35s, Ian Lalonde and Jim DesRoches). Timestamps are m:ss or h:mm:ss into the call. Quotes are verbatim from the transcript, which is noisy (speech-to-text). Jim spends the first ~58 minutes walking through his markup of the existing hot-list / PM report spreadsheet (the one Jeremy built). From 1:07 onward the call moves to an unrelated topic (the detail catalog).

## 1. Summary

Jim wants the existing hot-list / PM workload spreadsheet turned into a tool in Ian's app. He wants it to stay recognisable, because people already use the weekly sheet. It has two uses. First, every Monday each PM is sent a spreadsheet export with their data "right in their face". Second, it is the screen Jim drives during the workload meeting, which runs every two weeks and is mostly engineers plus a couple of drafting leaders. On that screen PMs are listed with totals and sorted by total fee, highest first, with JM always at the top. Jim expands a PM to see that PM's hot-list jobs sorted by phase, then expands a job to see who has put hours on it. He wants to jot notes against jobs and, at the end of the meeting, press a button that sends everyone the final report: which jobs are being worked on and who has committed to them. On the data side he wants:
- a drafting manager column
- original fee and extra fee split out
- budget hours placed beside actual hours
- two new indicators, earned fee (hours x charge-out rate) and cost (hours x salary rate), with ratios of fee billed to each
- non-billable hours and GFA dropped from the report

A drafters-only version may follow. It was agreed to build the full engineering version first and cut it down from there. Workload comes first; the financials work is kept separate for a later demo.

## 2. Requirements and instructions

Speakers: J = Jim, I = Ian. Items marked **[Ian]** are Ian's suggestions or commitments.

### General
1. **Keep it familiar.** J 0:04: "I don't want to change it up too much." J 0:37: "we keep all of this stuff. That's information we need that I may want to sort by." (Jim's markup showed which existing columns to keep. The markup itself is not in the transcript.)
2. **Keep workload separate from financials.** J 51:48: "Let's focus on this workload thing first. Let's separate that out from the financial stuff." J 59:03: "go ahead and separate these two things. Do whatever needs to be done to this workload thing. And then let's do another demo where we talk about that stuff."

### Columns and data fields
3. **Add a drafting manager column** next to the project manager. J 0:37: "add drafting manager so we can see the project manager and the drafting manager." J 18:03: "The project manager and the project and the drafting manager lists are included here [in Deltek]."
4. **Split the fee into total and extra.** J 1:02: "total fee and extra fee... in two different columns. I think it's probably good to include the extra fee in the total fee... But break out the extra fee as a separate item." Why (J 1:51): "if the job's going underwater and you're doing all this extra work, there should be additional services". J 2:36: "maybe we have original contract and extras, and then we add the two together." The final set of columns is not settled (see Open questions).
5. **How Deltek holds extras** (needed to build item 4):
   - Extras billed to the original client are elements under the 01 sub number (J 4:56: "that becomes an element under the original 01 sub number. It just gets added to the original fee"). Each change gets its own element (J 10:42), and a lone "01 General" element is only the default starter (I 10:31, J 10:42).
   - An additional service for a different client gets its own sub number and is treated as a separate project (J 5:16; J 12:40: "in Deltek, we treat that as a separate project because it's got a different client").
   - **Every sub number is listed as a separate project.** J 14:36: "Even the projects that have sub numbers are going to be listed as a separate project." J 17:01: "every sub number is going to be considered a different project".
   - Caveat, J 17:09: "sometimes like an 01 and an 02, those are original contracts. And sometimes if it goes to 03, 04, 05, 06, those would be extras."
   - J 18:57: "the total fee is probably going to end up... reflecting every sub number... And the extra fee will only be extra fees associated with one sub number, like an element".
6. **Percent of fee billed needs a stated definition.** Jim leans toward percent of original fee. J 19:53: "Whatever it is, we have to define it, right? Maybe we do percent of original fee, that way if you end up going over 100%, you know there's an extra that's being billed". J 20:16: billing cannot pass the contract fee without an approved extra.
7. **[Ian] Percent billed of the original fee and percent billed of the extras.** I 19:20: "I could even do percentage build of original fee and percentage build of extras." J 19:27: "maybe that makes sense."
8. **Phase column** comes from the current phase in Deltek, which the PM keeps up to date. J 17:34: "that's something that the project manager is supposed to keep up to date."
9. **Hot-list membership** is the Deltek checkbox. J 17:45: "this checkbox, that's how you end up, that's what puts you on the list."
10. **Add CAD vs Revit.** J 17:45: "CAD is where you record if it's CAD or Revit. That's another thing I want to include."
11. **Keep actual engineering and drafting hours.** J 20:55: "Engineering hours is good, drafting hours. So this is the actual hours spent... that we need."
12. **Put budget hours for engineering and for drafting right after the actual hours.** J 21:17: "Budget hours for each... engineering and drafting be reflected right after the actual hours. I just don't like the way this is arranged."
13. **Remove the non-billable hours column.** J 21:17: "non-billable hours, like, that's a stupid column"; J 21:44: "that column is dumb."
14. **Remove the GFA column from the report, but leave GFA in Deltek.** J 22:07: "only half of these are filled in... reporting it is really not helping us." J 22:51: "No, leave it in Del Tech". J 24:47: "But we don't need to report it on us on this." I 22:09: "we'll get rid of it."
15. **Keep the progress bars.** J 24:52: "I do like the fact that there's this little bar here that... gives you a visual of how close you are to the build fee. I do like the same bars for your drafting and engineering budget."
16. **Add earned fee and cost.** J 24:52: "The earned fee would be the hours spent times the charge out rate of the employee... The cost hours times salary rate is how much it costs us to do that project". J 26:11: "It'd be great to see those two numbers calculated and put in this column for each project".
17. **Add two ratios: fee billed vs earned fee, and fee billed vs cost.** J 26:11–26:36: "the ratio I'd like to see is build fee versus earned fee ratio and the build fee versus cost ratio". (The transcript writes "build"; this most likely means "billed". Unclear in transcript.) Jim's reading of them:
    - J 27:23: "If you're above this earn fee number... your job's making money". This was Glotman's main indicator (J 27:11).
    - J 55:35: "you don't want your earn fee exceeding your build fee... if your build fee ends up being your earned fee, then you're making about 35% profit."
    - J 57:04: "if your cost goes above your build fee... you're just working for free. So you should be landing somewhere in between those two numbers".
18. **Earned fee also shows how staff are deployed.** J 57:45: "If the guy putting time in on the job is the highest charge out rate, then that's not an efficient management of that person's time". This is context, not a separate column.
19. **Keep both "fee per hour spent" columns for now** (actual fee per hour spent, and fee billed per hour spent). J 52:37: "I guess those are two good metrics to look at. I think we probably should keep them." J 54:15: "For now I would say let's keep them... because I think people are actually looking at this sheet." Caveat, J 53:47: billed per hour "is maybe the best metric". Reference rates:
    - J 54:15: target "$125 per hour. So any job that goes below $125 per hour is under our target"
    - J 54:45: Rory's cost figure "now is 92 bucks an hour... if you go under 92 bucks an hour, we're not making money."
20. **Budget hours: drop area and calculate from fee.** Jeremy's spreadsheet formula uses GFA if it exists and falls back to fee (J 34:09). Jim, 36:34: "just abandon the area altogether and just use the fee... use the fee to calculate the budget hours based on something sensible." J 35:26: "we need to clarify how that works". The constants themselves are open (see Open questions).
21. **[Ian] Add plain definitions to the columns.** I 18:19: "put a bunch of good definitions on it so that you'll be able to see a real quick human definition".
22. **Next project milestone (Jim was tentative; Ian committed).** J 42:23: "The next project milestone, because if that's listed here, it kind of helps you understand what's hot and what's upcoming" (Jim had question marks on it). Source, J 43:01: Deltek milestones, "whatever milestone is listed that is yet to be completed". **[Ian]** I 43:07: "I'll include that on the spreadsheet, yeah."
23. **Summary row across the top: Jim is unsure.**
    - J 37:01: "I'm not sure how useful this information is."
    - J 37:35: percent of fee unbilled is "a number that you could probably see going up and down, and maybe you could see a trend".
    - J 38:01: average fee per square foot is doubtful without square footage.
    - J 38:34: "maybe we just leave it if it's actually might be helpful."
24. **Keep the historical trend tracking.** Jeremy logs data each time the sheet is produced. J 38:54: "He is tracking all of this... which I think is kind of clever"; J 39:04: "maybe that's a good thing to keep it to the track those trends". Which metric to plot is open (J 41:21). **[Ian]** I 41:32: "focus on a few metrics to begin with and then expand from there". J 41:46: "Yeah, yeah."

### Grouping, sorting and drill-down
25. **The dashboard opens on PMs with their totals and expands down to projects, then to employees.** J 27:40: "When you open this dashboard, what I would like to see is every project manager listed, and like the total of all of these columns beside each project manager, and then if I click on... an expand button... it would expand out that project manager and show all their projects that are on the hot list... And then... click on each project and expand and see where the hours are being spent from which employee". J 28:53 hedged on the employee level ("we probably won't go as deep as the employees"), but at 1:04:37 he asks for it ("click on the one project and expand that and see who's actually worked on it"). Ian, 29:08: drill-down "that's already there" in his app.
26. **Sort PMs by total fee, highest first.**
    - J 43:20: "I'd like to see it sorted by project manager and sorted by phase."
    - J 43:42: "sort it by project manager. Sorted by... the total fee of each project manager." Why: alphabetical order makes the meeting "not really that focused a discussion".
    - J 44:39: "have a total for each project manager across here and have it sorted by highest to lowest."
    - J 46:14: "We want to focus on those top three dogs."
27. **JM always at the top.** J 43:42: "I think we should start with JM every time. He should be the top of the list." J 1:00:48: "it'll be JM at the top of the list every time". Whether this is a pin or the result of the fee sort is unclear in the transcript.
28. **Within a PM, sort jobs by phase, in the current order.** J 1:00:48: "when I expand his list, they're all sorted by phase". J 1:01:27: "By phase in reverse, or no, no, but in phase the way it is now, because the jobs that are starting out and the ones that are in CD are gonna be the ones that probably require more effort".
29. **Keep the tone subtle.** Jim declined Ian's joking "column of shame" (I 45:17). J 45:27: "I'm thinking of doing it maybe a little more subtly". The later "loser column" and "AI equivalent" remarks (I 58:15) and "Kevin's head" (J 58:37) are banter, not requirements.
30. **Mark underwater jobs, perhaps by colour.** J 58:50: "how do we identify jobs that are underwater compared to others? Maybe there's some sort of colour coding we use or something, I don't know." This was left open.

### Meeting workflow
31. **How the meeting runs.** J 1:00:48: "I'll open the spreadsheet. It'll be the dashboard. It'll just have all the project managers listed... a line for each project manager that indicates... what volume of work... they've got... we'll start at the top of the list". J 1:04:37: "talk about his jobs and then close them back up again and then go to the next person, expand their list". J 1:04:59: "have it all happen right in front of them with the same list."
32. **A comment column for notes per project.** J 1:01:46: "Maybe there's a comment column there that I can just jot notes into for each project. You know, this job's going to be hot". **[Ian]** I 1:02:18 proposed bi-weekly action items that are committed to the record, so the next meeting shows last time's item and "in six months you can see all the historicals". J 1:02:25: "Yeah, well, maybe".
33. **A way to flag hot or priority jobs and bring them to the top.** J 1:02:54: "maybe it would be great if those ones were now at the top of the list... so we only talk about the hot ones and then... the guys can go in and actually make them the top, top priority, right? Maybe we have a priority called..." Why the hot list alone does not do this, J 1:03:17: people leave jobs under construction on it, and the real workload is "the stuff that we haven't billed yet". **[Ian]** I 1:03:55 deferred it: "let's get this first iteration done, and then we can add more bells and whistles... the top 5% or something".
34. **One button at the end of the meeting sends the final report.** J 1:04:10: "at the end of the meeting, we could just press a button and send out the final report to everybody that identifies all the jobs that we're going to be working on and who's committed to work on them". **[Ian]** I 1:04:23: "Through the app... I can do that".
35. **Use the ranking to push PMs to keep their data current.** J 59:39: "get their current projects on the hot list and make sure... they're up to date with the stage that they're at." J 1:00:03: "if we arrange it such that... the winners are at the top and the losers are at the bottom, hopefully that's all it's going to take to encourage people to... fill in your jobs."

### Outputs and distribution
36. **A weekly spreadsheet to every PM on Monday; the meeting every two weeks.** J 47:15: "I would like to have spreadsheet with all this data go to each PM at the beginning of each week. We will review workload meeting every two weeks". J 47:36: "if this goes to everybody every Monday, the way it does now, with the data right in their face... And if those people really want to dig down... they can go to the app". J 47:56: "some people are just not going to go there... it doesn't mean that I don't want to put this under their nose every week." **[Ian]** I 47:23: "this is going to happen with dashboard and it's going to be an export to PDF or to, sorry, to spreadsheet".
37. **A separate drafters' version, maybe.** J 29:45: "an output cheat [sheet] like this for... the engineering workload meeting, and then maybe a separate one just for the drafters". J 30:04–30:25: drafters want "who's the drafting manager... What's the drafting budget hours? And are we on track or not?" Context, J 29:27: drafters "are going to have their own separate meeting". **[Ian/agreed]** I 30:27: "let's get this one built, and then we just... pare down"; J 30:37: "Let's get this one built and then yeah... that's what I thought."

### Ian's commitments
38. **[Ian]** Find out how Jeremy calculates budget hours. I 32:29: "I can look into the how he's querying things and calculating things"; I 32:50: "I'll look into the spreadsheet."
39. **[Ian]** Work with the data as it is. I 36:32: "We'll start small... We'll work with what we have." I 36:58: "I'll make it work, man."
40. **[Ian]** Timeline. I 59:07: "give me till like midweek". I 1:05:18: "I'll do another pass though this weekend and over the next early next week and buff it all up and then show you... midweek and then... a couple days to fine tune it for your workload meeting on Monday." The next workload meeting is "on the 23rd" (J 59:30).
41. **[Ian]** Demo the financials later. I 51:38 offered a financials demo to Jim and JM; Jim deferred it until after the workload work (item 2).

## 3. Open questions and undecided items

1. **Which fee columns?** Total, original and extra (Ian, 19:16), or only original and extra (Jim, 19:19: "or we could just show original fee and extra fee"). Not decided.
2. **What is percent billed a percent of?** Original fee or total fee (J 19:41–19:53, "we have to define it"). Jim leans toward original. Ian also floated percent of extras (19:20). Not settled.
3. **How do sub numbers map to original vs extra fee?** 01/02 can both be original contracts (e.g. two towers), while 03–06 are extras for other clients (J 17:09). How to tell them apart in the data was not resolved. Known data problems:
   - Gardena: billed under 01, with a zero-fee Gardena 2 (J 15:30–16:26)
   - John Bryson structures sub numbers differently (J 11:57; Ian offered at 12:12 to "exclude, separate all of JB's billings")
4. **The budget hours formula and its constants.** Jim wants fee only and no area (36:34) but does not know the right rate: J 35:26: "what is the right number to be using here? I don't know." J 33:24 mentions he is "talking to him on Monday" (probably Jeremy; unclear in transcript).
5. **Keep the summary row?** Undecided (J 37:01–38:34).
6. **Which metric goes on the historical trend plot?** Jeremy plots percent of fee unbilled. J 41:21: "maybe we track something else". Not decided.
7. **Staff utilization.** Ian proposed it (42:02, 42:14). J 42:04: "I don't really know how that gets measured, or if it's something we'd even want to measure"; J 42:23: "maybe we introduce that." Undecided.
8. **Next milestone.** Jim was tentative (question marks, 42:23), though Ian said he would include it (43:07).
9. **How to mark underwater jobs** (colour coding?), J 58:50. Open.
10. **Drill down to individual employees?** J 28:53 says probably not; J 1:04:37 wants it. Mild inconsistency.
11. **Phase sort direction.** Settled on "the way it is now" (J 1:01:27), but the order of that existing phase list was not spelled out.
12. **The priority / "hotter list" mechanism and its name** (J 1:02:54, "Maybe we have a priority called..." is cut off). Deferred by Ian to a later iteration (1:03:55).
13. **Comment column or Ian's committed action-item record?** (1:01:46 / 1:02:18). Not decided.
14. **The drafters' version.** Deferred until the engineering version is built (30:27–30:37).
15. **Keep both "fee per hour spent" columns?** Kept "for now" (J 54:15, 55:35); revisit later.

## 4. Other calls, documents, spreadsheets and people mentioned

- **Jim's markup** of the current spreadsheet, held in Bluebeam (I 8:06 and I 9:13: "I'm still looking at Bluebeam"; J 0:37 and J 18:12: "my markup"). It lists the columns to keep and add. It is not in this transcript.
- **The current hot-list / PM report spreadsheet**, built by **Jeremy** (I 21:47: "I'm gonna tell Jeremy that"; formula walk-through 34:09–34:37). It has a summary row across the top (37:35), historical data/plot columns (38:54–41:21) and Jeremy's sq ft per hour rates (31:29). Sent to everyone weekly (J 52:37).
- **Deltek**, which supplies:
  - hot-list checkbox, phase, CAD/Revit, PM and drafting manager (17:34–18:03)
  - project metrics: fee area, actual GFA, design area (J 23:06)
  - milestones (43:01)
  - elements and sub numbers (3:43 onward)
- **Old Deltek dashboards**: "active project list" and "efficiency summary", which Jim and Ian built earlier and abandoned (48:22–50:13). Jim listed the efficiency summary columns at 49:27. Ian does not want to sink more time into Deltek (49:17, 50:52).
- **Jim's February billing from Daler** (J 6:22, 7:20), used as the example for extras. Jobs used: Capstan Phase 3/4, 308.878-02 (9:16); 3099601, Intracorp, Gardena South 06 (12:16–13:12); Top Flight (1:33).
- **Ian's existing app dashboard and the financials work.** It has drill-down (29:08), historical trends (38:55) and fee-billed handling with the same extras problem (2:50). A separate financials demo for Jim and JM is still to come (51:38, 59:03).
- **People:**
  - **JM**: always top of the list
  - **Rory**: looked at the financials and gave the $92/hr cost figure from research with Daler (52:03, 54:45)
  - **Daler**: billing and accounting
  - **John Bryson**: numbers sub-projects differently
  - **Chelsea**: set up Gardena wrongly
  - **Kevin**: non-billable hours (21:47–22:04)
  - **Conor, Andrea, Griffin**: inconsistent GFA entry (22:09–22:32)
  - **Omar**: Gardena is listed under him (15:11)
  - **Glotman**: Jim's former firm, the source of the earned-fee and cost indicators (27:11)
  - **Mark**: not invited to the meeting (46:29–46:32)
- **Next workload meeting: "the 23rd"** (J 59:30), "not this coming Monday, but the next Monday after that".
- **Not about this tool:** 1:05:44–1:07:54 covers Ian's AI comments on a rules/guideline document, emailed to Jim. 1:07:54 to the end covers the drafting detail catalog (Simon, Nelson, Serban/"Sir Ben", Shabana, Michael). Neither topic is about the workload tool.

## 5. Coverage

I read lines 1–542 of 542, in four Read calls covering 1–140, 141–280, 281–420 and 421–542.
