# BD hub kind split — implementation and verification, 2026-10-09

Implemented **Signals / Bids / Mine** in the local Debug app. The app was built and run against the live database. The production app binaries were not deployed.

Prior art checked: the supplied brief; audit `08-bd-app-surface.md` sections 5(d), 5(h), 5(i), and 9; the existing hub, Bazaar selector, source enum, source mappings, store interfaces, deep-link path, and verification tools under `tools/`, `docs/`, and `../KOR.Drafter/db/tools/`. The audit already identifies the unrelated naming, Reports, no-op-button, and empty-state work; those remain outside this change.

Two constraints needed, and received, explicit session approval:

- A read-only paged hub query: the existing registry reader hard-caps results at 5,000, and has no bulk source metadata read. The new query uses 1,000-ID pages, a fixed upper ID boundary, and includes undismissed closed/awarded records. Existing `ListAsync` behavior is unchanged.
- Explicit `bd.kind` source metadata for mixed transports. `GenericJson` actually carries applications, bids, and awards; RSS carries both bids and news. Migration `337_OpportunityHubSourceKinds.sql` was applied to the live database: 45 display-only mappings (3 application, 39 bid, 1 award, 2 other). No provider consumes this key. Existing operator mappings are preserved.

The UI uses `OpportunitySourceType`, with source metadata only for mixed transports. It contains no source-name list. The migration records the initial reviewed inventory; a newly configured mixed source is safely unclassified until its metadata is set.

| SourceType | Kind and segment |
|---|---|
| 20 ArcGisFeatureService, 21 TempestProspero, 22 EngagementHq | Application / rezoning → Signals |
| 4 CivicInfoHtml, 6 SamGov, 8 BcBid, 10 BidsAndTenders, 11 AlbertaPurchasingConnection, 19 MerxDcc | Bid / RFP → Bids |
| 9 BcBidAwards, 12 BidsAndTendersAwards, 13 AlbertaPurchasingConnectionAwards, 14 BcBidUnverifiedBidResults, 15 BcBidHistorical, 16 GenericCsvAward, 17 GenericJsonAward | Award / bid history → Bids |
| 1 GenericCsv, 2 GenericJson, 3 Rss, 5 GraphEmail, 99 Manual | Explicit `bd.kind`: application → Signals; bid/award → Bids; missing/invalid/other → Signals, visibly Other / unclassified |
| 0 Unknown, 7 BdOutreach, 18 MajorProjectsInventory, every unrecognised numeric SourceType, missing observations | Signals, visibly Other / unclassified |

Unknown types are never asserted to be applications or bids. Cross-posted observations count once; contradictory known kinds become unclassified. Disabled sources still contribute metadata to their existing records.

Signals defaults to newest filed/posted date first, with unknown dates last and first-seen time as the secondary fallback. Bids puts upcoming deadlines first, then expired, undated, and award/history rows. Mine matches the current actor using the existing signed-in UPN → override → Windows-user convention, and sorts by most recently updated. Each row displays its kind and date. The original source action now says **Open source**.

`To grab` is a checkbox inside Signals and Bids. It hosts the real `BazaarView` and retains `BazaarOpportunitySelector` eligibility and duplicate selection within the selected kind. Clearing it returns to the real `OpportunitiesView`. Unknown dates, expired bids, and awards remain reachable in the full segments. Deep links reveal the destination kind and clear conflicting filters.

The measured total was **8,836 undismissed rows**, not the brief's earlier 8,976. No opportunity rows were changed by this work.

| Before, measured using the unchanged production reader and selector | Rows |
|---|---:|
| Old registry loaded, before its client filters | 5,000 (hard cap) |
| Old registry with default prime-only filter | 29 |
| Old To grab | 3,966 (overlaps registry) |
| Database's complete undismissed population | 8,836 |

| After, measured both in the running app and the live read test | Rows |
|---|---:|
| Signals: applications / rezonings | 5,795 |
| Signals: Other / unclassified | 176 |
| **Signals total** | **5,971** |
| Bids: bids / RFPs | 2,785 |
| Bids: awards / bid history | 80 |
| **Bids total** | **2,865** |
| **Mine, signed in as Ian** | **1** |
| **Unique total** | **8,836** |

