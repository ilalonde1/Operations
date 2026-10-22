## Ship-blocker
Rejecting a join can give another lead the forward owner's unrelated files and requirements: SqlPursuitGrabStore.cs:306-310 moves everything with a timestamp at or after the join, regardless of provenance. Replace the timestamp partition with explicit tender/join ownership before exposing rejection to leads (C1).

## Critical
- C1 SqlPursuitGrabStore.cs:306-325: rejection moves unrelated work and can strand tender work. Trigger: Alice uploads a forward-project drawing after Bob's tender joins, then rejects; Bob receives Alice's drawing and any manually added requirements. A tender file stamped before the join stays behind, and equal timestamps are all moved. Who sees it: both leads.
  Fix shape: persist tender/join provenance on files, fetches, reviews, runs, requirements and generated asks; identify the generated next step by identity, not matching text.
- C2 SqlPursuitOverwatchStore.cs:158-194; BdDirectorWindow.Home.cs:161-168: reassignment can change another pursuit's tender owner. Trigger: a card holds E/O/Alice; rejection moves O into Bob's new engagement while E remains Alice's; giving stale E to Carol updates O to Carol but leaves Bob's engagement owned by Bob. The inverse, a stale NULL OpportunityId after a join, leaves the tender with the former owner. Who sees it: leads and manager.
  Fix shape: read the current OpportunityId under the engagement's write lock and use that value for the opportunity update, log and notice.
- C3 SqlPursuitGrabStore.cs:264-285; DirectorActions.cs:82,101: decisions identify an engagement, not the join shown. Trigger: Director and Workspace show E/tender A; Workspace rejects A, tender B joins E, then the old Director card accepts or rejects B while displaying A. Neither SQL decision checks the expected owner against the actor. Who sees it: forward owner and grabber.
  Fix shape: pass JoinId, OpportunityId and expected owner; atomically settle that exact pending join, returning a conflict for stale cards.
- C4 MpiOwnershipReaperJob.cs:47-68: the reaper can archive a newly worked pursuit. Trigger: its first UPDATE finds E quiet and clears MPI ownership; a concurrent next-step update sets E's future due date; its second UPDATE archives E without rechecking activity or due date. No isolation level is selected and no UPDLOCK/HOLDLOCK protects the eligibility read of E; READ COMMITTED does not guarantee its stability. Who sees it: lead and manager.
  Fix shape: lock the eligible engagements through the transaction, use a consistent lock order, and make release/archive conditional on the same validated state.
- C5 SqlPursuitGrabStore.cs:386-398; SqlPursuitLifecycleStore.cs:71-97: concurrent forward/tender grabs create two live pursuits for the same project. Trigger: tender matching finishes with no candidate; a forward grab commits; the tender then runs GrabSql, which never rechecks forward pursuits. The separate transactions do not serialize this decision. Who sees it: both leads and manager.
  Fix shape: coordinate project identity and forward/tender creation under one transaction or shared per-project lock with an in-transaction recheck.
- C6 SqlPursuitLifecycleStore.cs:117-118,130-145,87-97; SqlPursuitGrabStore.cs:294: release permits duplicate live forward ownership. Trigger: Alice's E has a pending joined tender; release its MPI, grab that MPI again as Alice, then reject the original join. Both engagements now have NULL OpportunityId, NULL Region and live Grab links to the same MPI; neither supplied unique index prevents this. Who sees it: lead and manager.
  Fix shape: enforce one active ownership/link per MPI and make release/regrab explicitly reconcile any surviving joined pursuit.
- C7 SqlPursuitLifecycleStore.cs:111-119: releasing one linked project archives another project's pursuit. Trigger: a stage-1 E has Grab links to P and Q; releasing P archives all of E, while Q retains its owner and is no longer represented by a live pursuit. Restore only clears dismissal fields (187-202); it does not reconcile this ownership. Who sees it: lead and manager.
  Fix shape: define primary ownership or detach individual links; archive only after checking every active link and reconcile remaining MPI ownership.
