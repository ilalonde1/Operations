# Revit Team Meeting — Synopsis & Action Points

**Meeting:** "Revit team meeting before MLi departs" · **13 July 2026, ~8:31 PM · 50 min · Teams + Boardroom**
**Prepared:** by Ian Lalonde (Ops/IT) · **For:** drafting team follow-up
**Recording + transcript:** `Desktop\Revit - Michael\`

> **Quick disambiguation — two Michaels:** **Michael Li** = the departed Revit developer who wrote the plugins and took the source code. **Michael Mousa** (drafter) = present in the meeting, volunteering to test Revit 2027. The Teams transcript labels everyone in the room as "KOR Structural – Boardroom," so the two get blended — most "Michael" lines about *testing 2027 / installing on my machine* are **Michael Mousa**.

**Attendees (best identification):** Ian Lalonde (remote), Jim & John "JM" (principals), Lindsay, Rory Beirne, Simon Szarkiewicz (remote), Nelson Yu, Michael Mousa; others in the room (Chris, Kevin referenced). Michael Li did not attend.

---

## 1. Synopsis — what was discussed

**Context.** Michael Li is exiting early (personal/stress). He is effectively not working, and — critically — **he took the source code for all his custom Revit plugins** (copied to a thumb drive Tuesday night). He was secretive, held his work close, and had repeatedly removed IT/remote-access tools from his machine over the years.

**Project handoff.** Michael Li had almost no active workload. The one live item (a tilt-up with panel shop drawings) is largely done and already picked up. Nothing critical is mid-stream. → Pull his project list from Deltek to confirm nothing slips.

**Department management going forward.** Michael Li reluctantly "project-managed" but communicated poorly, leaving the team blind to each other's workload. **Decision: all drafters join the existing bi-weekly Monday workload / PM meeting** (Teams, always-on) so everyone sees who's working on what, who needs help, and how new jobs get distributed. Drafters may optionally run their own drafting-specific check-in on alternate weeks. Ian will **demo the workload/PM tool** so drafters can track their own projects (filter by their name).

**The plugins problem (the core issue).** The custom tools are compiled DLLs on every machine, but **the source is gone**. When a new Revit version lands (2027), the tools break — the drafters believe it's mainly the **shortcuts/customizations** (families upgrade fine). Ian's plan: **reverse-engineer from a drafter's compiled tools, then rebuild the toolset clean, centralized, and version-"agnostic"** so future Revit releases don't break it. The team confirmed a key point — **they only use ~10% of the huge library**; most isn't needed. No panic on timing: **Revit 2027 won't hit production for ~6 months** (architects are delaying; some staying on 25/26).

**The opportunity.** Strong appetite to use AI ("Ian and Claude") to rebuild the tools better and add new ones drafters ask for — in plain language. Explicit goal: break the silos and rebuild both the tools *and* how the team communicates.

---

## 2. Decisions made
- All drafters **join the bi-weekly Monday workload meeting** (Teams).
- **Rebuild the toolset properly** — clean, centralized, version-agnostic — not patch-and-duct-tape per Revit release.
- **Follow-up demo meeting (Wednesday, ~2–3 PM, Teams):** drafters screen-share their Revit workflow (recorded) so Ian can see exactly what the tools do.
- **Install Revit 2027 on a test machine** for upgrade testing — not on a production machine.
- **Do not engage Solid CAD** (the wood-frame group) — too costly for the value; hire a specialist only for a specific problem if one arises.
- **Reach out to Michael Li** to ask for the source and a smooth transition (John/JM to try).

---

## 3. Action points

| # | Action | Owner | When |
|---|--------|-------|------|
| 1 | Install Revit 2027 on a **test** machine for upgrade testing | Ian | Right after the meeting |
| 2 | Test 2027 on a **duplicated (test) project**, document upgrade issues, send Ian the list | Michael Mousa | This week |
| 3 | Send Ian **point-form: what each Revit tool does for you** (layman's terms; email/Teams) | Each drafter (whoever knows best) | Before Wed |
| 4 | **Schedule + run the Wednesday demo** (~2–3 PM, Teams): each drafter screen-shares their workflow, recorded | Ian (invite) / drafters (demo) | Wed |
| 5 | **Demo the workload/PM tool** to drafters + provide a drafting-focused capacity view | Ian | Wed / ongoing |
| 6 | **Demo the new `KOR.RevitTools` foundation** (once tested/finished) | Ian | Wed / follow-up |
| 7 | **All drafters join** the bi-weekly Monday workload meeting | Ian (add them) / drafters | Next cycle |
| 8 | (Optional) drafters run their own drafting check-in on alternate weeks | Drafters | Ongoing |
| 9 | **Contact Michael Li** — request source on the thumb drive + smooth transition | John/JM (+ Jim) | ASAP |
| 10 | Pull Michael Li's **active project list from Deltek** to confirm nothing dropped | Ian / PMs | This week |

---

## 4. Where it actually stands now (update since the meeting)

In the meeting Ian said he was "going off nothing." That's no longer true — significant progress has been made, which reframes Wednesday from "help me understand this" to "here's what we've already recovered and built."

- **Source partially recovered.** A portion of Michael Li's **real, un-obfuscated source** was recovered from shadow copies, and **every deployed tool (DLL) is preserved** and organized — see `P:\Recovery` (source, DLLs, decompiled core, families, templates, standards).
- **Full accounting done.** The 195 tool files collapse to **78 distinct tools, of which only ~22 are actually loaded** — confirming the drafters' "we only need ~10%." Cut list + rebuild SOW are written (`docs\KOR-Revit-Toolset-Accounting-and-Rebuild-SOW`). A **one-page drafter worksheet** is ready to hand out Wednesday.
- **The rebuild has a working foundation — `KOR.RevitTools`.** A clean, modern codebase that **already builds for Revit 2024 / 2025 / 2026 from one project** (version-agnostic, using the Revit API NuGet — no per-machine paths), with first-pass tools across the main areas (Rebar, Visibility, Text, Views, QuickInsert, Dimensions, Units, Structural, Elements). **It needs testing + finishing**, but it's a real, demonstrable proof of the exact "make it agnostic and clean" vision described in the meeting — ideal to show Wednesday.
- **Why the source is gone, confirmed.** Michael Li **deliberately obfuscated** his DLLs (with a tool called Obfuscar) — that's why they can't simply be read back, and why the physical thumb drive is the only route to the remaining source.

**Net:** the toolset is inventoried, partially recovered, and a version-agnostic replacement foundation exists and builds. Wednesday is about (a) capturing exactly what drafters use, (b) capturing what they *wish* they had, and (c) showing them the workload tool and the new foundation.

---

## 5. Wednesday follow-up meeting — proposed shape (~45 min, Teams, recorded)
1. **Each drafter: ~5-min screen-share** of their real workflow with the current plugins (what they click, what it does). Recorded.
2. **Gaps round:** what do you do by hand, repeatedly, that a tool should do? (Use the one-page worksheet.)
3. **Ian demos:** the **workload/PM tool** (so drafters can track their own jobs) + the **new `KOR.RevitTools` foundation** (the agnostic rebuild, already building for 24/25/26).
4. Confirm the bi-weekly workload meeting cadence.

---

## 6. Draft follow-up email (ready to send)

> **Subject:** Wednesday 2 PM — Revit tools working session (please prep a 5-min demo)
>
> Team,
>
> Thanks for a good discussion today. Quick recap and next steps.
>
> **Wednesday at ~2:00 PM (Teams, ~45 min, I'll record it)** we'll do a working session on our Revit tools. Two things I need from each of you:
>
> 1. **A ~5-minute screen-share of your own workflow** — just walk me through the custom tools/buttons you actually use and what they do for you. No prep slides; live is perfect. This is how I'll understand what to rebuild.
> 2. **Before Wednesday, a quick point-form note** (reply here or Teams): *what does each tool do for you, in plain terms?* Don't overthink it — bullets are fine.
>
> The bigger picture: Michael's tools are compiled onto your machines but we don't have his source, so they'll break on future Revit versions. Rather than patch them each release, **we're rebuilding the toolset clean, centralized, and version-agnostic** so it stops breaking. Good news — I've already recovered a chunk of the work and stood up a working foundation that builds for Revit 2024/25/26; I'll **demo that plus the workload tool** on Wednesday.
>
> Also: **going forward, all drafters are invited to the bi-weekly Monday workload meeting** (Teams) so we all have visibility on who's working on what and who needs help.
>
> And the fun part — if there's a tool you *wish* you had (something you do by hand over and over), bring it Wednesday. We can build it.
>
> Invite to follow. Thanks,
> Ian

*Adjust the time/attendees; Michael Mousa is separately testing Revit 2027 on a test machine and will send an issues list.*
