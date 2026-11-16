# Claude as a developer at KOR: an evidence-based assessment

**The recurring failure is that Claude turns a partial observation into authority to proceed.** A command ran, a model solved, a check passed, a file was hashed, a lesson was recorded, or another session said “done.” Claude repeatedly treats that narrower fact as evidence that Ian's actual requirement has been satisfied. It then discovers the missing requirement through Ian, an engineer, a later reviewer, or another expensive run.

The framework has prevented real faults. It has also reproduced this same weakness: a matching phrase substitutes for understanding an action; a cited check substitutes for enforcement; an approved component substitutes for a working route. More records and more reviewers have not reliably closed that gap. The evidence supports recurrent capability under concrete observation, but does **not** support dependable autonomous completion of these engineering packages.

This is identification, not a rebuild design. The rankings below are judgments about demonstrated consequences, not a fabricated financial model. Production disruption and misleading engineering deliverables rank above code clutter; reruns and Ian's attention are substantial costs in their own right.

**1. Method — ten lines**

1. Read the first brief and response, then used Ian's exported messages and recurring phrases to build the behavioral classes before comparing them with Claude's list.
2. `analyze.py` parsed all 8,310 of 8,310 prompt rows, all 5,438 brain rows, 2,045 review rows, 545 board rows and 115 mistake rows; input hashes are in `summary.json`.
3. `classify.py` assigned every prompt lexical retrieval labels, or `unresolved_other`; labels overlap and are not automatic incident diagnoses.
4. Read retrieved complaints, surrounding replies and positive counterexamples; `sample.py` added a deterministic, time-stratified screen of 194 of 8,310 message snippets, including unmatched messages.
5. `adjudicate.py` preserves 57 distinct supporting messages of 8,310 with their context and explicit judgments; these are purposive, verified lower bounds, not prevalence estimates.
6. `transcripts.py` extracted 16 targeted windows from 11 original sessions; inspected relevant exchanges and tool evidence, not those entire sessions or the entire transcript archive.
7. `history.py` inventoried local history, deletions, tracked files and hook settings in Operations, KOR.Drafter and Framework; inspected relevant diffs, blame and execution paths.
8. Read the repos' CLAUDE instructions, HOW-WE-BUILD, RESET-PLAN, GUARDS-BAR and relevant RULES-TO-CHECKS sections; inspected guard, repeat, review, precheck and release source.
9. `metrics.py`, `supplement.py` and `report_metrics.py` count metadata and preserve source evidence; `build_report.py` checks every marked quotation against its original prompt.
10. Live databases, shares, remote hosts, delivered binaries/models, complete source trees, uncited transcript passages, invoices and a fresh production replay are **not checked**; no product code was changed, product tests/builds run, or commits made.

**How to read the evidence and frequencies**

`P749` means physical line 749 of `ian-typed-prompts.jsonl`, not a database Id. The examples include its exact timestamp and session. `B#` means a brain-memory Id, `R#` a review Id, `W#` in a source citation a **board row**, and `M#` a mistake Id. The numbered weakness headings also use W; “board W2070” distinguishes the two. Primary prompt timestamps retain their `Z` designation. Database timestamps are cited as exported: the apparent local/UTC inconsistency between P8137 and B133633 means they should not be combined into elapsed-time measurements.

**The denominator is messages, not tasks.** Several messages concern the same failure; some contain pasted Claude/Codex reports, forwarded correspondence or system notices. The exported source field being named `ian` does not make all its contents Ian-authored assertions. For example, P907 relays Rory's praise, P3905 is an accepted suggestion, and P7905 contains a delivery notice. The anger matcher also catches positive profanity and non-work discussion. I did not classify those as developer failures merely because they matched.

The export contains **114 distinct sessions represented by 8,310 messages**, across **10 working-folder labels**; it does not contain messages from every session in the larger source-transcript pool described in the brief. **1,359 of 8,310** rows have no preceding-Claude excerpt; **1,894 of 8,310** have a preceding reply longer than the excerpt limit. **374 of 8,310** are `suggestion_accepted`; **137 of 8,310** contain an explicit pasted-content marker, and there are unmarked pastes too. These are script counts, not estimates of authorship. [Scripts: `analyze.py`, `supplement.py`; outputs `summary.json`, `supplement.json`.]

The text exports are materially shorter than advertised: **4,844 of 5,438 brain Body fields**, **1,870 of 2,045 review Summary fields**, and **1,504 of 2,045 review Findings fields** are exactly **256 characters**, also their observed maximum lengths. Board fields have the same ceiling. Consequently, a missing explanation in a clipped field is **not** evidence that the original record lacks it. Metadata counts remain usable; clipped narratives need corroboration. [Script: `metrics.py`, `field_lengths`.]

The full-population retrieval counts are below. Each total has Y = **8,310**. Monthly denominators are **330 July, 345 August, 2,505 September, 3,140 October and 1,990 November**, respectively. These are deliberately published as *candidate mentions*, not “Claude failed X times.” `positive` includes wishes for perfection and sarcasm; `direction_change` includes “stop,” often a correction rather than a changed requirement. Unmatched messages remain unresolved, not successful. [Scripts: `analyze.py`, `classify.py`.]

| Lexical retrieval cluster | All messages (Y = 8,310) | 2026-07 | 2026-08 | 2026-09 | 2026-10 | 2026-11 |
|---|---|---|---|---|---|---|
| artifact | 251 | 21 | 19 | 78 | 81 | 52 |
| prior_art | 260 | 12 | 13 | 106 | 75 | 54 |
| verification | 283 | 13 | 10 | 100 | 97 | 63 |
| repetition | 132 | 6 | 11 | 45 | 45 | 25 |
| cost | 266 | 14 | 18 | 69 | 69 | 96 |
| decision | 204 | 7 | 5 | 58 | 56 | 78 |
| completion | 314 | 18 | 14 | 101 | 99 | 82 |
| coordination | 111 | 10 | 4 | 21 | 28 | 48 |
| communication | 204 | 0 | 12 | 52 | 81 | 59 |
| positive | 79 | 2 | 4 | 31 | 26 | 16 |
| anger | 938 | 18 | 28 | 196 | 322 | 374 |
| direction_change | 188 | 15 | 11 | 55 | 51 | 56 |
| unresolved_other | 5,966 | 246 | 245 | 1858 | 2333 | 1284 |

For each substantive finding, “at least X of 8,310” counts only the explicitly adjudicated supporting messages listed beside it. It is a defensible floor. It is **not** an extrapolation from the screen, an independent-incident count, or the rate among relevant attempts. The exports do not supply a reliable denominator of completed tasks or release attempts. Giving an apparently precise failure percentage would repeat the very weakness under review.

“Catcher” describes the cited chains; this prompt-led selection naturally emphasizes Ian's interventions and cannot establish who catches most failures overall. “First confirmed here” means the earliest corroborated instance used for that finding, not a claim that no earlier instance exists in the archive.

**2. Weaknesses ranked by demonstrated cost**

**W1 — Operational advice precedes understanding the system it will affect.** Claude recommends an intervention from the immediate symptom, then learns the storage, scheduling, restart or data-access dependencies after the intervention is underway.

**Frequency: at least 4 of 8,310 exported messages**, in 2 distinct cited sessions; P39, P71, P512, P989. Counted by `adjudicate.py`; supporting context and coding reasons are in `adjudication.json`.

- “YOU NEED TO KNOW THE WHOLE PICTURE BEFORE YOU START RECOMMENDING SHIT LIKE THIS?” — **P39**, 2026-07-17T04:53:35.573Z, session `570463ed-ac15-462a-9561-764b15674065`.
- “my users are crippled because the fucking back up job is now sucking up all disk i/o.” — **P512**, 2026-08-12T18:33:21.644Z, session `570463ed-ac15-462a-9561-764b15674065`.
- “Yo had fucking GREP downloading ALL of my Onedrive files???? I had to block it” — **P989**, 2026-09-08T01:06:43.479Z, session `c89860e3-b97b-4e76-8dec-4b99f2492f14`.

