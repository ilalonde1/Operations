# Codex brief: adversarial audit of the BD grab system and the BD screens (2026-10-21)

Reading set: 35 file ranges, 199,744 bytes (about 50k tokens), measured 2026-10-21 with `sed -n a,bp | wc -c` on the
working tree.

**IMPORTANT. Read-only.** Do NOT run `dotnet build`, `dotnet test`, git, or any command that writes. No database, no
network, no shares. Open only the files and line ranges in the two tables below; do not grep, list, search or open
anything else in the repo. Reasoning: medium. Write your answer to
`docs/codex/CODEX-BD-ADVERSARIAL-AUDIT-RESPONSE.md` and stop.

## Your job

You are a hostile reviewer. Find what is wrong; do not summarise or praise what is right. Every finding needs a
concrete trigger: a sequence of actions, a row, a title, a sector. "Could be a problem" without a trigger is not a
finding.

One severity axis. **Critical**: a pursuit, its files or its review is lost, moved or misattributed, or a lead or the
BD manager is shown something false or misses something real, with nothing on screen saying so. **Material**: a
wrong result that is visible. **Minor**: everything else. KOR is a structural engineering firm: a lead who sees road
work, or does not see a school, judges the whole app by it.

## Part 1: the write paths

### What the code does (ground truth; do not re-derive it)

A business-development lead **grabs** an opportunity to pursue it. One verb, two kinds:

- **A tender** (an `Opportunities` row with a closing date) becomes a `CrmEngagements` row with `OpportunityId` set.
  `SqlPursuitGrabStore.GrabAsync`.
- **A forward project** (a `MajorProjectsInventory` row, years before any RFP) becomes a `CrmEngagements` row with
  `OpportunityId` NULL, `Stage` 1, `ExternalSource` 'Grab.Forward', `ExternalSourceKey` 'MPI.<id>.<timestamp>',
  `Region` NULL, plus a `CrmEngagementProjectLink` row (`MatchedBy` 'Grab'). `SqlPursuitLifecycleStore.GrabProjectAsync`.