- C8 SqlPursuitGrabStore.cs:285,294-300; PursuitRfpReview.cs:545-546: rejecting a submitted join destroys its displayed submission state. Trigger: advance a pending joined tender to stage 3, then reject; the split tender is stage 1, while the forward engagement remains stage 3 with no tender and disappears from the Director board. Who sees it: lead and manager.
  Fix shape: preserve the tender's submission state/history and explicitly restore the forward pursuit's appropriate state.
- C9 ProjectNameMatch.cs:40,65-67; SqlPursuitGrabStore.cs:457-461; OpportunitiesView.xaml.cs:340-345: automatic matching transfers an unrelated tender before either person confirms. Trigger: BC forward "Burnaby Fire Hall Replacement" and BC tender "Surrey Fire Hall Replacement - Prime Consultant Services" share 3 of 5 significant tokens, exactly 0.60; the tender joins Burnaby's owner. The grabber's disclosure is after commit; the owner's is on a later Today load. Who sees it: both leads.
  Fix shape: require corroborating municipality/buyer/project identity and resolve ambiguity before ownership or documents move; preserve phase identifiers.
- C10 PursuitRfpReview.cs:544-546: a live submitted tender silently falls off Today/Pursuits. Trigger: stage 3, no review recommendation, closing date eight days ago; the board excludes it even though the result is still outstanding. Who sees it: lead and manager.
  Fix shape: keep live submissions until resolved; separate decision urgency from board membership and expose any intentional date restriction.
- C11 KorBidFit.cs:31-56; KorProjectFit.cs:25-45: default relevance filtering silently removes genuine building opportunities. Trigger: "Prime consultant services - Main Street Elementary School Replacement" hits street, and an Infrastructure "Riverbend Library Expansion" lacks a Building-regex token. The ten hidden examples below follow the deciding branches. Who sees it: leads.
  Fix shape: distinguish service scope from place/building names, expand building vocabulary, and validate these adversarial examples independently of the tuning corpus.
- C12 SqlCompetitionInfoQueryStore.cs:37-39,93,147: relevance filtering happens after TOP. Trigger: 15,000 newer non-building records precede an older school RFP within the SQL filters; KOR markets returns zero and never reaches the school. "188 rows" is the number surviving the capped prefix, not evidence of 188 total relevant archive records. Who sees it: leads and manager.
  Fix shape: filter before limiting, or page until relevant results are exhausted/capped and return total/truncation metadata.
- C13 BdWorkspaceWindow.Today.cs:38-43,56-69: an early identity fallback can hide the owner's work for the entire window session. Trigger: first open occurs before signed-in UPN is available, no override exists, and Environment.UserName is "ilalonde"; Actor caches that bare login and later ignores the resolved full UPN, while the supplied live-owner baseline is entirely UPNs. Who sees it: lead.
  Fix shape: wait for a resolved actor or show identity unavailable; invalidate and reload when identity resolves.
- C14 BdWorkspaceWindow.Today.cs:35,53-74: Today silently retains empty, stale or partially refreshed data on failure. Trigger: board loading succeeds but PendingJoinsAsync throws; only a log entry is written, so missing confirmations or old joins remain without a visible failure state. Overlapping fire-and-forget loads also have no generation check. Who sees it: lead.
  Fix shape: load a coherent snapshot with cancellation/generation checks; surface partial-load failures and disable stale actions.
- C15 BdDirectorHomeViewModel.cs:354,440-462: the question summary removes decision criteria and conceals questions. Trigger: "Do we meet the minimum (3 comparable school projects)?" loses the minimum; four questions on the first line become "three questions", and questions on subsequent lines are ignored. "(see RFP 6.1)" remains because its prefix is not recognized. Who sees it: lead and manager.
  Fix shape: use structured questions/citations, preserve substantive parentheticals, and display the full count plus an explicit remainder.