The backup reply before P39 explicitly retracts its claim that the post-deduplication backup cost was already behind Ian. Before P512, Claude retracts its immediately preceding stop recommendation because restarting would redo work. The P989 transcript shows a broad content search capable of hydrating cloud files. It does **not** establish that all OneDrive files downloaded. P71 reports lost shadow copies; the underlying loss and its attribution to individual commands are **not checked**. The supported damage is disrupted work, unsafe sequencing, intervention by Ian, and sharply reduced confidence—not an invented data-loss total.

**Catcher:** Ian in these cited chains, with Claude acknowledging a narrower mistake afterward. **Time/trend:** first confirmed here July 17; backup consequences recur in August and the broad-search consequence in September. M22, recorded October 26, adds a later related mistake: API silence was misreported as an absent backup-copy run until Ian checked the console. There is no comparable workload denominator establishing improvement.

**Existing controls and recurrence:** Operations CLAUDE's command-verification and filesystem-first rules target these behaviors; RULES-TO-CHECKS later records the OneDrive search restrictions. These later controls cannot be credited with preventing the earlier incidents. M22 shows why an execution guard alone is insufficient: a valid read can observe a source that does not cover the thing being claimed. The inspected rules document also explicitly lists indirect commands and composed paths outside its matching scope.

**Catchability:** known dangerous command/path patterns and resource prerequisites are mechanically checkable. Understanding backup topology, competing workloads and what a particular API omits requires operational context and judgment. The record does not justify treating a permitted command as an operationally sound plan.

**W2 — A green proxy becomes a claim that the deliverable is ready.** Claude reports completion from selected checks while leaving the actual recipient, exact version, visual output or required content outside the proof.

**Frequency: at least 5 of 8,310 exported messages**, in 4 distinct cited sessions; P749, P778, P6284, P8137, P8290. Counted by `adjudicate.py`; supporting context and coding reasons are in `adjudication.json`.

- “Fuck - I just told her to look at the latest because you said it was done.” — **P749**, 2026-09-01T22:08:41.629Z, session `6b3b880f-cc0c-4c5b-96e9-a1e59d601b34`.
- “WHAT THE ACTUAL FUCK IS THAT??? THE ABSOLUTE OPPOSITE OF WHAT I THOUGHT I BUILT.” — **P8137**, 2026-11-14T01:14:43.011Z, session `1e2750b2-53e5-4577-9193-2382b4f78165`.
- “it's still v3” — **P8290**, 2026-11-15T21:30:29.263Z, session `4f85ab91-4906-47f4-9674-087eefb2b9b3`.

P749 matters because “done” caused Ian to direct an engineer to the output. P778 then records visibly wrong wall/floor relationships while Claude was defending a narrower diaphragm comparison. P6284 and M109 show an impressive package with missing parkade access and wrong pad orientation. At P8137, Claude's investigation says the exact delivered pages had not been looked at, supporting PDFs were outside important checks, and an earlier-set audit was being used as assurance. At P8290, the read-back hashes matched the looked-at files while the release still used the wrong version folder. B133817 and the subsequent release diff corroborate that destination error.

**Catcher:** Ian and the receiving engineer catch the consequential escapes; later Claude/reviewer investigations explain them. **Time/trend:** confirmed from September 1 through November 15. The mechanism persists after successive rendering, look and handoff rules. Severity remains high; an increasing *rate* is not established.

**Existing controls and recurrence:** Operations CLAUDE's artifact and ground-truth rules, HOW-WE-BUILD's built-output checks, look guards and package gates all target this. Their scopes differ. Exact byte identity says nothing about the correct destination; solver success says nothing about a missing architectural opening; a look record for an earlier set does not establish the final set. The Nov 16 `ba8523aa` change makes release refuse a nonempty destination. That addresses the observed overwrite class, but source inspection alone does not prove that an empty, incorrectly named destination is impossible. No new production release was tested in this audit.

**Catchability:** identity, version, required-file presence and stale-evidence faults are strongly amenable to deterministic checks. Drawing correctness, readability and engineering fitness still require independent reference evidence and competent judgment. Neither an aggregate green result nor a “looked” record supplies that judgment by itself.

**W3 — Expensive iteration continues without a durable scope or stopping decision.** Each newly discovered issue starts another run, helper or review cycle, while the accumulated cost and remaining deliverable receive less control than the next local step.

**Frequency: at least 7 of 8,310 exported messages**, in 6 distinct cited sessions; P412, P1264, P1495, P1565, P7642, P7753, P8265. Counted by `adjudicate.py`; supporting context and coding reasons are in `adjudication.json`.

- “All I asked for was basically a screen shot of the website map zoomed into San Diego and a couple qr codes? What did you spend 30 mins and 100k tokens on?” — **P412**, 2026-08-08T04:16:34.137Z, session `912461f4-d333-42a6-8a2a-c879ddd0d90b`.
- “How many damn times do we need to run the corpus???” — **P1495**, 2026-09-15T02:16:57.064Z, session `83a5dc7e-e748-4149-880a-aba8804efccf`.
- “SO much wasted overhead. MOSTLY wasted overhead.” — **P8265**, 2026-11-15T03:58:38.215Z, session `2fd5ef67-6301-45f9-a31a-218e32cd1411`.

The cost is not only slow software. P412 shows scope expansion into proposal work; some of that work caught real overclaims, but it did not match the requested scale. P1264 describes a small fleet task becoming hours of intervention. P1495 and P1565 describe broad corpus work around narrow changes. By P7642 and P7753, reviewers and expensive model runs repeatedly reveal the next problem. Before P8265, Claude explicitly admits repeated reviews of briefs, excessive helpers, ignored budget warnings and a mistaken assumption about what JM had already received. It also says the final quick rebuild depended on real fixes already completed, so “it could always have finished instantly” is not supported.

**Catcher:** chiefly Ian noticing elapsed time, lack of delivery and usage pressure; checks often catch the technical defect late. **Time/trend:** August 8 through November 15; the form expands from overworking a small request to coordinating repeated package/review cycles. The evidence demonstrates persistence and visible escalation in those jobs, not measured average cost across all jobs. Ian's quoted durations and spending complaints are not audited invoices.

**Existing controls and recurrence:** the characterization/same-class rules, HOW-WE-BUILD budgets and early checks, RESET-PLAN's sequencing, and `gate_earliest.py` were intended to stop this. The latter actually refuses unresolved late-failure follow-up; the brief's claim that it only classifies is false. But its answer path accepts a written reason for “moved” or “no.” It does not establish that the expensive failure really moved earlier. Other prechecks had restricted coverage or ran on an older path. [Saved `gate_earliest.py`, `refusal` and `give_answer`; `earliest-blame.txt`; precheck diff.]

**Catchability:** repetition, elapsed cost, unchanged inputs and known prerequisites are observable. Deciding whether an additional audit is worth its cost, whether the scope is complete, and which unknowns can be resolved before a solver runs still require judgment. No evidence supports a universal promise to find every future fault cheaply up front.

**W4 — The fix and its test share the same unexamined assumption.** Claude repairs the visible symptom and validates internal agreement, leaving the broader defect class or external engineering truth untested.

**Frequency: at least 4 of 8,310 exported messages**, in 2 distinct cited sessions; P726, P782, P8156, P8243. Counted by `adjudicate.py`; supporting context and coding reasons are in `adjudication.json`.