- **Join.** When a tender is grabbed, `FindForwardMatchAsync` scores live forward pursuits in the same province with
  `ProjectNameMatch` (confident at 0.60). On a match the tender joins that pursuit: the forward owner becomes the
  tender's owner automatically and a 'Join' row is logged. The forward owner later accepts ('JoinAccepted') or
  rejects. **Reject** (`RejectJoinAsync`) splits the tender out as the grabber's own engagement ('Grab.Unjoined') and
  moves every row created at or after the join time: `OpportunityFiles`, `PursuitDocumentFetch`, `PursuitReview`,
  `BdPromptRun` (rfp), `PursuitRequirement`, the review's `BdAsks` (`AskedBy LIKE 'Claude (RFP review%'`) and the
  review-set next step. It logs 'JoinRejected' then 'Grab'.
- **New grabs** (`RecentGrabsAsync`): 'Grab'/'Join' rows from the last 14 days by others, not followed by 'Kept' or
  'Reassign'. **Pending joins** (`PendingJoinsAsync`): 'Join' rows not followed by 'JoinAccepted'/'JoinRejected'.
- **Reassign** (`SqlPursuitOverwatchStore`) moves the owner, and `MajorProjectsInventory.OwnerStaffId` through the
  Grab link. The app then emails the new owner automatically (the only email this system sends).
- **Release / Dismiss** of a forward project archives its grab engagement (Stage 8 + a StageHistory row) unless a
  tender has joined it.
- **MpiOwnershipReaperJob** (nightly) releases only quiet grabs: no tender, no future next step, not updated in @days;
  it archives the pursuit. `BdMorningReportJob` warns owners about the same set four days early.

Schema facts (live database, 2026-10-21):

| object | fact |
|---|---|
| `UX_CrmEngagements_Opportunity` | UNIQUE (OpportunityId) WHERE OpportunityId IS NOT NULL |
| `UX_CrmEngagements_ExternalKey` | UNIQUE (ExternalSource, ExternalSourceKey) WHERE both NOT NULL |
| `UX_CrmEngagements_BdRelationship` | UNIQUE (BuyerCanonicalOrgId, OwnerStaffId, Region) WHERE OpportunityId IS NULL AND all three NOT NULL AND Stage <> 8 |
| `UX_CrmEngagementProjectLink_Pair` | UNIQUE (EngagementId, MajorProjectsInventoryId) |
| `OpportunityAssignmentLog` | Id, OpportunityId?, EngagementId?, Action, FromStaffId?, ToStaffId?, ByStaffId?, AtUtc, Reason?, MpiId? (no unique index besides the PK) |
| stages | 1 = not submitted, 3 = submitted, 6 = won, 7 = lost, 8 = archived |
| owners | 161 of 161 live pursuits (stage 1 or 3) store `OwnerStaffId` as a full UPN |

### Reading set, part 1

| file | lines | why |
|---|---|---|
| Kor.Opportunities.Data/Crm/SqlPursuitGrabStore.cs | all | grab, join, accept, reject, keep, the "not followed by" queries |
| Kor.Opportunities.Data/MajorProjects/SqlPursuitLifecycleStore.cs | all | forward grab, release, dismiss, restore |
| Kor.Opportunities.Data/Crm/SqlPursuitOverwatchStore.cs | all | reassign, the manager board |
| Kor.Opportunities.Data/Crm/PursuitRfpReview.cs | 30-45, 525-600 | the board row and its SQL (forward rows via OUTER APPLY) |
| Kor.Opportunities.Worker/Jobs/MpiOwnershipReaperJob.cs | all | nightly release |
| Kor.Opportunities.Worker/Services/Reporting/BdMorningReportJob.cs | 180-215, 395-420 | the early warning's query and text |
| Kor.Operations.App/BusinessDevelopment/Director/DirectorActions.cs | all | accept, reject, keep, give from the Director |
| Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.Home.cs | all | loading and acting on grabs and joins |
| Kor.Operations.App/BusinessDevelopment/Workspace/BdWorkspaceWindow.Today.cs | all | the lead's accept and reject |
| Kor.Operations.App/Opportunities/OpportunitiesView.xaml.cs | 325-360 | the Grab button and its join message |
| Kor.Operations.App/Opportunities/MajorProjectsInventoryViewModel.cs | 160-240 | forward grab, release, dismiss from the list |
| Kor.Opportunities.Data.Tests/PursuitLifecycleIntegrationTests.cs | all | what is already tested, so you can say what is not |

### Attacks, part 1 (at least these)

1. **Wrong join.** Two different projects in one province whose names score at least 0.60 ("Fire Hall Replacement"
   in two towns; "Phase 2" of two developments). Can the grabber or the forward owner tell before the owner changes?
2. **Reject ordering and scope.** Does `RejectJoinAsync` free the forward engagement's `OpportunityId` before it
   creates the split engagement, inside one transaction? Which rows does "created at or after the join time" wrongly
   move or leave behind: rows the forward owner added after the join, rows in the same second, rows stamped by the
   app clock rather than SQL's?
3. **Races.** Two leads grab one tender at once; a forward grab and a tender grab of one project at once; accept and
   reject clicked in the Director and the Workspace at once; the reaper running while a lead sets a next step. Name
   the isolation level and lock that does or does not stop each.
4. **Transactions and email.** Is every multi-statement write one transaction that rolls back whole? Can the
   reassignment email go out for a reassignment that rolled back, or fail to go out for one that committed?
5. **"Not followed by".** Ties on `AtUtc`; a re-join after a reject; 'Kept' then a new 'Grab'; log rows with NULL
   `EngagementId`. Can a pending join or new grab vanish, or come back after it was settled?
6. **Archive and restore.** Release or dismiss after a tender joined; restore after dismiss; a forward pursuit with
   two link rows. Can a live pursuit end up archived, or an archived one keep the project?
7. **Board duplicates.** Can the board's OUTER APPLY or joins return one engagement twice, or drop one?
8. **Keys.** Can two 'MPI.<id>.<timestamp>' keys collide or a regrab fail? Does `Region` NULL let two live forward
   pursuits exist for one project and owner?
9. **App side.** After each await, is the selected or shown item re-checked before the screen or database changes?
   (The class that corrupted pursuits on 2026-05-16: a stale selection after an await.)
10. **Tests.** Which attacks above does no integration test exercise?

## Part 2: what the screens show

### What the code does (ground truth; do not re-derive it)

- **KorBidFit.IsKorWork(title)**: regexes in order NeverOurs, Structural, NotOurs and NotOursConsulting, Ours.
  Tuned and measured on 581 hand-labelled open tenders (precision 63/66, recall 63/67) and 732 archive titles
  (175/187, 175/176).
- **KorProjectFit.IsKorProject(sector, name)**: an energy name is out; a project in an infrastructure, utility,
  transport, industrial, economic-development, cross-sector or "other sectors" sector shows only if its name says
  building and not plant; any other sector is trusted. On all 2,616 active projects: 2,533 shown, 83 hidden, none of
  the hidden from a building sector. Future Projects applies it by default ("KOR markets only").
- **Market History** ("KOR markets only"): SQL query, then a C# post-filter `KorBidFit.IsKorWork`. 188 rows on
  2026-10-21.
- **MajorProjectRow.PlainStage** turns stage codes (CapitalPlan, NOE, NOD, filed) into words; the stage filter offers
  the words and compares both the word and the raw values.
- **Names**: `StaffDisplay` loads the BD staff directory once per session (StaffKey and Mailbox to DisplayName),
  falling back to the part before the @. Two people can share a first name (John Markulin, John Bryson).
- **My Pursuits** (`CrmViewModel`): "Mine" on by default; with no signed-in actor it shows everyone's. Pills count
  what "Mine" lets through. A stage-1 row reads "Out for bid" with a tender that has a closing date, else "No RFP yet".
- **All Pursuits** (`OverwatchRowView`): the same stage words, from the board query's closing date.
- **Engagements** (`BdTrackingViewModel`): the Initiator filter offers first names; rows hold UPNs; it matches the
  first name of the person the UPN belongs to.
- **Events**: tier from the researched verdict at the start of "Why" (Highest or HIGH: Tier 1, MEDIUM: Tier 2, LOW:
  Watch), else a keyword score. Verdict heads seen on 2026-10-21: "Highest ", "HIGH - ", "MEDIUM - ", "LOW - "; others
  start with prose ("Core KOR...", "The procurement...") or are empty. A one-day event has no EndDate; the list keeps
  events whose end date, or start date if none, is today or later.
- **Scorecard** (`AttributionViewModel`): wins by source in plain words; wins by person excludes "(unattributed)".
- **BD Director Today**: join and new-grab cards, and "It comes down to N questions" points pulled from Claude's RFP
  review text, citations stripped by `Uncited`.

### Reading set, part 2

| file | lines | why |
|---|---|---|
| Kor.Opportunities.Data/Ingestion/KorBidFit.cs | all | the tender rule |
| Kor.Opportunities.Data/MajorProjects/KorProjectFit.cs | all | the future-project rule |
| Kor.Opportunities.Data/MajorProjects/ProjectNameMatch.cs | all | the matcher behind joins |
| Kor.Opportunities.Data/MajorProjects/MajorProjectRow.cs | 60-110 | PlainStage |
| Kor.Opportunities.Data/HistoricalOpportunities/SqlCompetitionInfoQueryStore.cs | 28-148 | Market History query and post-filter |
| Kor.Opportunities.Data/IndustryEvents/SqlIndustryEventStore.cs | 84-110 | which events are upcoming |
| Kor.Operations.App/BusinessDevelopment/Director/BdDirectorHomeViewModel.cs | 30-75, 340-370, 420-520, 565-695 | Today's cards |
| Kor.Operations.App/Crm/CrmViewModel.cs | 100-235, 387-470, 800-840 | Mine, pills, load, history line |
| Kor.Operations.App/Crm/CrmEngagementRowView.cs | all | stage words |
| Kor.Operations.App/BusinessDevelopment/StaffDisplay.cs | all | names |
| Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchRowView.cs | all | All Pursuits row |
| Kor.Operations.App/BusinessDevelopment/Workspace/AttributionViewModel.cs | all | Scorecard |
| Kor.Operations.App/BusinessDevelopment/Workspace/EventsViewModel.cs | 75-160 | event tiers |
| Kor.Operations.App/Crm/BdTrackingViewModel.cs | 75-130 | Initiator filter |
| Kor.Operations.App/Opportunities/MajorProjectsInventoryViewModel.cs | 325-350, 450-530 | KOR markets toggle, status, filter |

### Attacks, part 2 (at least these)

11. **Rules, both directions.** Ten realistic titles or (sector, name) pairs a structural firm WOULD bid that the
    rules hide, and ten road, utility, energy or supply items they SHOW. Quote the deciding branch. Look hardest at
    word boundaries, sector strings the anchored regex misses ("Infrastructure / Utilities", "Energy", "Mining"), and
    Plant or Energy words that also name buildings or streets.
12. **Market History.** Does the post-filter run after a TOP, paging or row cap, so the list or its count is silently
    short? What does "188 rows" then mean?
13. **Stage words.** A tender whose closing date has passed still "Out for bid"? A forward pursuit with a joined
    tender? PlainStage on acronyms, empty strings, odd casing; a filter choice that matches nothing.
14. **Names.** The directory load racing the first screen; one key for two people; a person who left; a UPN in
    another case; a first-name filter matching two people.
15. **Mine and the pills.** The actor set after the first load; a deep link to someone else's pursuit; search plus
    Mine plus a pill. Can pills, list and footer disagree?
16. **Event tiers and dates.** Prose that starts with a verdict word by accident; a multi-day event with only a start
    date; a date-only column against a time-zone-aware "today".
17. **Scorecard.** Can the tiles, the source table and the by-person table be read as contradicting each other?
18. **Director cards.** Review text where `Uncited` or the points extraction mangles a question, drops one, or keeps a
    citation.

## Response format (in the RESPONSE file; at most 150 lines)

```
## Ship-blocker
<the one finding that must be fixed before this reaches the leads, in two sentences>

## Critical
- C1 file:line: what breaks or is shown wrongly. Trigger: <sequence or input>. Who sees it: <lead / manager>.
  Fix shape: <one line>.

## Material
- M1 ...

## Minor
- m1 ...

## Rule misreads (attack 11)
- title or (sector, name) | shown or hidden | should be | deciding branch

## Not covered by any test (part 1)
- attack number: one line each
```

Cite only lines you opened. If a line number is uncertain, quote the code instead of guessing a number.