- C16 CrmViewModel.cs:817-826; SqlPursuitGrabStore.cs:328-329; SqlPursuitOverwatchStore.cs:192-194: history names the wrong person as decision-maker. Trigger: Ian reassigns Alice's pursuit to Bob: it reads "Given to Bob by Alice"; Alice rejects Bob's join: it reads "Bob sent the RFP back". These use FromStaffId/ToStaffId instead of ByStaffId. Who sees it: leads and manager.
  Fix shape: render the recorded actor separately from the previous/new owner.
- C17 SqlIndustryEventStore.cs:92: incomplete durations and server-local "today" silently hide ongoing events. Trigger: an Oct 20-22 conference has StartDate Oct 20 and NULL EndDate; it vanishes Oct 21. Separately, with a UTC-configured SQL server, an Oct 21 Vancouver event disappears at 17:00 PDT when the server date becomes Oct 22. Who sees it: lead.
  Fix shape: validate multi-day end dates, expose unresolved duration, and compare against an explicit event/business-local date; server timezone was not inspected.
- C18 CrmViewModel.cs:116-118,454-456: "Mine" can show everybody's work without saying identity lookup failed. Trigger: load with Mine enabled and CurrentActorUpn blank; all owners pass and the footer merely says "Loaded N pursuits". Who sees it: lead.
  Fix shape: show an explicit identity-unavailable state and require a deliberate switch to everybody's pursuits.
- C19 CrmEngagementRowView.cs:34-36; OverwatchRowView.cs:62-64; BdDirectorHomeViewModel.cs:652-653: bid-state labels assert facts stage 1 does not establish. Trigger: a stage-1 tender closed yesterday still reads "Out for bid"; a person holding three forward projects with no tender gets "3 open tenders" on Team. Who sees it: lead and manager.
  Fix shape: derive open-tender status from tender identity and deadline, distinguish overdue/unsubmitted work, and exclude forward rows from tender counts.
- C20 BdDirectorWindow.Home.cs:167-191; SqlPursuitOverwatchStore.cs:196-197: committed reassignment and its notice can diverge. Trigger: the app exits after commit and before SendAsync, so no notice is requested; if SendAsync throws instead, the catch says "Could not reassign" although ownership already changed. Who sees it: new owner and manager.
  Fix shape: enqueue an idempotent notification in the ownership transaction and distinguish committed reassignment from notice failure.

## Material
- M1 SqlPursuitGrabStore.cs:228-243; BdDirectorHomeViewModel.cs:578-583: a rejected join remains a new-join card. Trigger: join then reject before any Keep/Reassign; the old Join remains the maximum Grab/Join Id on the forward engagement, producing "The RFP joined..." alongside "no RFP yet". Who sees it: manager.
  Fix shape: retire the specific rejected join from New grabs and derive the card from current ownership/tender state.
- M2 KorBidFit.cs:34-56; KorProjectFit.cs:29-45: KOR markets includes visibly irrelevant supply, road and energy work. Trigger: the ten shown examples below; Structural overrides ordinary exclusions, while unrecognized sectors default to true. Who sees it: leads.
  Fix shape: add service/supply distinctions, normalize composite sectors and evaluate unknown sectors explicitly.
- M3 MajorProjectsInventoryViewModel.cs:198-216: completion forces an obsolete selection back onto the screen. Trigger: grab A, select B during the await, then A completes; Selected becomes A even if a reload removed A from Projects. The captured database action targets A, but the displayed selection jumps back. Who sees it: lead.
  Fix shape: update the matching collection item, and replace selection only if it still refers to the captured row.
- M4 CrmViewModel.cs:141-147,182-206,452-456; MajorProjectsInventoryViewModel.cs:459-469,481-528: counts/footer and visible rows describe different populations. Trigger: Mine has ten live rows, search matches none: the Live pill stays ten; turning Mine off leaves the old "Your pursuits" footer. A province/Removed filter on Future Projects leaves the status counting other provinces and lifecycle states. Who sees it: lead.
  Fix shape: calculate visible counts from shared predicates, label broader totals explicitly, and refresh footer text when filters change.