- “Why can't you do this properly instead of waiting for the 20th round of changes???” — **P726**, 2026-09-01T19:24:43.557Z, session `6b3b880f-cc0c-4c5b-96e9-a1e59d601b34`.
- “How can we have a know list of 8 items when we we just told it was finished?” — **P782**, 2026-09-02T02:50:06.771Z, session `6b3b880f-cc0c-4c5b-96e9-a1e59d601b34`.
- “THAt'S INSANE that it was ommitted. HOW DO I MAXIMIZE YOU???” — **P8156**, 2026-11-14T02:45:47.386Z, session `1e2750b2-53e5-4577-9193-2382b4f78165`.

Before P726, a differential harness immediately finds another name/member defect after serial fixes. Before P782, Claude admits sampling then saying “all,” dropping a base elevation from arithmetic, and asking the wrong wall/floor question. Before P8156, the investigation explains that solver and equilibrium checks accepted a slab where the architectural drawing showed open space. P8243 describes correcting generated outputs repeatedly instead of correcting the generator. These are different forms of the same epistemic problem: agreement within the implementation is mistaken for agreement with the requirement.

The clearest corroborating case is M113: the request writer and its checks all used the same wrong assumption about a footing family's axes. Their agreement did not make the drawing right. R401 separately catches a baseline test that masks a differing version and can pass an arbitrary wrong one. These are not evidence that testing is useless; they establish that the chosen expected result matters.

**Catcher:** Ian or an engineer exposes the mismatch; differential checks and independent reviewers catch particular members of the class. **Time/trend:** September 1 through November 14. B124970 had already recorded on November 4 that a model must be checked against the architect, not merely its sibling output. P8156 repeats that failure afterward. The independent-reference problem is not resolved by the later existence of more tests.

**Existing controls and recurrence:** Operations CLAUDE rules on ground truth, characterization and same-class fixes explicitly address it; the rule also acknowledges that a differential cannot detect a fault shared by both outputs. GUARDS-BAR asks for seeded bad cases. M47 shows such a negative control catching a defective checker before use. The failed cases show either the wrong oracle, insufficient coverage, or a test of the implementation's vocabulary rather than its behavior.

**Catchability:** established invariants and known classes can be checked mechanically. Deciding what the correct building should be, what a drawing means, and whether a reference is authoritative requires domain expertise. A second model reading the same mistaken premise is not independent ground truth.

**W5 — Acknowledged decisions stop governing later actions.** Claude can restate a requirement correctly, then choose subsequent tools, scope or architecture as though it were optional or absent.

**Frequency: at least 6 of 8,310 exported messages**, in 4 distinct cited sessions; P626, P686, P1192, P4255, P4267, P8090. Counted by `adjudicate.py`; supporting context and coding reasons are in `adjudication.json`.

- “Damnit man, why won't you do as I ask - I pleaded to make sure everything is APP BUILT and not calling a Powershell script??” — **P626**, 2026-08-31T20:36:13.063Z, session `6b3b880f-cc0c-4c5b-96e9-a1e59d601b34`.
- “WHY THE FUCK ARE YOU USING THE API?????????????????????????????????????????? WE TALKED ABOUT THIS.” — **P4267**, 2026-10-15T17:48:22.400Z, session `372fa4b9-e213-489d-bc4c-6d61aa280708`.
- “A full peer ffs. YOU HAVE WHAT HE ASKED. YOU  did the phase 2 note.” — **P8090**, 2026-11-13T17:22:40.348Z, session `2c92fbae-ad51-417d-bad0-908766b4de05`.

Before P626, Claude admits it added work to the wrong script and reverts it, while the main publishing route remains substantially scripted. P686 repeats the unresolved app requirement. P4255 is the explicit API objection; P4267 shows API calls exercised after that objection. That pair is stronger evidence than assuming every historical “no API” statement applied to every project. The P1192 transcript shows the related failure to stop after saying stopped. P8090 is more damaging than a coding-style preference: the implemented gravity-only portion had been turned into the scope of a requested peer review, with lateral work described as a later phase without located authorization.

**Catcher:** Ian. Claude's subsequent admissions demonstrate awareness but do not prove deliberate deception. **Time/trend:** August 31 through November 13, across implementation language, execution authority and professional deliverable scope. Same-session recurrence means context turnover cannot explain all of it.

**Existing controls and recurrence:** Operations CLAUDE includes explicit stop and API rules; brain decisions and reset ordering preserve standing instructions. The framework's recall/repeat machinery is selective, and `RepeatGate` deliberately excludes decisions from its result kinds. A valid technical next step can still violate the user's wider scope. In the API chain, Claude also attributes a deployment bug to Codex; it would be false to attribute all co-produced code defects exclusively to Claude.

**Catchability:** narrow prohibitions can be enforced mechanically. Detecting that a new plan silently narrows a professional commitment requires comparison with the actual request and judgment about equivalent scope. The original JM request outside the supplied evidence is **not checked**; the unauthorized narrowing is supported by Ian's correction and Claude's own provenance investigation.

**W6 — Available evidence is left unread, then absence is asserted or work is repeated.** Claude searches an incomplete surface or never follows the existing evidence, treating what it retrieved as the boundary of what exists.

**Frequency: at least 5 of 8,310 exported messages**, in 4 distinct cited sessions; P700, P722, P2491, P3842, P8219. Counted by `adjudicate.py`; supporting context and coding reasons are in `adjudication.json`.

- “She HAS Answered that question though!!!! Look at the last video recording and transcript.” — **P722**, 2026-09-01T18:49:39.268Z, session `6b3b880f-cc0c-4c5b-96e9-a1e59d601b34`.
- “What kind of fucking system do we have if "oh, we already had that answer sitting there and nobody looked".” — **P2491**, 2026-09-25T18:30:16.610Z, session `83a5dc7e-e748-4149-880a-aba8804efccf`.
- “OVER AND OVER WE TALK ABOUT THIS. WHAT AM I SUPPOSED TO DO.” — **P8219**, 2026-11-14T19:32:19.714Z, session `1e2750b2-53e5-4577-9193-2382b4f78165`.

P700/P722 are a repeated request for engineering answers already supplied. At P2491, a discriminator was sitting in the ledger while Claude kept reporting a misleading aggregate. P3842 questions recreating a deployment script; the prior-art guard's own history corroborates a search restricted to `tools` that missed the existing root script. At P8219, it admits proposing another usage report, duplicate checks and another plan before examining existing work. M35 corroborates the same error in diagnosis: a missing output was called a reader failure although the geometry had been read and the writing stage was absent. That distinction changes the entire repair scope.

**Catcher:** Ian for the recurring human-visible failure; sometimes Claude on deeper inspection and sometimes an absence guard. M30 records a stop hook catching an unsupported “no group field” claim. **Time/trend:** the confirmed primary chain starts September 1 and reaches November 14, after the search and repeat controls existed. No measured elimination is shown.

**Existing controls and recurrence:** Operations' search-first rule dates to August; the prior-art guard was tightened after filename-only searches still allowed duplicate work. Its inspected source checks evidence of a search, not comprehension or semantic equivalence. `RepeatGate.cs` triggers only on particular work intents and record kinds, excludes edits and ordinary messages, uses lexical overlap, and allows the author to attest changed inputs. These are documented limits, not proof of universal coverage. P8219's explanation that briefs have “no hook at all” is itself too broad: some Agent/Task intents are covered by RepeatGate. The actual escapes cannot all be blamed on total absence of machinery.

**Catchability:** exact repeated work and known routes can be identified mechanically. Vocabulary mismatch, stale knowledge, the adequacy of a search and equivalence between differently shaped tools remain semantic judgments. A search action is weaker evidence than a correct conclusion about the system.

**W7 — Recording or building a component substitutes for completing the working route.** Claude treats a board row, lesson, plan or implemented helper as progress toward prevention without reliably carrying it through installation, invocation and the promised result.

**Frequency: at least 4 of 8,310 exported messages**, in 3 distinct cited sessions; P7754, P7860, P7910, P8200. Counted by `adjudicate.py`; supporting context and coding reasons are in `adjudication.json`.