Mine is deliberately an overlapping personal view, and the header states that. Signals + Bids = 5,971 + 2,865 = **8,836**. A disjoint reconciliation is Signals 5,971 + Bids excluding Mine 2,864 + Mine 1 = **8,836**. Adding Mine to both complete kind segments would double-count the owned bid.

Running-app verification used Windows UI Automation to invoke the actual controls and inspect DataGrid row counts; screenshots were captured from the running WPF window and visually inspected:

1. Home → Business Development → Opportunities: [Signals](evidence/bd-hub-2026-10-09/signals-final.png), 5,971 rows, newest filed dates first.
2. Bids: [Bids](evidence/bd-hub-2026-10-09/bids-final.png), 2,865 rows, upcoming deadlines ordered October 9, 11, 12, 13. Awards / bid history filter: 80 rows.
3. Mine: [Mine](evidence/bd-hub-2026-10-09/mine-final.png), Ian's one owned bid; To grab is absent.
4. Signals → Applications / rezonings: 5,795 rows. Signals → Other / unclassified: [176 rows](evidence/bd-hub-2026-10-09/other-final.png).
5. Signals → To grab: [Bazaar with 4,752 rows](evidence/bd-hub-2026-10-09/signals-to-grab.png). While in Bazaar, selecting Other / unclassified showed 41 rows and updated its status to 41. Clearing To grab returned to the registry.
6. Bids → To grab: [Bazaar with 621 rows](evidence/bd-hub-2026-10-09/bids-to-grab.png). Switching back to Mine restored the registry and its one row.

No live claim/dismissal was performed just to test a write. The regression test exercises the real Bazaar VM's claim result with mocked stores, then switches kinds and verifies the claimed row cannot reappear. Deep-link reveal is tested on an isolated STA thread with a ten-second bound.

Debug builds passed with **0 warnings, 0 errors**:

```powershell
dotnet build Kor.Operations.App/Kor.Operations.App.csproj -c Debug --no-restore -nologo -v quiet
dotnet build Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj -c Debug --no-restore -nologo -v quiet
```

The final filtered run passed **24 tests**:

```powershell
dotnet test Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj -c Debug --no-build --filter 'FullyQualifiedName~OpportunityHubTests|FullyQualifiedName~OpportunityHubLiveTests|FullyQualifiedName~BazaarOpportunitySelectorTests|FullyQualifiedName~OrphanedPublicTypeTests|FullyQualifiedName~XamlBindingPathTests' -nologo --logger 'console;verbosity=normal'
```

The live test's `KOR_HUB_VERIFY_DB` process environment variable was populated directly from the authorized remote registry value, then removed. No connection string was printed or saved. Without that variable the live test explicitly skips. Its `PagedHubReachesEveryUndismissedRowExactlyOnce` assertion compared **every returned ID** to an independent uncapped SQL query, including order and uniqueness. It does not certify the correctness of manually configured source-kind metadata or perform UI/write testing.

Additional named checks include `MixedTransportsRequireExplicitMetadataAndUnknownTypesRemainOther`, `ConflictingObservationsAreNotMisrepresentedAndCrossPostsCountOnce`, `SignalsAndBidsPartitionEveryKindIncludingUnknownAndMineIsAnOverlap`, `SignalsUseFiledDatesNotRecentIngestionAndLeaveUnknownDatesLast`, `BidsSortLiveDeadlinesBeforeExpiredUndatedAndAwards`, `BazaarKindSwitchDoesNotRestoreAGrabbedRow`, and `DeepLinkRevealsItsKindAndClearsConflictingFilters`.

`OrphanedPublicTypeTests` also passed before the changes (3 tests). `BdIntegrityCheck` ran before and after the metadata insert; all **98 result lines matched**, including the pre-existing `opportunity_merged_across_sibling_sources` error (1), 39 warning worklists, and zero check failures. No audit-register item was marked verified or closed.

Unchanged: the entire FIND/PURSUE/KNOW/TOOLS rail; Pursuits / Pursuit Monitor / BD Tracking; Reports navigation; no-op buttons; empty states; the separately briefed em-dashes and action-name mismatches; Future Projects / MPI functionality; schema structure; ingestion; providers; relevance gates; existing claim/dismissal transactions; and the old capped reader used outside this hub. Both existing views remain reachable and their existing actions remain wired. Three unrelated untracked files present at session start were left untouched.