- M5 BdTrackingViewModel.cs:112-123: one Initiator choice conflates two people. Trigger: choose "John" with rows for John Markulin and John Bryson; both satisfy StartsWith("John "), so neither person can be isolated. Who sees it: lead and manager.
  Fix shape: bind choices to unique staff IDs and display full names.
- M6 AttributionViewModel.cs:71-75,99-102,134-136: scorecard prose and source labels contradict or obscure the totals. Trigger: five attributed wins and zero losses produce "The 0 wins recorded so far..."; separate Grab.Forward and Grab.APC groups both become identically named "Grabbed in the app" rows without aggregation. Who sees it: manager.
  Fix shape: describe unattributed wins as a subset, avoid inventing their source from missing ownership, and aggregate or retain distinct source labels.
- M7 EventsViewModel.cs:135-143: prose prefixes masquerade as researched verdicts. Trigger: "Highway suppliers only; no building buyers" becomes Tier 1; "Lower Mainland school board buyers attend" becomes Watch. Who sees it: lead.
  Fix shape: store a structured verdict or require an exact verdict token and delimiter; otherwise use the fallback score.
- M8 SqlPursuitLifecycleStore.cs:91: forward birth keys are not guaranteed unique. Trigger: release/regrab the same MPI while both SQL clock readings format to the same millisecond; the archived engagement retains that ExternalSourceKey and the supplied unique index rejects the new grab. Who sees it: lead, as an action failure.
  Fix shape: use a unique generated identifier or sequence rather than formatted wall-clock time.

## Minor
- m1 StaffDisplay.cs:23-38: "loaded" also means "still loading", and ambiguous legacy keys silently use the first directory row. Trigger: a second window awaits EnsureLoadedAsync during the first request and proceeds with login fallbacks; no notification follows publication. Two staff records sharing StaffKey "John" make bare "John" resolve according to return order. Full distinct UPNs do not have that collision.
  Fix shape: share the in-flight loading task, notify/refresh dependants, and reject ambiguous aliases.
- m2 PursuitLifecycleIntegrationTests.cs:27-28,67-68,114-115,201-202,276-277: absent database configuration produces successful no-op tests. Trigger: run this suite without KOR_OPPORTUNITIES_OPPORTUNITIESDB; no lifecycle assertions execute.
  Fix shape: report explicit skips or fail the dedicated integration gate when its required database is unavailable.