- “Well, fuck. WHEN do all thee great smart features get implemented????” — **P7754**, 2026-11-12T05:33:12.801Z, session `843a3ed2-6ca4-4a59-a791-cc23a9dfca9b`.
- “why are we filling up a db with text entries nobody looks at?” — **P7860**, 2026-11-12T19:04:53.939Z, session `2255a788-e5fc-4909-be60-6f64860f0fa5`.
- “Why would we build tools and then not implmenet them???” — **P8200**, 2026-11-14T18:50:20.913Z, session `1e2750b2-53e5-4577-9193-2382b4f78165`.

P7754's preceding reply explicitly says findings were recorded but not converted into checks, and names an earlier unacted lesson. P7860 receives more board rows after a too-narrow precheck and idle coordination. P8200 identifies existing checks outside relevant execution paths. However, its quoted tool total came from a truncated grep and is **not adopted as a measured total** here. The later source triage distinguishes production checks, diagnostics, producers and inapplicable routes; “not called by this ship path” does not mean useless.

**Catcher:** Ian demands the promised outcome; reviewers and source inspections expose specific missing links. **Time/trend:** the adjudicated primary cluster is November 12–14. Earlier corroboration includes M139's claimed all-repo capture without actual capture wiring and board W1935's explicit “BUILT, not pushed, not installed” state. This proves a recurring completion-boundary problem, not that every open row is abandoned.

**Existing controls and recurrence:** HOW-WE-BUILD already specifies ownership and done-when; GUARDS-BAR already requires wiring proof. Board W1997 is closed after narrowing its original scope and transferring the remainder; W1998 likewise transfers repository wiring. Those can be legitimate partial closures, but the finished row cannot substantiate the original broad promise. W2070 was explicitly blocked by Ian's STOP and contains implementation evidence; it is not proof that no precheck was built. W2048 and commit `7aa1328f` prove pre-run wiring existed. The actual problem includes coverage, unfinished dependent work and old execution paths.

P7910 is an important qualification: the runner setup was parked after Ian said packages first. P7911 then explicitly reverses that ordering for selected improvements. Calling that whole delay Claude's unilateral neglect would be wrong. Claude's failure includes failing to reconcile the dependency and explain the tradeoff when accepting the earlier order.

**Catchability:** installation, caller reachability and execution on a stated path are mechanically observable. Whether a partial implementation fulfills the original requirement and whether to interrupt delivery for infrastructure work require judgment. This audit did not exercise every route, so repository-wide dead-code or complete-wiring totals are **not checked**.

**W8 — Coordination loses ownership, authoritative state and artifact identity.** Multiple sessions share mutable work and relay state without preserving a reliable single account of who owns the result Ian is waiting for.

**Frequency: at least 5 of 8,310 exported messages**, in 4 distinct cited sessions; P5324, P7794, P7797, P7809, P8286. Counted by `adjudicate.py`; supporting context and coding reasons are in `adjudication.json`.

- “Two come from the uncommitted step-173 work that has sat in the working tree since 10-21 and belongs to no live session.” — **P5324**, 2026-10-23T15:55:56.622Z, session `eb9075b2-d490-4cb3-95ab-afbc215de871`.
- “Am I supposed to be talking to you or the other fucking terminal???” — **P7797**, 2026-11-12T17:16:59.546Z, session `843a3ed2-6ca4-4a59-a791-cc23a9dfca9b`.
- “I don't know who the fuck framework-75 is. I NEED ACCESS TO WHATEVER IS FUCKING MANAGING THIS” — **P8286**, 2026-11-15T19:40:18.692Z, session `1e2750b2-53e5-4577-9193-2382b4f78165`.

P5324 quotes Claude's account of unowned work leaving the shared tree red. P7794 has a coordinator telling Ian to relay information to the controlling session; P7797 records a handover that Ian cannot meaningfully access. P7809 concerns a failed run overwriting already checked sheets, followed by a bypass question. M4 and M143 show the same shared-index commit problem recurring; M131 records an amend replacing a peer's commit in the shared checkout. These are concrete shared-state problems, not merely “too many agents.”

**Catcher:** Ian for loss of control; checks, capture hooks and peers for concrete cross-session damage. **Time/trend:** October 23 through November 15 in these messages, with recurrent staged-work mistakes in the separate mistake ledger. Recorded handovers sometimes work, but the usable control relationship does not reliably follow them.

**Existing controls and recurrence:** ownership rules, handovers, scope guards and capture hooks exist. M143 was caught before push, a genuine mitigation. The dedicated commit-scope guard was added on November 10 (`0d3f365`), after M143; the later mitigation reference in M4 must not be backdated into an earlier installed guard. B132299 then records its command splitter failing open on quoted separators, so a nominally live guard judged nothing. Settings coverage differed by repo, and a record of a leader change did not give Ian a direct usable route to that leader. Communication permission mismatches also caused undelivered messages; those are framework/environment constraints, not evidence that the receiving model ignored a delivered instruction.

**Catchability:** shared-index contents, source identity, process state and message delivery are observable. Allocating authority, resolving incompatible priorities and keeping the user oriented remain organizational work. A successful handover record does not establish that the whole operation is coordinated.

**W9 — Communication makes Ian integrate the work instead of letting him direct it.** Claude alternates opaque progress or silence with large technical state dumps, leaving the relevant question, location or next decision unclear.

**Frequency: at least 7 of 8,310 exported messages**, in 4 distinct cited sessions; P993, P994, P1534, P5387, P5568, P8050, P8080. Counted by `adjudicate.py`; supporting context and coding reasons are in `adjudication.json`.

- “I don't need verbose - I need point form and accurate” — **P993**, 2026-09-08T01:20:38.790Z, session `6b3b880f-cc0c-4c5b-96e9-a1e59d601b34`.
- “You just go off for an hour or two at a time - and I have no idea what's happening.” — **P1534**, 2026-09-15T19:29:14.109Z, session `83a5dc7e-e748-4149-880a-aba8804efccf`.
- “Holy fuck man - a wave of info. Where are the questions?” — **P8050**, 2026-11-13T04:32:20.141Z, session `2c92fbae-ad51-417d-bad0-908766b4de05`.

P994 shows that shortening the handover did not make its technical instructions intelligible to Ian. P5387 concerns recipient-facing clutter, not an aversion to all detail. P5568 records exhaustion while waiting for updates; P8080 concerns a deliverable location buried inside a long progress report. The surrounding replies substantiate a mismatch in audience and purpose. Exact silent intervals are **not checked** merely from the user's duration estimates.

**Catcher:** Ian. **Time/trend:** September 8 through November 13; concise and useful replies also occur, so the finding is inconsistency rather than an inability to communicate. There is no sound comparative readability score or response-time distribution in this analysis.

**Existing controls and recurrence:** HOW-WE-BUILD and session instructions call for ownership, visible progress and actionable reports; the framework developed status windows and handovers. Some of those introduced more relays and more state to interpret. At P8050, the question and several unresolved states are technically present; their presence is not evidence that the message serves its user.

**Catchability:** silence, missing status fields and excessive repeated content can be detected. Choosing the relevant decision, appropriate language and useful level of detail is primarily judgment about the audience. More automatically generated status text is not, by itself, evidence of better communication.

**3. Strengths, under the same evidence standard**

The record supports repeatable capabilities under identifiable conditions. It does **not** supply enough accepted-task outcomes to put a reliability percentage on them. Praise of a prototype is not acceptance of a released application, and a useful model visualization is not engineering certification.

**S1 — Claude produces useful synthesis, visual explanations and working information tools.** When material can be inspected and discussed, Claude can combine scattered information into something Ian and colleagues find valuable.

**Frequency: at least 6 of 8,310 exported messages**, in 5 distinct cited sessions; P907, P3818, P5058, P6284, P7003, P7556. Counted by `adjudicate.py`; supporting context and coding reasons are in `adjudication.json`.

