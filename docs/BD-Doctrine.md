# BD Data Doctrine — binding invariants

Every invariant below is ENFORCED BY A TEST or a runtime guard — not by memory,
comments, or review vigilance. If you violate one, the build goes red or the
Worker refuses to start. That is the point: fixes decayed for months because
nothing re-asserted them; these do.

Run: `dotnet test Kor.Opportunities.Data.Tests` — the doctrine suite runs with
every test pass. Escapes require a justified line in
`Kor.Opportunities.Data.Tests/doctrine-allowlist.txt` (file|fragment|reason),
which is itself reviewable history.

| # | Invariant | Enforced by |
|---|-----------|-------------|
| D1 | A canonical-org FK is never blind-overwritten by SQL: every `<X>CanonicalOrgId = @param` SET assignment is COALESCE/CASE-guarded (fill-only or name-paired), or explicitly allowlisted with a reason. A resolver miss must never null a good link. | `DoctrineTests.CanonicalFkWrites_AreGuarded` |
| D2 | Every `*CanonicalOrgId` column that appears in Schema/*.sql is either registered in `CanonicalColumnRegistry` (wheel repairs it, audit measures it, dedup repoints it) or listed as a documented exclusion (child-row FKs with no paired name column). Adding a column without deciding is a red build. | `DoctrineTests.EveryCanonicalFkColumn_IsRegisteredOrExcluded` |
| D3 | Every Quartz job registered in Worker Program.cs appears in `ScheduledJobDefinitions` (Admin visibility: see, disable, reschedule) or is allowlisted with a reason. Invisible schedules are how the wheel and IntelRetirement went dark. | `DoctrineTests.EveryQuartzJob_IsInScheduleRegistry` |
| D4 | The shared junk guard cannot silently weaken: the corpus of placeholder/multi-firm strings that once polluted the org graph ("no", "TBD", "Not publicly confirmed", "Firm A; Firm B (role)") must always be rejected by `TeamNameCleaner`/the resolver denylist, and real firms must always survive it. | `DoctrineTests.JunkCorpus_NeverResolves` + `RealFirms_AlwaysSurvive` |
| D5 | The registry matches the live schema or the Worker does not start. | `CanonicalColumnRegistry.StartupVerifyAsync` (fail-closed, Program.cs) |
| D6 | One live CanonicalOrg per strict NormalizedName — a live twin is physically impossible, not merely guarded. | Unique filtered index `UX_CanonicalOrg_LiveNormalizedName` (migration 279) |
| D7 | Person identity flows through `usp_ResolveOrCreateIntelPerson` (email → linkedin → name+org anchor). Retired people resurrect on re-discovery (no 2627 poison records); guessed/shared emails never anchor identity; verified email displaces guessed. | proc 277 (TOCTOU-guarded); generators emit contract keys |
| D8 | Firehose sources (createArchived) resolve-but-keep-cold: they may reuse an archived org's identity but never resurrect it. Curation retirements stand — reused cold, never twinned, never revived. | Resolver tier + `CanonicalOrgResolverTests` (firehose/curation/merge-loser pinning tests) |
| D9 | Scheduling plane ownership is data (`OpportunitySources.QuartzManaged`), never a name list in code. Failed sources back off a full crawl window. A provider-name miss throws; it never runs the wrong parser silently. | Migration 281 + scheduler SQL + dispatcher throw |
| D10 | Failures are red and rot is delivered: jobs rethrow instead of logging-and-green; DEAD-GREEN sources and registry orphan-rates land in the 6am morning email, not a server-local file. | DataHealthAuditJob rethrow + BdMorningReportJob sections |
| D11 | "Actionable" is ONE predicate: `vw_ActionableProjects` / `vw_ActionableOpportunities` (live, un-dismissed, un-owned, seat not filled/locked — migration 282). Every surface (weekly sheet, boards, digests, reports) reads the views; a Worker job re-deriving lifecycle predicates inline is a red build. Human dismissal (`DismissedAtUtc/By/Reason`) is distinct from system staleness (`RetiredAtUtc`) and is never a delete. Ownership is race-safe and always audited (`OpportunityAssignmentLog`, `MpiId` for projects); the reaper auto-releases unconverted plays after 14 days (digest warns from day 10). | Migration 282 views + `DoctrineTests.D11_*` + `SqlPursuitLifecycleStore` + `MpiOwnershipReaperJob` |

## The doctrine in one sentence each

- **Fill-only:** existing canonical links win; new resolution fills gaps; a miss never destroys.
- **Name and FK move together:** a row must never display one firm while linking to another.
- **One registry:** adding a canonical column is ONE entry; forgetting is a loud failure, not silent rot.
- **Keep-cold:** high-volume feeds reference archived identity without warming it.
- **Curation is final:** a human retirement is never undone by an automated path.
- **Junk dies at the door:** placeholder text never becomes an organization.
- **Everything visible:** every schedule in the registry, every failure red, every health number delivered.

## Changing the doctrine

These invariants are changeable — deliberately, in one place, with the test
updated in the same commit. What is forbidden is drifting past them silently.