## Rule misreads (attack 11)
These are synthetic adversarial inputs derived from the opened regexes, not claims about existing database rows. The first ten should be shown; the last ten should be hidden.
- "Structural steel design services for a new secondary school" | hidden | shown | KorBidFit.cs:31,54: NeverOurs "structural\s+steel" precedes Structural.
- "Prime consultant services - Heritage Library seismic retrofit and abatement" | hidden | shown | KorBidFit.cs:31,54: NeverOurs "abatement" precedes seismic.
- "Prime consultant services - Main Street Elementary School Replacement" | hidden | shown | KorBidFit.cs:37,56: NotOurs "street\w*".
- "Architectural and engineering services - New Data Centre Building" | hidden | shown | KorBidFit.cs:37,56: NotOurs "data\s+cent\w*".
- "Prime consultant services - New Mental Health Hospital" | hidden | shown | KorBidFit.cs:44,56: NotOursConsulting "mental\s+health".
- "Prime consultant services - Heritage School Expansion" | hidden | shown | KorBidFit.cs:44,56: NotOursConsulting "heritage".
- (Civic, "Substation Road Community Hall") | hidden | shown | KorProjectFit.cs:25,42: Energy "substation", including the street name.
- (Infrastructure, "Riverbend Library Expansion") | hidden | shown | KorProjectFit.cs:33,43-44: Building lacks "library".
- (Transportation, "Airport Terminal Expansion") | hidden | shown | KorProjectFit.cs:33,43-44: Building lacks "terminal".
- (Industrial, "Waste Services Administration Building") | hidden | shown | KorProjectFit.cs:37,44: Plant "waste" overrides the office building.
- "Seismic sensors - supply and delivery" | shown | hidden | KorBidFit.cs:34,55: Structural "seismic" immediately returns true.
- "Structural inspection of sanitary sewer chambers" | shown | hidden | KorBidFit.cs:34-37,55: Structural returns before sanitary/sewer exclusions.
- "Structural design services - Highway 1 overpass" | shown | hidden | KorBidFit.cs:31,34,55: no NeverOurs match; Structural overrides highway/overpass exclusions.
- "RFP - Supply of microscopes to North School" | shown | hidden | KorBidFit.cs:48,56: Ours "rfp ... school"; no supply exclusion.
- ("Infrastructure / Utilities", "Eastside Trunk Sewer Renewal") | shown | hidden | KorProjectFit.cs:29,45: anchored InfrastructureSector misses the composite; "return true".
- (Energy, "Ridge Wind Turbine Array") | shown | hidden | KorProjectFit.cs:25,29,45: Energy lacks "wind turbine"; sector unmatched; "return true".
- (Mining, "Copper Pit Expansion") | shown | hidden | KorProjectFit.cs:29,45: sector unmatched; "return true".
- (Transport, "Highway 1 Interchange Reconstruction") | shown | hidden | KorProjectFit.cs:29,45: only "transportation"/"transit" match; "return true".
- (Utilities, "Water Treatment Facility Expansion") | shown | hidden | KorProjectFit.cs:33,37,44: Building "facility", but Plant lacks water treatment.
- (Infrastructure, "Water Supply Pipeline to School Road") | shown | hidden | KorProjectFit.cs:25,33,37,44: Building "school"; water pipeline matches neither Energy nor Plant.

## Not covered by any test (part 1)
Coverage claims here apply only to the permitted PursuitLifecycleIntegrationTests.cs; other tests were outside the reading set. Findings are static, not database reproductions; file references use the unique basenames in that set.
- 1: No ambiguous towns/phases, tied best matches, missing province or wrong-buyer joins; the forward fixture uses a GUID-tagged matching project (133,150).
- 2: No mixed-provenance files, fetches, reviews, prompt runs or requirements, equal timestamps, app/SQL clock skew, or human next-step text collisions; rejection fixtures cover an ask and its next step (230-249).
- 3: No parallel tender grabs (53-56 is a sequential retry), concurrent forward/tender creation, Director/Workspace accept/reject, or reaper/next-step contention. Batches leave isolation at the connection default (READ COMMITTED assumed, database options uninspected): tender UPDATE takes write locks, Join holds UPDLOCK/HOLDLOCK on E, matching is outside the transaction, Accept has no explicit settlement lock, and reaper eligibility has no held E lock.
- 4: No injected failure after each statement to prove full rollback, commit-acknowledgement loss, or commit/send crash/failure; reassignment checks only ordinary movement and stale owner (83-97).
- 5: No equal AtUtc ordering, re-join after rejection, Kept then new Grab/Join, rejected joins in New grabs, NULL EngagementId log rows, or stale decisions acting on a newer cycle.
- 6: No dismiss/restore sequence, submitted-join rejection, release/regrab/reject cycle, or two Grab links; only ordinary release and preservation of a joined tender are exercised (171-173,292-297).
- 7: The board assertion checks containment of one forward row (140-141), not row uniqueness, two-link identity selection, or omission of live tenders after the seven-day cutoff.
- 8: No same-millisecond ExternalSourceKey collision, repeated grab after release/dismiss/restore, or multiple live same-owner/NULL-Region pursuits linked to one MPI.
- 9: No selection change during awaits, overlapping loads, late identity resolution, stale card actions or notification failure paths; this file has no app fixtures.