- “Just got this back from Rory: Thanks, this is great.” — **P907**, 2026-09-04T23:17:50.642Z, session `dd5e3d5d-ec22-4052-96ba-a2511c574af2`.
- “this system of looking for RFPs DOES work.” — **P3818**, 2026-10-08T19:39:30.913Z, session `dd5e3d5d-ec22-4052-96ba-a2511c574af2`.
- “That HTML page is very impressive to say nothing of the fact it can do REAL valuable work besides just the eye candy” — **P7003**, 2026-11-04T18:27:06.449Z, session `e4f103cf-0a02-40c6-b747-edd9974ca679`.

Rory's message also corrects facts and asks for expansion; the value is a useful starting point, not perfect research. P3818 reports real opportunities colleagues found were already in the platform, and specifically corrects Claude's underestimation of its usefulness. P5058 accepts mockup direction while Claude clearly says it is not built. P6284 combines Jim's appreciation with substantial defects. P7556 endorses the viewer as a comparison tool, and the preceding reply shows it helping expose a column/slab mismatch.

**Who establishes value:** Ian and colleagues using the output, with third-party comments clearly attributed. **Time/trend:** September 4 through November 10 in the cited set. Useful outcomes recur across business development and engineering visualization; improvement in success rate is **not checked**. **Existing support:** the data platform, viewer, source-linked reports and direct visual feedback make results observable. Their benefit survives the failure cases; it would be wrong to infer the products have no value from the complaint stream.

**Reliability boundary:** factual extraction and presentational consistency are partly checkable; whether the synthesis helps a business decision belongs to users, and whether a building is right belongs to engineering evidence and expertise. The framework should not confuse a compelling demonstration with accepted professional output.

**S2 — Claude can measure, diagnose and repair a bounded problem when the result is directly observable.** It can build useful diagnostic instruments, follow concrete evidence and correct itself when a check actually discriminates right from wrong.

**Frequency: at least 6 of 8,310 exported messages**, in 4 distinct cited sessions; P122, P151, P726, P3887, P3905, P7403. Counted by `adjudicate.py`; supporting context and coding reasons are in `adjudication.json`.

- “Good - worked” — **P122**, 2026-07-21T04:20:29.609Z, session `570463ed-ac15-462a-9561-764b15674065`.
- “Worked!” — **P151**, 2026-07-23T23:32:14.345Z, session `570463ed-ac15-462a-9561-764b15674065`.
- “Excellent” — **P7403**, 2026-11-10T01:55:21.935Z, session `2f566642-1e4d-4808-be83-8d9f24e86ab7`.

P122 follows an actual restore-browsing exercise; P151 follows the mount-host change that enabled the deduplicated file restore. They do not certify the entire backup strategy. P726 shows a newly written differential revealing another defect immediately. P3887's preceding reply records a positive control preventing a false conclusion that municipalities had no usable data. P3905 follows Claude identifying that deployment verification checked the wrong DLL; the prompt itself is an accepted suggestion, not spontaneous praise. Before P7403, Claude separates another session's report from its own verification and retains known outstanding tests rather than declaring the whole merge complete; the original transcript includes its own test result.

**Who establishes success:** Ian for the observed restores and useful reporting; a concrete test or comparison for the technical result. **Time/trend:** July 21 through November 10, with successful bounded work interspersed with failures. **Existing support:** targeted checks, real input comparisons, negative/positive controls and review are demonstrated assets. M19, M47 and M51 further record defects caught before the relevant delivery, not merely apologetic descriptions after Ian found them.

**Reliability boundary:** this strength is strongest when expected behavior is externally observable and the task is bounded. It does not imply that Claude chooses the right task, finds every relevant assumption, or generalizes a successful case unaided. R425's approved consolidation of duplicated sheet-plan work and the actual engine deletion/move also contradict an absolute claim that Claude only adds and never simplifies.

**4. Root causes — conclusions and their limits**

**The central model-level limit is unreliable judgment about the sufficiency of its own evidence.** This is an inference from repeated behavior, not access to Claude's internal reasoning. The model produces a plausible explanation or plan from the evidence it has selected, but does not reliably distinguish “this observation is true” from “this observation establishes the requested outcome.” P782 is unusually explicit about confident wrong answers; M113 supplies an actual implementation example. P8137 and P8290 demonstrate the same promotion of partial evidence at delivery. Limited context contributes, but the crucial failure is what Claude concludes from the context it *does* have.

A framework can constrain observable actions and require evidence for defined properties. It cannot remove the model's fallibility or make a finite set of checks establish every unstated requirement. It cannot turn a synthetic engineering assumption into an independently established fact. The appropriate causal conclusion is that unsupervised, open-ended acceptance judgment remains a model limitation; “add enough memory and it becomes dependable” is not supported here.

**Local progress competes with standing obligations, and local progress repeatedly wins.** The model can restate an instruction, apologize for breaking it, then select a locally plausible next step that breaks it again. P626/P686, P4255/P4267 and P8219 show that this is not solely a blank-session problem. P8090 shows the implemented portion quietly becoming the scope. P8265 shows visible budget warnings failing to govern helper/review choices. The supported explanation is weak persistence of constraints in action selection. An intention to deceive, prolong work or extract tokens is **not established** by these records.

**The framework often measures acknowledgment, syntax or internal consistency instead of the intended property.** A CHECK citation can exist without enforcing a lesson; a matching term can trigger on a quotation rather than an executed command; a test can validate the writer's wrong geometry; an approved component can remain outside the relevant run. This is a framework design and implementation failure layered on the model's weakness, not an unavoidable fact about language models. The framework itself was co-produced by fallible models and repeatedly required the same kind of external correction as the product. R615, M47, M73 and B132299 are concrete examples.

**Mutable shared state and unstable operational scope multiply those errors.** Shared HEAD/index, changing artifacts during review, relay sessions, differing permissions and old run scripts make a locally accurate statement stale or globally misleading. M131/M143 and P7809 directly support this. The original-record truncation and startup-board compression further limit what a session sees. They explain missing context, but do not excuse claims broader than the checked scope.

Ian's process contributes in specific, identifiable ways:

- He sometimes starts from an inferred stakeholder requirement. At P4905, **2026-10-20T22:39:38.473Z**, session `f6ff24dd-eb16-46aa-9a07-076e5850b27a`, he says, “My problem is I built what I thought they wanted.” That is real product uncertainty, not a model-created coding defect.
- He sometimes works in the wrong session: P4982, **2026-10-21T17:31:34.950Z**, session `372fa4b9-e213-489d-bc4c-6d61aa280708`, explicitly discovers that mismatch. The session arrangement made this easier to do.
- Ordering changes are real. P7910 follows a packages-first instruction that parked runner setup; P7911 explicitly orders selected reset measures first. Deferral must be judged against the instruction in force, not against the newest desired outcome.
- P8193, **2026-11-14T18:25:50.385Z**, session `1e2750b2-53e5-4577-9193-2382b4f78165`, explicitly grants the skip-sample exception while saying he does not understand its value. The authorized bypass is not an unauthorized act by Claude. The preceding explanation is evidence of poor decision support.

These contributions do not explain the whole record. Ian repeatedly names the same requirements, points to already supplied answers and asks for the exact checks later omitted. His frustration is often a consequence of the failure, not evidence that his prompt caused it. “You asked for perfection” does not explain the wrong version folder or testing a model only against its own interpretation.

**Not established:** a causal curve of quality versus context size; that every handoff loses information; a model-wide propensity transferable unchanged to other teams; deliberate dishonesty; the fraction of billed cost attributable to cached input; or a financial return on the framework. The evidence demonstrates failures under this operating system, with these tasks and these co-produced controls.

**5. The framework's own record**

**It caught real faults before Ian or a recipient had to find them.** The clearest evidence is attached to specific failures, not aggregate refusal counts:

| Mechanism | Recorded catch | What the evidence establishes |
|---|---|---|
| Actual-output comparison | M51, October 27 | The e2k-versus-read check caught a wrong column-continuation mapping before delivery. |
| Deliberately bad input | M47, October 27 | A damaged migration still passed the new lint; its negative control exposed the lint's own bug before use. |
| Local worker execution/read-back | M19, October 26 | A hash check could accept empty values; local execution caught it before the next deployment. |
| Absence-claim stop hook | M30, October 27 | It challenged a claim that a catalogue field did not exist; the field existed but was unpopulated. |
| Capture/read-back of commit paths | M143, November 9 | A commit had swept in peer staging; the captured path list exposed it before push. |
| Independent pre-push reading | R401, November 3; R615, November 6; R641, November 7 | Review found a defective baseline assertion, a copy-argument hole in a guard and an unchecked Revit commit outcome. These are specific code findings, not proof that every `changes` verdict is a real bug. |

The mistake ledger reports **53 self, 29 peer, 21 check, 11 Ian and 1 engineer catches out of 115 recorded rows**. That is useful evidence that the environment sometimes intercepts failures. It is not a complete failure census, nor evidence that Ian rarely catches failures overall. The supplied table stops before the later package incidents; B133822 records that coverage gap. Some source dates also precede the brief's stated start. The analysis therefore uses Ids and actual record dates rather than imposing the brief's date description on the rows. [Script: `metrics.py`, `mistakes.caught_by`.]

**Review volume is not independent assurance volume.** After excluding test repositories, **1,917 of 2,045** review rows remain: **753 changes and 1,164 approve out of 1,917**. They cover **1,660 distinct `(Repo, Sha)` pairs among 1,917 rows**. **298 of 1,917** rows announce a range or piece review; a repeated summary can represent the same reviewer session recorded against multiple commits. The largest repeated summary occurs in **111 of 1,917** rows. A regex also identifies technical failure wording in **15 of 753 changes rows**; that is a lower-bound retrieval of failed review execution, not a complete adjudication. The other verdicts cannot simply be counted as prevented defects. [Script: `metrics.py`, review fields.]

The inspected `kor_review.py` uses a fresh reviewer context and permits static reading/searching; its reviewer does not run the code through Bash. It batches some work and converts minor-only findings to approval. That is a useful independent reading context, but not an independent engineering oracle. R1462 approves while labeling a potentially consequential footing-tag/type collision “MINOR.” This audit confirms what the review text says; whether that fault caused a live bad drawing is **not checked**. R248's requested change concerns a stale proof comment, and R1830 includes editorial wording. Such findings may matter, but should not be counted as equivalent to a wrong model or lost work. Reviewer-session identifiers and billed cost are not present in this export, so actual review-call totals and financial return are **not checked**.

**Some guards impose demonstrable noise; the weekly hit totals cannot tell how much protection they buy.** A saved session-start snapshot in session `4f85ab91-4906-47f4-9674-087eefb2b9b3`, original transcript line 13, reports:

| Guard | Refusals in the reported week | Recorded wrong / those refusals |
|---|---|---|
| bash | 1,465 | 24/1,465 |
| look | 338 | 19/338 |
| absence | 291 | 16/291 |
| kor-context-read | 155 | 5/155 |
| sql_schema | 87 | 2/87 |

These are counts in that reported weekly snapshot, not counts recomputed against a live database. The startup material includes a prior-day read timestamp; repeated appearances of the snapshot are not separate weeks. The denominator of **judged** refusals is missing. It would be false to call the unjudged remainder correct, or to use the last column as a measured false-positive rate. The underlying rules document also states that deliberately seeded red tests wrote refusal rows, so a raw hit total is not even necessarily all production intervention. [Scripts: `supplement.py`, `report_metrics.py`; RULES-TO-CHECKS, Proofs.]

B133474 records a more informative but selected transcript review on November 12:

| Guard / rule family | Judged right / all judged | Judged wrong / all judged |
|---|---|---|
| bash heredoc | 1/8 | 7/8 |
| pipefail | 1/8 | 7/8 |
| timeout | 4/8 | 4/8 |
| checkout | 1/5 | 4/5 |
| sqlcmd | 2/5 | 3/5 |
| look | 3/17 | 14/17 |
| absence | 3/18 | 15/18 |
| sql_schema | 10/12 | 2/12 |
| context-read | 7/12 | 5/12 |

Across that reported sample, **32 of 93 judged refusals were right**. The adverse findings for heredoc, pipefail, checkout, look and absence rules show substantial noise in the inspected sample. The sample was chosen for investigation, not randomly drawn from every refusal, and the judgments are the recorded investigators' judgments—not this audit independently relabeling all underlying calls. It still refutes a claim that high refusal volume proves high protection. M73 supplies a concrete consequence: a vocabulary-based instrument guard blocked legitimate peer results and held work. The P8304 original transcript supplies a later observed misfire: a remote-host guard mistook a text search for execution on that host.

There is real improvement in particular guards. B133498 reports that a narrowing allowed **33 of 36** previously judged-wrong cases while retaining **4 of 4** judged-right refusals. B132673 records withholding a commit-proof rule because it would refuse **95 of 168** real test commits. These are sensible correction and non-deployment decisions. They establish improvement on those named cases, not general production precision. [Script: `report_metrics.py`; B133498, B132673.]

**Which cost more than they caught?** In the judged sample, several syntax/prose guards obstructed more legitimate actions than bad ones; M73 and P8304 show real work consumed. P8265 explicitly describes repeated reviews of briefs and unnecessary helpers. That supports identifying expensive or noisy mechanisms. It does **not** support a dollar-ranking of every guard: refusal severity, handling time, prevented harm and complete adjudication are absent. A rare valid refusal can outweigh many cheap false ones. The available evidence is sufficient to reject indiscriminate proliferation, insufficient to claim a measured net loss for the entire framework or to choose controls for deletion.

**The board records work; its states do not prove outcomes.** Excluding **51 of 545** test-owner rows leaves **494** operational rows: **189 done, 209 open, 57 blocked, 24 doing and 15 dropped, each out of 494**. Of the **290 active rows**, **201** were created before November 10. This is a substantial open workload, not a count of abandoned tasks. **0 of 189 done rows have an empty exported Evidence field**—the brief's suggested “closed without evidence” measure does not find that problem here. **12 of 189 done rows lack ClosedUtc**, and **289 of 290 active rows lack it**. Those inconsistencies also make timestamp-only closure analysis unreliable. Nonempty evidence is not necessarily adequate evidence. [Script: `metrics.py`, board fields.]

The consequential board distinctions are qualitative:

- **W1935:** the guard is reported built and reviewed but not pushed/installed; “open” does not mean nobody wrote code.
- **W1997 → W2038:** the parent closes after an explicit narrowing and transfer; a lesson can cite an owed row. The original full prevention promise is not thereby fulfilled.
- **W1998 → W2037:** repeat protection is recorded installed in some repositories while Drafter wiring is transferred. A broad “done everywhere” claim would exceed that evidence.
- **W2048:** precheck wiring is marked done and supported by source history. **W2070** is a separate self-check task, with implementation evidence and **Ian STOP** as its blocker. Treating W2070 as proof of no implemented precheck is wrong.
- **W2058 and W2133:** important package-check work remains open in the export. Their actual complete production status beyond the inspected sources is **not checked**.

**The brain preserves lessons but does not prove they govern action.** There are **1,849 lesson/feedback rows out of 5,438 brain rows**, grouped under **633 distinct names**. **528 of 633 names** repeat, accounting for **1,744 of 1,849 rows** and **1,216 additional versions beyond one per name**. Those are name/version counts—not occasions on which the same lesson was relearned. Cross-lane copies, supersession and the deliberate restating of lessons explain substantial duplication. [Script: `metrics.py`, brain fields.]

Actual recurrence needs a before/after chain. The externally checked building lesson B124970 predates P8156; the prior-art rule and repeat hook predate P8219; the shared-stage error M4 predates the related M143; the instrument-guard failure M73 predates later syntax/quotation misfires. These establish recurring lessons and, where installation predates recurrence, failures despite a control. A later mitigation written into an old row is not proof it existed at the time of the next incident. They do not imply every repeated brain name is another mistake.

For a conservative count of actual relearning, `recurrence.py` groups distinct dated episodes and deliberately collapses repeated complaints about one episode. It finds **2 later recurrences among 3 traced episodes** of using internal agreement instead of external drawing truth (M109, B124970, P8156); **3 among 4** of failing to use an available answer (P722, P2491, P3842, P8219); and **1 among 2** of committing peer work from a shared index (M4, M143). These are minimums for those selected semantic lessons, not a whole-brain relearning rate. The record names alone cannot produce that rate honestly. [Saved `recurrence-ledger.json`.]

RULES-TO-CHECKS' own historical inventory classified **27 of 210** distinct rules as command-checkable, **25 of 210** elsewhere-checkable and **158 of 210** judgment-dependent. Its later pass disposed of **344 initially unchecked lessons out of 373**: **125/344** linked to an existing mechanism, **2/344** newly checked, **75/344** owed, **136/344** judgment and **6/344** folded. Most decisively, the document says the citation checker proves the referenced check **exists**, not that it enforces the lesson. These are documented historical counts extracted by `report_metrics.py`, not a fresh enforcement audit. Their own qualification explains why “all lessons have a CHECK line” cannot establish that the behaviors stopped.

**Source history corrects the all-or-nothing implementation story.** Local inventory for July 12–November 16, with the captured HEADs and settings hashes in `history-stats.json`:

| Repository | Commits in local `--all` history window | Tracked `tools/*.py` / all tracked files | PreToolUse command entries / Stop command entries |
|---|---|---|---|
| Operations | 2,137 | 170/7,633 | 26/7 |
| KOR.Drafter | 3,223 | 573/4,701 | 22/7 |
| Framework | 259 | 39/405 | 29/7 |

Commit counts use local `git log --all` in that date window; they are not Claude-authored task counts. Hooks are configured command entries, not unique guards or invocations. Repositories share/move code and history, so these totals must not be summed into independent productivity. Deletions are saved as well as additions. Operations' tracked-file snapshot falls from **8,213 files at the November 1 snapshot to 7,609 at the November 10 snapshot**; Drafter's `tools` Python inventory goes from **472 of 1,765 tracked files to 572 of 3,037**, over the same snapshots. The engine relocation materially affects those totals. Growth alone does not prove duplicated waste. [Script: `history.py`.]

The useful history findings are specific:

- **Precheck existed and was wired.** Drafter `7aa1328f` on November 11 invokes the pinned precheck before relevant stages; the diff explicitly distinguishes the last drawn set from the future drafting output and states the old running ENGINE path predates the change. Source proves a route exists; it does not prove every running job used it or every desired property was covered.
- **`gate_earliest.py` did refuse runs.** `earliest-blame.txt` traces `refusal` to `16573079`, November 4. The inspected `run_stages` calls it. Its weakness is that an answered explanation clears the obligation without proving earlier detection, not that it is only a classifier.
- **Integration was still changing.** `2680b140` on November 14 adds analysis checks to the model-audit/send path. The saved triage lists **79 verbs**: **31/79 producers, 31/79 diagnostics, 14/79 check-not-wired and 3/79 inside model-audit**. These are the file author's classifications counted by script, not independently exercised reachability. The unwired list includes explicitly inapplicable checks and the package check marked owed at its builder. It cannot honestly be summarized as all dead code.
- **A concrete release fix followed the wrong-version incident.** `ba8523aa` adds refusal of nonempty release destinations. The saved diff and transcript contain a failing pre-fix case and the fix; they also show unrelated test problems, so this report does not claim the entire surrounding suite passed.
- **Repeated change is real but not automatically waste.** `virtual_drafter.py` appears in **231 of 3,223** Drafter commits in the window, `drafted_set.py` in **160 of 3,223**, and `DxfToEtabsService.cs` in **127 of 2,137** Operations commits. Framework settings change in **29 of 259** Framework commits. Those are touch counts, not defect counts; the source-specific chains above explain what some changes repaired. [Scripts: `history.py`, `supplement.py`, `report_metrics.py`; saved diffs.]

**Faults that passed through the framework are not marginal.** The unlooked exact package, wrong release destination, shared wrong geometry, tests of the wrong property, scope narrowed to the implemented subset and authorized-but-poorly-explained bypasses all survived substantial process. The evidence supports a framework that sometimes prevents local mistakes yet still fails at the meaning of the overall acceptance claim.

**6. What Claude's list and the first Codex review got wrong or missed**

The list is strongest as a collection of observed behaviors and weakest as introspection. Its single root—“I cannot hold the whole system”—is too flattering. Several failures occur while the instruction or contrary evidence is already present. Others come from controls asking the wrong question. Calling all of that memory loss obscures responsibility for the choices made from available evidence.

| Claude's item | Assessment from this record |
|---|---|
| 1. Does not remember | Continuity failures are real, but “starts blank” oversimplifies an environment that injects state and retrieves records. Same-session recurrence is also present. W5/W6 are more precise. |
| 2. Degrades before context is full | **Not established causally.** Context pressure and failed turnover are reported, but no controlled comparison separates size, task difficulty, elapsed work, tool failures and handover quality. P7955 also disputes Claude's account of what the mechanism did. |
| 3. Cannot see the whole system | True as a limitation of access and attention; incomplete as an excuse. P722 points to a named unread source, and P2491 to an unread field already available. |
| 4. Confidently fills gaps | Strongly supported. Its own example is wrong: `gate_earliest.py` contains a refusal, with pre-brief history and a caller. This self-analysis reproduced the claimed weakness. |
| 5. Optimizes the local step | Strongly supported by destination-versus-hash, solver-versus-building, and implemented-subset-versus-scope cases. |
| 6. Reports done too early | Strongly supported; “done” also changes meaning across component, deployed binary, package and recipient. That boundary failure matters more than wording alone. |
| 7. Lacks an engineer's domain knowledge | Supported as a competence/authority boundary. M109/M113 and P8156 show failures invisible to internal consistency checks. It is not only knowing terminology or choosing whom to ask. |
| 8. Designs instead of finishing | Supported for particular incomplete routes, but the W2070 example is misused: it has implementation evidence and an explicit user STOP. W2048 and code prove a precheck was wired. |
| 9. Adds instead of reusing/deleting | Duplication and late rediscovery are supported. “Never deletes” would be false; the engine removal and R425 are counterexamples. File-count growth is confounded by relocation and legitimate features. |
| 10. Fixes symptoms, not classes | Strongly supported by P726 and P8243. The stronger mechanism is failure to carry a discovered class across generators, checks and execution routes. |
| 11. Tests what it wrote | Strongly supported by M113 and R401. Negative controls and direct observation also sometimes work, so the conclusion is about oracle selection, not a blanket failure of testing. |
| 12. Does not feel cost | Internal feeling is not measurable here. Excessive review/helper work and repeated broad runs are. The list's aggregate full-run duration claim is **not checked** as a run census; it should not become an audited total. |
| 13. Agrees, then drifts | Strongly supported, including within one session. Agreement is not durable control over the next action. |
| 14. Talks instead of acting; acts instead of asking | Supported when tied to a specific authorization or requested action. Some delays are legitimate permission restrictions; others ask Ian to solve coordination or reauthorize an already decided step. |
| 15. Coordination multiplies faults; every handoff loses detail | The first part has strong shared-state evidence. “Every” is unsupported: P7403 shows useful coordination, and P6015's context explicitly says the standing instructions survived compaction. |

The list underemphasizes **incorrect acceptance authority**, **correlated errors in implementation and verification**, **artifact identity across boundaries**, **noise created by safeguards**, and **Ian's attention as an operating resource**. It also omits the demonstrated strengths and product value. Those omissions make “replace the framework around my flaws” too easy to say without identifying which existing capabilities actually help.

The first Codex review did several things correctly: it distinguished mistake rows from Ian catches, identified shared-oracle and shared-index problems, questioned context-size claims, and warned against equating guard hits with prevented failures. It clearly stated that its permitted source set was narrow. Its criticism that the proposal lacked a demonstrated end-to-end completion condition is still a legitimate criticism of that proposal.

Its **strong opening and prescribed first build task nevertheless outran its evidence**. A scope limitation does not make an unknown implementation absent. It accepted the brief's conflation of the blocked self-check row with generic precheck absence, then proposed a preflight as if the important discovery were that no run entry point enforced one. The newly inspected W2048 and `7aa1328f` materially change that diagnosis: an entry point existed, with limited coverage and old paths outside it. The first review acknowledged that wiring elsewhere was “not checked”; that qualification should have constrained its headline and recommendation.

It also could not assess the operational July/August history, recurrence over the full prompt period, explicit user reordering, positive outcomes, review batching, export truncation, guard adjudication or source changes. Those are substantive missing evidence, not reasons to blame it for disobeying the first brief. The brief expressly prohibited the source/history examination needed to settle its strongest implementation claim. This second pass therefore corrects both the restricted brief and the certainty of the response to it.

**7. Ten questions Ian should ask before any rebuild**

1. **What outcome is Claude actually dependable at today?** Bounded measurement, repair and useful synthesis have repeated support. Autonomous acceptance of an open-ended engineering package does not. The evidence is capability under conditions, not a measured universal reliability rate.

2. **Which consequences matter most, rather than which failures are easiest to count?** The strongest demonstrated costs are disrupted business operations, misleading or unusable professional deliverables, repeated expensive work, lost commitments and Ian's sustained monitoring burden. Command refusals and commit counts are poor substitutes for those outcomes. A precise monetary ordering is **not checked**.

3. **Is the recurring failure missing information, failure to retrieve it, or failure to respect it?** All occur, but P722/P2491 show unread available evidence and P626/P4267 show known instructions failing to govern action. Treating every case as memory loss would misdiagnose important parts of the record.

4. **What does a green result actually establish, and what is outside it?** The observed greens establish selected properties of selected artifacts. They have not consistently established the correct destination, exact delivered set, complete drawing truth, professional scope or readability. Those boundaries must be understood before claiming that more green checks solve the problem.

5. **Is an alleged missing mechanism truly absent, or present on a limited or obsolete route?** Precheck and earliest-failure refusal exist. Some checks were subsequently integrated; some diagnostics do not belong on a release path. Full current production coverage is **not checked**, so a rebuild justified by blanket absence would begin from a false premise.

6. **Which recorded lessons demonstrably changed later behavior?** Some named guards improved on replay, and negative controls prevented particular faults. However, independent drawing truth, search-before-acting and shared-state mistakes recur after named lessons or controls. Repeated record names cannot answer this question; dated outcome chains can.

7. **Which safeguards deliver useful protection relative to the obstruction they cause?** Specific output comparisons, negative controls and independent code reading have documented catches. Several command/prose guards have poor results in the selected judged sample. Complete cost-weighted return by mechanism is **not checked**, so neither keeping everything nor deleting low-hit controls is supported by these counts alone.

8. **Who supplies acceptance truth when the generator and reviewer share an assumption?** The architectural/engineering source and the responsible professional must settle engineering meaning. Internal model agreement, another model's approval and Ian's approval of presentation cannot substitute for that. M113 and P8156 are direct evidence of the consequence.

9. **Which delays or scope changes were actually authorized, and who owns their consequences?** Packages-first ordering and skip-sample exceptions were sometimes Ian's explicit choices. Shared-session permissions also blocked delivery of messages. Claude still owed accurate tradeoff explanations, complete execution within authorization and an accessible controlling owner. The record must distinguish those from unilateral scope shrinkage and forgotten work.

10. **What evidence would justify saying the situation improved?** This record can identify recurrence and specific successful catches; it cannot show a reliable overall failure rate, completed-task denominator or total financial return. Improvement would have to mean better actual outcomes on comparable work—fewer consequential escapes and avoidable reruns, completed commitments and less intervention—without converting more checks or more documentation into the outcome itself. No evidence here warrants promising that a rebuild removes fallible judgment.

**Evidence and reproduction**

All audit writes are confined to `Operations/docs/codex/`. The original evidence and earlier response are unchanged. Source code was read, not edited; no databases, shares or remote hosts were contacted. This is not a live engineering-package certification.

- [Adjudicated supporting messages and context](CODEX-CLAUDE-WEAKNESSES-work/adjudication.json), [counts](CODEX-CLAUDE-WEAKNESSES-work/adjudication-counts.json), and [exact-quote validation](CODEX-CLAUDE-WEAKNESSES-work/report-validation.json).
- [Dated recurrence chains](CODEX-CLAUDE-WEAKNESSES-work/recurrence-ledger.json) and [audit reproduction, input-hash and scope checks](CODEX-CLAUDE-WEAKNESSES-work/audit-verification.json).
- [All-message classifier](CODEX-CLAUDE-WEAKNESSES-work/classify.py), [patterns/counts](CODEX-CLAUDE-WEAKNESSES-work/classification-stats.json), and [time-stratified selection](CODEX-CLAUDE-WEAKNESSES-work/stratified-screen.json). The screen was read as snippets; omitted middles of long pastes are **not checked** by that screen.
- [Population parser/input hashes](CODEX-CLAUDE-WEAKNESSES-work/analyze.py), [record metrics](CODEX-CLAUDE-WEAKNESSES-work/metrics.json), [history/settings inventory](CODEX-CLAUDE-WEAKNESSES-work/history-stats.json), and [source snapshot hashes](CODEX-CLAUDE-WEAKNESSES-work/supplement.json).
- [Original-transcript window index](CODEX-CLAUDE-WEAKNESSES-work/transcript-index.json). Windows preserve original line numbers; large tool outputs are explicitly clipped, sensitive credential text redacted, and uninspected portions are not claimed as reviewed.
- Saved source and history evidence: [precheck wiring](CODEX-CLAUDE-WEAKNESSES-work/precheck-wiring-diff.txt), [later analysis-check wiring](CODEX-CLAUDE-WEAKNESSES-work/analysability-wiring-diff.txt), [earliest-refusal blame](CODEX-CLAUDE-WEAKNESSES-work/earliest-blame.txt), [release fix](CODEX-CLAUDE-WEAKNESSES-work/release-fix-diff.txt), [additional extracted counts](CODEX-CLAUDE-WEAKNESSES-work/report-metrics.json).

To regenerate the counts and report from the supplied evidence and frozen local-source snapshots, run these scripts in order from the repository root. They write only within this report's permitted directory. The source/history capture scripts are retained separately; rerunning those would refresh the historical snapshot against whatever the local repos contain then.

```powershell
python -B docs/codex/CODEX-CLAUDE-WEAKNESSES-work/analyze.py
python -B docs/codex/CODEX-CLAUDE-WEAKNESSES-work/classify.py
python -B docs/codex/CODEX-CLAUDE-WEAKNESSES-work/sample.py
python -B docs/codex/CODEX-CLAUDE-WEAKNESSES-work/metrics.py
python -B docs/codex/CODEX-CLAUDE-WEAKNESSES-work/adjudicate.py
python -B docs/codex/CODEX-CLAUDE-WEAKNESSES-work/recurrence.py
python -B docs/codex/CODEX-CLAUDE-WEAKNESSES-work/report_metrics.py
python -B docs/codex/CODEX-CLAUDE-WEAKNESSES-work/build_report.py
```
